namespace PAL;

/// <summary>
/// An sRGB authored color with floating-point components in [0, 1].
/// </summary>
/// <remarks>
/// <para>
/// Colors are authored the way designers see them (sRGB). PAL converts to
/// linear space where the rendering pipeline expects it (the frame clear
/// color and text vertex colors), so a <see cref="White"/> text drawn over a
/// dark scene appears white.
/// </para>
/// <para>
/// Values outside [0, 1] are preserved (useful for HDR-like intensities in
/// some pipelines) but are not clamped by PAL.
/// </para>
/// </remarks>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>Red component in [0, 1] (sRGB).</summary>
    public float R { get; }

    /// <summary>Green component in [0, 1] (sRGB).</summary>
    public float G { get; }

    /// <summary>Blue component in [0, 1] (sRGB).</summary>
    public float B { get; }

    /// <summary>Alpha component in [0, 1].</summary>
    public float A { get; }

    /// <summary>Creates a color from sRGB float components.</summary>
    public Color(float r, float g, float b, float a = 1.0f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>Opaque white.</summary>
    public static Color White => new(1.0f, 1.0f, 1.0f);

    /// <summary>Opaque black.</summary>
    public static Color Black => new(0.0f, 0.0f, 0.0f);

    /// <summary>Fully transparent black.</summary>
    public static Color Transparent => new(0.0f, 0.0f, 0.0f, 0.0f);

    /// <summary>Opaque red.</summary>
    public static Color Red => new(1.0f, 0.0f, 0.0f);

    /// <summary>Opaque green.</summary>
    public static Color Green => new(0.0f, 1.0f, 0.0f);

    /// <summary>Opaque blue.</summary>
    public static Color Blue => new(0.0f, 0.0f, 1.0f);

    /// <summary>Opaque yellow.</summary>
    public static Color Yellow => new(1.0f, 1.0f, 0.0f);

    /// <summary>Opaque cyan.</summary>
    public static Color Cyan => new(0.0f, 1.0f, 1.0f);

    /// <summary>Opaque magenta.</summary>
    public static Color Magenta => new(1.0f, 0.0f, 1.0f);

    /// <summary>Mid gray (sRGB 0.5).</summary>
    public static Color Gray => new(0.5f, 0.5f, 0.5f);

    /// <summary>Creates a color from 8-bit sRGB components.</summary>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Creates a color from a packed 0xRRGGBB value, opaque.</summary>
    public static Color FromRgb(uint rgb) =>
        FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Creates a color from a packed 0xAARRGGBB value.</summary>
    public static Color FromArgb(uint argb) =>
        FromBytes((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <inheritdoc />
    public bool Equals(Color other) =>
        R == other.R && G == other.G && B == other.B && A == other.A;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    /// <inheritdoc />
    public override string ToString() => $"Color({R:0.###}, {G:0.###}, {B:0.###}, {A:0.###})";

    /// <summary>Determines whether two colors are equal.</summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>Determines whether two colors differ.</summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    /// <summary>
    /// Converts the sRGB components to linear space (alpha passes through).
    /// Uses the standard sRGB transfer function; the linear segment keeps
    /// very dark values exact.
    /// </summary>
    internal (float R, float G, float B, float A) ToLinear()
    {
        float lr = SrgbToLinear(R);
        float lg = SrgbToLinear(G);
        float lb = SrgbToLinear(B);
        return (lr, lg, lb, A);
    }

    private static float SrgbToLinear(float c)
    {
        if (c <= 0.04045f)
        {
            return c / 12.92f;
        }
        return MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
