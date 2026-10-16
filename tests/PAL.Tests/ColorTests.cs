using Xunit;
namespace PAL.Tests;

public class ColorTests
{
    [Fact]
    public void PresetsHaveExpectedValues()
    {
        Assert.Equal(new Color(1, 1, 1, 1), Color.White);
        Assert.Equal(new Color(0, 0, 0, 1), Color.Black);
        Assert.Equal(new Color(0, 0, 0, 0), Color.Transparent);
        Assert.Equal(new Color(1, 0, 0, 1), Color.Red);
    }

    [Theory]
    [InlineData(255u, 0x000000u, 0f, 0f, 0f, 1f)]
    [InlineData(255u, 0xFF0000u, 1f, 0f, 0f, 1f)]
    [InlineData(128u, 0x00FF00u, 0f, 1f, 0f, 128 / 255f)]
    [InlineData(0u, 0x0000FFu, 0f, 0f, 1f, 0f)]
    public void FromBytesPacksComponents(byte a, uint rgb, float r, float g, float b, float alpha)
    {
        var color = Color.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
        Assert.Equal(r, color.R, 5);
        Assert.Equal(g, color.G, 5);
        Assert.Equal(b, color.B, 5);
        Assert.Equal(alpha, color.A, 5);
    }

    [Fact]
    public void ToLinearMatchesSrgbTransferFunction()
    {
        // Linear segment: c <= 0.04045 -> c/12.92
        var dark = new Color(0.02f, 0.02f, 0.02f).ToLinear();
        Assert.Equal(0.02f / 12.92f, dark.R, 6);

        // Power segment: 0.5 sRGB -> ~0.214 linear
        var mid = new Color(0.5f, 0.5f, 0.5f).ToLinear();
        Assert.Equal(0.2140f, mid.R, 3);

        // White maps to white; alpha passes through.
        var white = new Color(1f, 1f, 1f, 0.25f).ToLinear();
        Assert.Equal(1f, white.R, 6);
        Assert.Equal(0.25f, white.A, 6);
    }

    [Fact]
    public void EqualityAndToString()
    {
        Assert.Equal(Color.Red, Color.Red);
        Assert.NotEqual(Color.Red, Color.Green);
        Assert.True(Color.Red == new Color(1, 0, 0));
        Assert.True(Color.Red != Color.Blue);
        Assert.Contains("Color(", Color.Red.ToString());
        Assert.Equal(Color.Red, Color.FromRgb(0xFF0000));
        Assert.Equal(new Color(1, 1, 1, 0x80 / 255f), Color.FromArgb(0x80FFFFFF));
    }
}
