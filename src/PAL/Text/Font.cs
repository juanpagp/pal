using FilamentSharp;
using System.Runtime.InteropServices;
using StbTrueTypeSharp;

namespace PAL.Text;

/// <summary>
/// A TrueType/OpenType font loaded from a file, byte array or stream, ready
/// for <see cref="Graphics.DrawText"/> and
/// <see cref="Graphics.MeasureText"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each <see cref="Font"/> owns one glyph atlas (a Filament texture) shared
/// by all font sizes used with it; glyphs are rasterized on first use per
/// pixel size and cached. Keep fonts alive for the application's lifetime
/// and dispose them before the graphics system.
/// </para>
/// <para>
/// Rasterization uses stb_truetype (StbTrueTypeSharp): it covers the
/// font's Unicode repertoire (including code points outside the BMP) but
/// performs no complex-script shaping — there are no ligatures, joining or
/// bidi reordering, and no right-to-left support. Latin and other
/// simple-script text renders correctly with kerning applied.
/// </para>
/// </remarks>
public sealed class Font : IDisposable
{
    private readonly Engine? _engine;
    private readonly byte[] _data;
    private readonly GCHandle _pin; // stbtt points into _data
    private StbTrueType.stbtt_fontinfo _info;
    private GlyphAtlas? _atlas;
    private readonly Dictionary<int, GlyphCache> _caches = new();
    private bool _disposed;

    /// <summary>
    /// Creates a font. The engine may be null for CPU-only use (metrics,
    /// measurement, glyph rasterization tests); drawing text with such a
    /// font fails when the atlas first needs a texture.
    /// </summary>
    internal Font(Engine? engine, byte[] ttfData)
    {
        ArgumentNullException.ThrowIfNull(ttfData);
        if (ttfData.Length < 12)
        {
            throw new PALException(
                "The font data is too small to be a TrueType/OpenType font (expected a " +
                ".ttf/.otf file; note that .ttc collections are not supported by this " +
                "rasterizer).");
        }

        _engine = engine;
        _data = ttfData;
        _pin = GCHandle.Alloc(_data, GCHandleType.Pinned);

        unsafe
        {
            var info = new StbTrueType.stbtt_fontinfo();
            fixed (byte* p = _data)
            {
                if (StbTrueType.stbtt_InitFont(info, p, 0) == 0)
                {
                    _pin.Free();
                    throw new PALException(
                        "Could not parse the font data (stbtt_InitFont failed). Provide a " +
                        "valid TrueType (.ttf) or OpenType (.otf) font.");
                }
            }
            _info = info;
        }
    }

    /// <summary>Whether this font has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// A copy of the stbtt font info struct (a value type holding a pointer
    /// into the pinned font data; stb_truetype's API takes it by value).
    /// Internal to the text pipeline.
    /// </summary>
    internal StbTrueType.stbtt_fontinfo InfoRef => _info;

    internal GlyphAtlas Atlas => _atlas ??= new GlyphAtlas(_engine);

    internal GlyphCache GetCache(int pixelSize)
    {
        if (!_caches.TryGetValue(pixelSize, out var cache))
        {
            cache = new GlyphCache(this, pixelSize);
            _caches[pixelSize] = cache;
        }
        return cache;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _caches.Clear();
        _atlas?.Dispose();
        _atlas = null;
        _pin.Free();
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Font),
                "The font has been disposed; load a new one with Graphics.LoadFont.");
        }
    }
}
