using System.Buffers.Binary;

namespace BallGM.Mods.Cosmetics;

/// <summary>What is wrong with an image file, as far as reading its header can tell.</summary>
public enum ImageProblem
{
    None,
    Missing,
    Empty,
    Unreadable,
    NotPngOrJpeg,
    TooWide,
    Oversized,
}

/// <summary>An image file's header, read without decoding the image.</summary>
public sealed record ImageInspection(ImageProblem Problem, string? Format, int Width, int Height, long Bytes, bool ExtensionMismatch)
{
    public bool Usable => Problem is ImageProblem.None or ImageProblem.Oversized;
}

/// <summary>
/// Reads just enough of an image file to say whether the client will be able to draw it — its
/// signature and its dimensions — without decoding it. Fast enough to run over a five-thousand-face
/// pack on every check: it reads at most the first 64 KB of each file.
/// <para>
/// The checks are the client's robustness matrix moved to before the game starts: a missing, empty,
/// mislabelled, unreadable or absurdly proportioned file is reported here, by pack and by count,
/// rather than discovered one fallback silhouette at a time.
/// </para>
/// </summary>
public static class ImageFileInspector
{
    /// <summary>Past this many pixels on a side the client decodes a downscaled copy; worth saying, not worth refusing.</summary>
    public const int LargestSide = 4096;

    /// <summary>A file this big slows every screen it appears on.</summary>
    public const long LargestBytes = 16L * 1024 * 1024;

    private const int HeaderBytes = 64 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <param name="maximumAspect">The widest width-to-height (or height-to-width) ratio the slot can draw: 2 for a face, 3 for a logo.</param>
    public static ImageInspection Inspect(string path, double maximumAspect)
    {
        byte[] header;
        long length;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new ImageInspection(ImageProblem.Missing, null, 0, 0, 0, false);
            }

            length = info.Length;
            if (length == 0)
            {
                return new ImageInspection(ImageProblem.Empty, null, 0, 0, 0, false);
            }

            using var stream = info.OpenRead();
            header = new byte[(int)Math.Min(HeaderBytes, length)];
            stream.ReadExactly(header);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ImageInspection(ImageProblem.Unreadable, null, 0, 0, 0, false);
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        string format;
        (int Width, int Height)? size;
        if (header.AsSpan().StartsWith(PngSignature))
        {
            format = "png";
            size = header.Length >= 24 ? (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20))) : null;
        }
        else if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            format = "jpeg";
            size = JpegSize(header);
        }
        else
        {
            return new ImageInspection(ImageProblem.NotPngOrJpeg, null, 0, 0, length, false);
        }

        var mismatch = format == "png" ? extension != ".png" : extension is not (".jpg" or ".jpeg");
        if (size is not { Width: > 0, Height: > 0 } dimensions)
        {
            return new ImageInspection(ImageProblem.Unreadable, format, 0, 0, length, mismatch);
        }

        var aspect = (double)Math.Max(dimensions.Width, dimensions.Height) / Math.Min(dimensions.Width, dimensions.Height);
        var problem = aspect > maximumAspect ? ImageProblem.TooWide
            : Math.Max(dimensions.Width, dimensions.Height) > LargestSide || length > LargestBytes ? ImageProblem.Oversized
            : ImageProblem.None;
        return new ImageInspection(problem, format, dimensions.Width, dimensions.Height, length, mismatch);
    }

    /// <summary>The frame size from the first start-of-frame segment, walking segment headers from the start of the file.</summary>
    private static (int Width, int Height)? JpegSize(byte[] data)
    {
        var offset = 2;
        while (offset + 9 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                return null;
            }

            var marker = data[offset + 1];
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            var isFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 7)), BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 5)));
            }

            offset += 2 + segmentLength;
        }

        return null;
    }
}
