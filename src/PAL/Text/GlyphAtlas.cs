using FilamentSharp;

namespace PAL.Text;

/// <summary>
/// A growing RGBA8 glyph atlas shared by all sizes of one
/// <see cref="Font"/>, packed with a row-shelf algorithm and backed by a
/// Filament texture.
/// </summary>
/// <remarks>
/// <para>
/// The binding's texture upload replaces a whole mip level, so the atlas
/// keeps a CPU-side pixel buffer and uploads it at most once per frame, and
/// only while glyphs were added (dirty flag). Steady-state frames upload
/// nothing.
/// </para>
/// <para>
/// The atlas starts at <see cref="InitialSize"/> and doubles when full,
/// recreating the texture and re-uploading every cached glyph. Beyond
/// <see cref="MaxSize"/> it resets and repacks. All quads are rebuilt from
/// <see cref="Size"/> every frame, so growth and reset need no special
/// handling in the renderer.
/// </para>
/// </remarks>
internal sealed class GlyphAtlas : IDisposable
{
    internal const int InitialSize = 512;
    internal const int MaxSize = 4096;
    private const int Padding = 1; // avoids bleeding between glyphs

    private List<PlacedGlyph> _placements = new();
    private byte[] _pixels = new byte[InitialSize * InitialSize * 4];
    private Texture? _texture;
    private int _textureSize;
    private int _size = InitialSize;
    private int _cursorX;
    private int _cursorY;
    private int _rowHeight;
    private bool _dirty;

    private readonly Engine _engine;

    internal GlyphAtlas(Engine engine)
    {
        _engine = engine;
    }

    private readonly record struct PlacedGlyph(int Width, int Height, AtlasRect Rect, byte[] Rgba);

    /// <summary>Current atlas side in pixels (square, power of two).</summary>
    internal int Size => _size;

    /// <summary>Whether the texture needs (re)creation or re-upload.</summary>
    internal bool Dirty => _dirty;

    /// <summary>
    /// The Filament texture; null until <see cref="EnsureTexture"/> runs.
    /// Recreated on growth — callers that bind the texture to a material
    /// must rebind when the instance changes.
    /// </summary>
    internal Texture? Texture => _texture is { IsDisposed: false } ? _texture : null;

    /// <summary>
    /// Packs a glyph bitmap into the CPU-side buffer and marks the atlas
    /// dirty. Returns the placement rectangle in atlas pixels.
    /// </summary>
    internal AtlasRect Place(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length < width * height * 4)
        {
            throw new ArgumentException("Bitmap buffer too small for its dimensions.");
        }

        if (!TryPack(width, height, out var rect))
        {
            if (_size < MaxSize)
            {
                Grow();
            }
            else
            {
                Reset();
            }

            if (!TryPack(width, height, out rect))
            {
                // A glyph larger than the whole atlas: clamp at the maximum
                // and let it fail loudly if it still does not fit.
                Reset();
                if (!TryPack(Math.Min(width, _size - 2), Math.Min(height, _size - 2), out rect))
                {
                    throw new PALException(
                        $"A {width}x{height}px glyph does not fit the {MaxSize}px glyph " +
                        "atlas; the font size is too large for this release's limits.");
                }
            }
        }

        Blit(rect, rgba);
        _placements.Add(new PlacedGlyph(width, height, rect, rgba));
        _dirty = true;
        return rect;
    }

    /// <summary>
    /// Tries to place a w*h box at the current shelf cursor. Exposed
    /// internal for unit tests.
    /// </summary>
    internal bool TryPack(int width, int height, out AtlasRect rect)
    {
        if (_cursorX + width + Padding > _size)
        {
            int nextY = _cursorY + _rowHeight + Padding;
            if (nextY + height + Padding > _size)
            {
                rect = default;
                return false;
            }
            _cursorY = nextY;
            _cursorX = 0;
            _rowHeight = 0;
        }

        rect = new AtlasRect(_cursorX, _cursorY, width, height);
        _cursorX += width + Padding;
        _rowHeight = Math.Max(_rowHeight, height);
        return true;
    }

    /// <summary>Creates the texture if needed and uploads pending content.</summary>
    internal Texture EnsureTexture()
    {
        if (_texture is { IsDisposed: false } texture && !_dirty)
        {
            return texture;
        }

        if (_texture is null || _texture.IsDisposed || _textureSize != _size)
        {
            _texture?.Dispose();
            _texture = _engine.CreateTexture2D(
                (uint)_size, (uint)_size, 1, TextureFormat.Rgba8);
            _textureSize = _size;
        }

        _texture.SetImageRgba8(0, (uint)_size, (uint)_size, _pixels);
        _dirty = false;
        return _texture;
    }

    private void Grow()
    {
        _size = Math.Min(_size * 2, MaxSize);
        RepackAll();
    }

    /// <summary>Drops all placements and re-packs every stored bitmap.</summary>
    private void Reset()
    {
        _size = InitialSize;
        RepackAll();
    }

    private void RepackAll()
    {
        _cursorX = 0;
        _cursorY = 0;
        _rowHeight = 0;

        var old = _placements;
        _placements = new List<PlacedGlyph>(old.Count);
        _pixels = new byte[_size * _size * 4];

        foreach (var glyph in old)
        {
            if (!TryPack(glyph.Width, glyph.Height, out var rect))
            {
                // Should not happen (we just doubled the space), but fail
                // loudly rather than corrupt placement.
                throw new PALException(
                    "Internal glyph atlas error: repacking lost a glyph. " +
                    "This indicates a PAL bug; please report it.");
            }
            Blit(rect, glyph.Rgba);
            _placements.Add(glyph with { Rect = rect });
        }
        _dirty = true;
    }

    /// <summary>Copies a glyph bitmap into the CPU-side pixel buffer.</summary>
    private void Blit(AtlasRect rect, byte[] rgba)
    {
        int rowStride = _size * 4;
        for (int y = 0; y < rect.Height; y++)
        {
            int src = y * rect.Width * 4;
            int dst = (rect.Y + y) * rowStride + rect.X * 4;
            Array.Copy(rgba, src, _pixels, dst, rect.Width * 4);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _placements.Clear();
        _pixels = Array.Empty<byte>();
    }
}
