using Xunit;
namespace PAL.Tests;

public class PpmWriterTests
{
    [Fact]
    public void WritesValidP6HeaderAndPixels()
    {
        // 2x2 RGBA image with distinct pixels.
        var rgba = new byte[]
        {
            255, 0, 0, 255,    0, 255, 0, 255,
            0, 0, 255, 255,    255, 255, 255, 255,
        };
        string path = Path.Combine(Path.GetTempPath(), $"pal-{Guid.NewGuid():N}.ppm");
        try
        {
            PpmWriter.Write(path, 2, 2, rgba);
            var bytes = File.ReadAllBytes(path);

            // Header: "P6\n2 2\n255\n"
            var header = System.Text.Encoding.ASCII.GetString(bytes[..11]);
            Assert.Equal("P6\n2 2\n255\n", header);

            // Pixel payload: RGB triples in row order.
            var payload = bytes[11..];
            Assert.Equal(12, payload.Length);
            Assert.Equal(new byte[] { 255, 0, 0 }, payload[0..3]);
            Assert.Equal(new byte[] { 0, 255, 0 }, payload[3..6]);
            Assert.Equal(new byte[] { 0, 0, 255 }, payload[6..9]);
            Assert.Equal(new byte[] { 255, 255, 255 }, payload[9..12]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FramePixelsExposesDimensionsAndPixels()
    {
        var rgba = new byte[3 * 2 * 4]; // 3x2
        rgba[0] = 10;
        rgba[1] = 20;
        rgba[2] = 30;
        rgba[3] = 255;
        var frame = new FramePixels(3, 2, rgba);

        Assert.Equal(3, frame.Width);
        Assert.Equal(2, frame.Height);
        var color = frame.GetPixel(0, 0);
        Assert.Equal(10 / 255f, color.R, 5);
        Assert.Equal(20 / 255f, color.G, 5);
        Assert.Equal(30 / 255f, color.B, 5);
        Assert.Equal(1.0f, color.A, 5);
    }

    [Fact]
    public void GetPixelValidatesBounds()
    {
        var frame = new FramePixels(2, 2, new byte[2 * 2 * 4]);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetPixel(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetPixel(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.GetPixel(-1, 0));
    }

    [Fact]
    public void SavePpmRejectsBadPaths()
    {
        var frame = new FramePixels(1, 1, new byte[4]);
        Assert.Throws<ArgumentException>(() => frame.SavePpm(""));
        Assert.ThrowsAny<ArgumentException>(() => frame.SavePpm(null!));
    }

    [Fact]
    public void RoundTripsThroughFile()
    {
        var rgba = new byte[2 * 1 * 4];
        rgba[0] = 1;
        rgba[1] = 2;
        rgba[2] = 3;
        rgba[3] = 255;
        rgba[4] = 4;
        rgba[5] = 5;
        rgba[6] = 6;
        rgba[7] = 128;

        string path = Path.Combine(Path.GetTempPath(), $"pal-{Guid.NewGuid():N}.ppm");
        try
        {
            var frame = new FramePixels(2, 1, rgba);
            frame.SavePpm(path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
