using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>
/// The real decoder, on SkiaSharp's codec: it decodes by content (a JPEG named .png still reads),
/// recognises animation, and - unlike a plain bitmap load - refuses truncated input instead of
/// drawing half an image. The result is resampled to the bucketed size and handed to Avalonia as a
/// bitmap that owns a copy of the pixels.
/// </summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
    public ImageProbe? Probe(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec is null ? null : new ImageProbe(codec.Info.Width, codec.Info.Height, Math.Max(1, codec.FrameCount));
    }

    public IImage Decode(byte[] bytes, int width, int height)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Not a readable image.");

        // JPEG can decode straight to a fraction of its size; other formats return their full size here.
        var scale = Math.Min(1f, Math.Max((float)width / codec.Info.Width, (float)height / codec.Info.Height));
        var decodeSize = codec.GetScaledDimensions(scale);
        var info = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result != SKCodecResult.Success)
        {
            throw new InvalidDataException($"The image did not decode completely ({result}).");
        }

        using var sized = decoded.Width == width && decoded.Height == height
            ? decoded.Copy()
            : decoded.Resize(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell))
              ?? throw new InvalidDataException("The image could not be resized.");

        return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, sized.GetPixels(), new PixelSize(sized.Width, sized.Height), new Vector(96, 96), sized.RowBytes);
    }
}
