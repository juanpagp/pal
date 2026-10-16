namespace PAL;

/// <summary>
/// The pixels of one rendered frame, captured with
/// <see cref="Graphics.CaptureFrame"/>: RGBA8, rows ordered top-down
/// (row 0 is the top row of the image, the PPM convention).
/// </summary>
public sealed class FramePixels
{
    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// The raw RGBA8 pixels: <c>Width * Height * 4</c> bytes, four bytes per
    /// pixel (R, G, B, A), rows top-down.
    /// </summary>
    public byte[] Rgba { get; }

    internal FramePixels(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    /// <summary>Reads one pixel (sRGB-encoded components as rendered).</summary>
    /// <param name="x">Column, 0 at the left.</param>
    /// <param name="y">Row, 0 at the top.</param>
    public Color GetPixel(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        if (x >= Width || y >= Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x), $"Pixel ({x}, {y}) is outside the {Width}x{Height} image.");
        }
        int i = (y * Width + x) * 4;
        return new Color(
            Rgba[i] / 255f,
            Rgba[i + 1] / 255f,
            Rgba[i + 2] / 255f,
            Rgba[i + 3] / 255f);
    }

    /// <summary>
    /// Writes the frame as a binary PPM image (P6, RGB, 255 max) — a
    /// dependency-free format readable by GIMP, ImageMagick, feh and most
    /// analysis tooling.
    /// </summary>
    /// <param name="path">Destination file path (created or overwritten).</param>
    public void SavePpm(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        PpmWriter.Write(path, Width, Height, Rgba);
    }
}

/// <summary>PPM (P6) writing, dependency-free and unit tested.</summary>
internal static class PpmWriter
{
    public static void Write(string path, int width, int height, byte[] rgba)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        stream.WriteByte((byte)'P');
        stream.WriteByte((byte)'6');
        stream.WriteByte((byte)'\n');
        WriteAsciiNumber(stream, width);
        stream.WriteByte((byte)' ');
        WriteAsciiNumber(stream, height);
        stream.WriteByte((byte)'\n');
        stream.WriteByte((byte)'2');
        stream.WriteByte((byte)'5');
        stream.WriteByte((byte)'5');
        stream.WriteByte((byte)'\n');

        // Rows are already top-down; PPM is top-down too: straight copy.
        Span<byte> rgb = stackalloc byte[3];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgb[0] = rgba[i];
            rgb[1] = rgba[i + 1];
            rgb[2] = rgba[i + 2];
            stream.Write(rgb);
        }
    }

    private static void WriteAsciiNumber(FileStream stream, int value)
    {
        foreach (var c in value.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            stream.WriteByte((byte)c);
        }
    }
}
