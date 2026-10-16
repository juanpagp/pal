using StbTrueTypeSharp;

namespace PAL.Text;

/// <summary>A rectangle in the glyph atlas, in pixels.</summary>
/// <param name="X">Left edge of the rectangle.</param>
/// <param name="Y">Top edge of the rectangle.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
internal readonly record struct AtlasRect(int X, int Y, int Width, int Height);

/// <summary>Metrics and atlas placement of one rasterized glyph.</summary>
/// <param name="Codepoint">The Unicode code point.</param>
/// <param name="Advance">Horizontal advance in pixels (includes scale).</param>
/// <param name="XOffset">Bearing X: pen position to glyph bitmap left, pixels.</param>
/// <param name="YOffset">
/// Bearing Y: baseline to glyph bitmap top, pixels; negative above the
/// baseline (stb_truetype convention).
/// </param>
/// <param name="Rect">Placement in the atlas; empty when the glyph has no ink.</param>
internal readonly record struct Glyph(
    int Codepoint,
    float Advance,
    int XOffset,
    int YOffset,
    AtlasRect Rect);

/// <summary>
/// Rasterizes and caches glyphs for one font at one pixel size. Owned by
/// <see cref="Font"/>; pure CPU work plus atlas placement, unit tested
/// without a GPU.
/// </summary>
internal sealed unsafe class GlyphCache
{
    private readonly Font _font;
    private readonly int _pixelSize;
    private readonly float _scale;
    private readonly Dictionary<int, Glyph> _glyphs = new();

    internal GlyphCache(Font font, int pixelSize)
    {
        _font = font;
        _pixelSize = pixelSize;
        _scale = StbTrueType.stbtt_ScaleForPixelHeight(_font.InfoRef, pixelSize);
    }

    internal Font Font => _font;

    internal int PixelSize => _pixelSize;

    internal float Scale => _scale;

    /// <summary>Scaled ascent in pixels (baseline to line top, positive).</summary>
    public float Ascent { get; private set; }

    /// <summary>Scaled descent in pixels (baseline to line bottom, negative).</summary>
    public float Descent { get; private set; }

    /// <summary>Scaled line gap in pixels (extra spacing between lines).</summary>
    public float LineGap { get; private set; }

    /// <summary>Ascent - Descent + LineGap: the full line height in pixels.</summary>
    public float LineHeight { get; private set; }

    /// <summary>
    /// Loads font metrics. Must be called once before
    /// <see cref="GetGlyph"/>; separated so metrics-only use
    /// (text measurement) never rasterizes.
    /// </summary>
    public void LoadMetrics()
    {
        int ascent, descent, lineGap;
        int* a = &ascent, d = &descent, g = &lineGap;
        var info = _font.InfoRef;
        StbTrueType.stbtt_GetFontVMetrics(info, a, d, g);
        Ascent = ascent * _scale;
        Descent = descent * _scale;
        LineGap = lineGap * _scale;
        LineHeight = Ascent - Descent + LineGap;
    }

    /// <summary>Returns the glyph for a code point, rasterizing on first use.</summary>
    public Glyph GetGlyph(int codepoint)
    {
        if (_glyphs.TryGetValue(codepoint, out var cached))
        {
            return cached;
        }

        var info = _font.InfoRef;
        int advance, lsb;
        int* adv = &advance, l = &lsb;
        StbTrueType.stbtt_GetCodepointHMetrics(info, codepoint, adv, l);

        int width, height, xoff, yoff;
        int* w = &width, h = &height, xo = &xoff, yo = &yoff;
        byte* bitmap = StbTrueType.stbtt_GetCodepointBitmap(
            info, 0.0f, _scale, codepoint, w, h, xo, yo);

        AtlasRect rect;
        try
        {
            if (bitmap != null && width > 0 && height > 0)
            {
                var rgba = new byte[width * height * 4];
                for (int i = 0; i < width * height; i++)
                {
                    // White RGB with the coverage in alpha: the material
                    // multiplies by the per-vertex color, and alpha blending
                    // uses coverage directly.
                    rgba[i * 4] = 255;
                    rgba[i * 4 + 1] = 255;
                    rgba[i * 4 + 2] = 255;
                    rgba[i * 4 + 3] = bitmap[i];
                }
                rect = _font.Atlas.Place(width, height, rgba);
            }
            else
            {
                rect = default; // space and other ink-less glyphs
            }
        }
        finally
        {
            if (bitmap != null)
            {
                StbTrueType.stbtt_FreeBitmap(bitmap, null);
            }
        }

        var glyph = new Glyph(
            codepoint,
            advance * _scale,
            xoff,
            yoff,
            rect);
        _glyphs[codepoint] = glyph;
        return glyph;
    }

    /// <summary>Scaled kerning between two code points, in pixels.</summary>
    public float GetKerning(int left, int right)
    {
        var info = _font.InfoRef;
        int kern = StbTrueType.stbtt_GetCodepointKernAdvance(info, left, right);
        return kern * _scale;
    }

    /// <summary>Advance of a space at this size (used for tabs).</summary>
    public float SpaceAdvance => GetGlyph(' ').Advance;
}
