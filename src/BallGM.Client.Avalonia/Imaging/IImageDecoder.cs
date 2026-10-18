using Avalonia.Media;

namespace BallGM.Client.Avalonia.Imaging;

/// <summary>What a file's header says, read without decoding the pixels.</summary>
public sealed record ImageProbe(int Width, int Height, int FrameCount);

/// <summary>
/// Reads and decodes image bytes. Always called off the UI thread by <see cref="AssetResolver"/>;
/// replaceable so tests can slow it down or watch which thread it runs on.
/// </summary>
public interface IImageDecoder
{
    /// <summary>The header, or null when the bytes are not an image any decoder recognises.</summary>
    ImageProbe? Probe(byte[] bytes);

    /// <summary>Decodes at <paramref name="width"/> x <paramref name="height"/> pixels (already bucketed, never above the source).</summary>
    IImage Decode(byte[] bytes, int width, int height);
}
