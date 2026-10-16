using System.Buffers.Binary;
using System.Numerics;
using FilamentSharp;

namespace PAL.Text;

/// <summary>
/// The screen-space text overlay: rasterizes glyphs on demand, batches
/// glyph quads per font for the current frame, and renders them as a
/// translucent layer on top of the 3D view.
/// </summary>
/// <remarks>
/// <para>
/// Drawing model: <see cref="Graphics.DrawText"/> appends quads to the
/// drawing font's per-frame batch; the next <see cref="Graphics.Render"/> or
/// <see cref="Graphics.CaptureFrame"/> uploads the batches, renders the
/// overlay view, and clears them. Text drawn during a frame appears in that
/// frame; nothing accumulates across frames. Multiple fonts may be drawn in
/// one frame; each font's quads use its own atlas through its own material
/// instance, all inside one overlay scene.
/// </para>
/// <para>
/// Coordinate system: <see cref="Graphics.DrawText"/> positions are window
/// pixels with the origin at the <b>top-left</b> corner, x growing right and
/// y growing down (the 2D UI convention). Quads are re-laid-out every frame
/// against the current viewport, so text stays put when the window resizes.
/// </para>
/// </remarks>
internal sealed class TextRenderer : IDisposable
{
    /// <summary>Initial per-font batch capacity in quads; doubles on demand.</summary>
    internal const int InitialCapacity = 256;

    /// <summary>Maximum quads per font per frame; beyond this PAL throws.</summary>
    internal const int MaxCapacity = 16384;

    // Interleaved vertex: float3 position, float2 uv, float4 color = 36 bytes.
    internal const int VertexStride = 36;

    private readonly Engine _engine;

    private View? _view;
    private FilamentSharp.Scene? _scene;
    private FilamentSharp.Camera? _camera;
    private Entity _cameraEntity;
    private Material? _material;
    private readonly Dictionary<Font, FontBatch> _batches = new();
    private bool _disposed;

    internal TextRenderer(Engine engine)
    {
        _engine = engine;
    }

    /// <summary>Whether any quads are queued for the current frame.</summary>
    internal bool HasQuads
    {
        get
        {
            foreach (var batch in _batches.Values)
            {
                if (batch.QuadCount > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>The shared text material, loaded from the embedded package.</summary>
    private Material Material =>
        _material ??= _engine.CreateMaterial(LoadEmbeddedMaterial());

    private static byte[] LoadEmbeddedMaterial()
    {
        var assembly = typeof(TextRenderer).Assembly;
        const string resourceName = "PAL.Assets.text_overlay.filamat";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new PALException(
                $"The embedded text material resource '{resourceName}' is missing from " +
                "the PAL assembly. The build may be corrupted; rebuild PAL.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // ---- Draw / measure (called by Graphics) -------------------------------

    /// <summary>Queues text for the current frame; see Graphics.DrawText.</summary>
    internal void DrawText(string text, Font font, Vector2 position, float pixelSize,
        Color color, int viewportHeight)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        font.ThrowIfDisposed();
        if (text.Length == 0)
        {
            return;
        }

        int size = Math.Max(1, (int)MathF.Round(pixelSize));
        var cache = font.GetCache(size);
        cache.LoadMetrics();

        var batch = GetBatch(font);

        // Convert the sRGB color to linear (the overlay pass works in linear
        // space and is encoded on output).
        var linear = color.ToLinear();
        var vertexColor = new Vector4(linear.R, linear.G, linear.B, linear.A);

        float penX = position.X;
        float baselineY = position.Y + cache.Ascent; // top-left origin -> baseline
        float startX = penX;
        int previousCodepoint = -1;

        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];

            // Surrogate pairs -> full code point.
            if (char.IsHighSurrogate((char)codepoint) &&
                i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            switch (codepoint)
            {
                case '\n':
                    penX = startX;
                    baselineY += cache.LineHeight;
                    previousCodepoint = -1;
                    continue;
                case '\r':
                    continue; // CRLF-safe: '\n' handles the break
                case '\t':
                    penX += cache.SpaceAdvance * 4.0f;
                    previousCodepoint = -1;
                    continue;
            }

            if (previousCodepoint >= 0)
            {
                penX += cache.GetKerning(previousCodepoint, codepoint);
            }
            previousCodepoint = codepoint;

            var glyph = cache.GetGlyph(codepoint);
            if (glyph.Rect != default)
            {
                batch.EmitQuad(glyph, cache, penX, baselineY, vertexColor, viewportHeight);
            }
            penX += glyph.Advance;
        }
    }

    /// <summary>Measures laid-out text without drawing (see Graphics.MeasureText).</summary>
    internal static Vector2 MeasureText(string text, Font font, float pixelSize)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        font.ThrowIfDisposed();

        int size = Math.Max(1, (int)MathF.Round(pixelSize));
        var cache = font.GetCache(size);
        cache.LoadMetrics();

        float width = 0.0f;
        float maxWidth = 0.0f;
        int lines = 1;
        int previousCodepoint = -1;

        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];
            if (char.IsHighSurrogate((char)codepoint) &&
                i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (codepoint == '\n')
            {
                maxWidth = Math.Max(maxWidth, width);
                width = 0.0f;
                lines++;
                previousCodepoint = -1;
                continue;
            }
            if (codepoint == '\r')
            {
                continue;
            }
            if (codepoint == '\t')
            {
                width += cache.SpaceAdvance * 4.0f;
                previousCodepoint = -1;
                continue;
            }

            if (previousCodepoint >= 0)
            {
                width += cache.GetKerning(previousCodepoint, codepoint);
            }
            previousCodepoint = codepoint;

            var glyph = cache.GetGlyph(codepoint);
            width += glyph.Advance;
        }
        maxWidth = Math.Max(maxWidth, width);

        return new Vector2(maxWidth, lines * cache.LineHeight);
    }

    // ---- Frame flow ---------------------------------------------------------

    /// <summary>
    /// Prepares overlay resources for a frame of the given viewport size,
    /// before BeginFrame: realizes the overlay view/camera, realizes atlases
    /// and uploads batches. Safe to call with no queued text (no-op).
    /// </summary>
    internal void PrepareFrame(int viewportWidth, int viewportHeight)
    {
        ThrowIfDisposed();
        if (!HasQuads)
        {
            return;
        }

        EnsureOverlayObjects(viewportWidth, viewportHeight);

        foreach (var (font, batch) in _batches)
        {
            if (batch.QuadCount == 0)
            {
                continue;
            }

            // Realize the atlas and (re)bind it to this font's instance; the
            // instance is rebound whenever the atlas texture was recreated
            // (growth) or this is the first frame.
            var instance = batch.EnsureInstance(Material);
            var texture = font.Atlas.EnsureTexture();
            if (batch.BoundTexture != texture)
            {
                instance.SetTexture("atlas", texture,
                    TextureMinFilter.Linear, TextureMagFilter.Linear,
                    TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
                batch.BoundTexture = texture;
            }

            batch.EnsureBatchObjects(_scene!);
            batch.Upload();
        }

        // Camera maps window pixels to the viewport (y up, origin
        // bottom-left). Recomputed every frame; cheap and resize-proof.
        _camera!.SetOrthographicProjection(0, viewportWidth, 0, viewportHeight, -1, 1);
        _camera.LookAt(
            new Vector3(0, 0, 0),
            new Vector3(0, 0, -1),
            new Vector3(0, 1, 0));
        _view!.SetViewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
    }

    /// <summary>Renders the overlay (call between BeginFrame/EndFrame).</summary>
    internal void RenderOverlay(Renderer renderer)
    {
        if (_view == null || !HasQuads)
        {
            return;
        }
        renderer.Render(_view);
    }

    /// <summary>Clears every per-frame batch. Called after the frame ends.</summary>
    internal void EndFrame()
    {
        foreach (var batch in _batches.Values)
        {
            batch.EndFrame();
        }
    }

    private FontBatch GetBatch(Font font)
    {
        if (_batches.TryGetValue(font, out var batch))
        {
            return batch;
        }
        batch = new FontBatch(font, this);
        _batches[font] = batch;
        return batch;
    }

    private void EnsureOverlayObjects(int viewportWidth, int viewportHeight)
    {
        if (_view != null)
        {
            return;
        }

        _scene = _engine.CreateScene();
        _view = _engine.CreateView();
        _view.SetBlendMode(ViewBlendMode.Translucent);

        _cameraEntity = EntityManager.Create();
        _camera = _engine.CreateCamera(_cameraEntity);
        _camera.SetOrthographicProjection(0, viewportWidth, 0, viewportHeight, -1, 1);
        _camera.LookAt(new Vector3(0, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0));

        _view.SetScene(_scene);
        _view.SetCamera(_camera);
        _view.SetViewport(0, 0, (uint)viewportWidth, (uint)viewportHeight);
    }

    internal Engine Engine => _engine;
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes overlay resources deterministically. Fonts are owned by the
    /// user; their atlas textures are released by the user's Font.Dispose
    /// (or by the engine's teardown safety net if leaked).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var batch in _batches.Values)
        {
            batch.Dispose();
        }
        _batches.Clear();

        _view?.Dispose();
        _camera?.Dispose();
        _scene?.Dispose();
        if (_cameraEntity.IsValid)
        {
            EntityManager.Destroy(_cameraEntity);
        }
        _material?.Dispose();

        _view = null;
        _camera = null;
        _scene = null;
        _material = null;
    }

    /// <summary>
    /// Per-font batch state: vertex data, geometry buffers, renderable and
    /// the material instance bound to the font's atlas.
    /// </summary>
    private sealed class FontBatch : IDisposable
    {
        private readonly Font _font;
        private readonly TextRenderer _owner;
        private readonly byte[] _vertexData = new byte[MaxCapacity * 4 * VertexStride];

        private VertexBuffer? _vertexBuffer;
        private IndexBuffer? _indexBuffer;
        private Renderable? _renderable;
        private Entity _renderableEntity;
        private MaterialInstance? _instance;
        internal Texture? BoundTexture;
        private int _capacity;

        internal FontBatch(Font font, TextRenderer owner)
        {
            _font = font;
            _owner = owner;
        }

        internal int QuadCount { get; private set; }

        /// <summary>The font's material instance, created on first use.</summary>
        internal MaterialInstance EnsureInstance(Material material)
        {
            if (_instance is { IsDisposed: false } instance)
            {
                return instance;
            }
            _instance?.Dispose();
            _instance = material.CreateInstance();
            return _instance;
        }

        internal void EmitQuad(Glyph glyph, GlyphCache cache, float penX,
            float baselineY, in Vector4 color, int viewportHeight)
        {
            if (QuadCount >= MaxCapacity)
            {
                throw new PALException(
                    $"Text batch overflow: more than {MaxCapacity} glyph quads were " +
                    $"queued for one font in a single frame ({_font} at " +
                    $"{cache.PixelSize}px). Draw less text or split draws across frames.");
            }

            int atlasSize = _font.Atlas.Size;
            var rect = glyph.Rect;

            // Top-left origin (y down) -> ortho pixel space (y up): the
            // baseline arrives in window coordinates measured from the top,
            // while the overlay camera's ortho projection is y-up from the
            // bottom edge — flip the baseline around the viewport's bottom
            // edge and derive the quad from there. The glyph's bitmap top
            // stays above the baseline (yoff negative in y-down = up).
            float baselineUp = viewportHeight - baselineY;
            float top = baselineUp - glyph.YOffset; // yoff is negative above baseline
            float bottom = top - rect.Height;
            float left = penX + glyph.XOffset;
            float right = left + rect.Width;

            float u0 = (float)rect.X / atlasSize;
            float u1 = (float)(rect.X + rect.Width) / atlasSize;
            // Filament's texture upload maps the buffer's row 0 to v = 1
            // (the top in UV space): flip V so the glyph's top bitmap row
            // is sampled by the quad's top edge (verified empirically on
            // Vulkan — see docs/text-rendering.md, "Atlas orientation").
            float v0 = 1.0f - (float)rect.Y / atlasSize;                 // bitmap top row
            float v1 = 1.0f - (float)(rect.Y + rect.Height) / atlasSize; // bitmap bottom row

            int quadIndex = QuadCount;

            // Vertex order: bottom-left, bottom-right, top-left, top-right.
            // uv v: bitmap row 0 (top) maps to the top vertices.
            WriteVertex(quadIndex, 0, left, bottom, u0, v1, color);
            WriteVertex(quadIndex, 1, right, bottom, u1, v1, color);
            WriteVertex(quadIndex, 2, left, top, u0, v0, color);
            WriteVertex(quadIndex, 3, right, top, u1, v0, color);

            QuadCount++;
        }

        private void WriteVertex(int quadIndex, int corner, float x, float y,
            float u, float v, in Vector4 color)
        {
            int offset = (quadIndex * 4 + corner) * VertexStride;
            var span = _vertexData.AsSpan(offset, VertexStride);
            BinaryPrimitives.WriteSingleLittleEndian(span, x);
            BinaryPrimitives.WriteSingleLittleEndian(span[4..], y);
            BinaryPrimitives.WriteSingleLittleEndian(span[8..], 0.0f);
            BinaryPrimitives.WriteSingleLittleEndian(span[12..], u);
            BinaryPrimitives.WriteSingleLittleEndian(span[16..], v);
            BinaryPrimitives.WriteSingleLittleEndian(span[20..], color.X);
            BinaryPrimitives.WriteSingleLittleEndian(span[24..], color.Y);
            BinaryPrimitives.WriteSingleLittleEndian(span[28..], color.Z);
            BinaryPrimitives.WriteSingleLittleEndian(span[32..], color.W);
        }

        /// <summary>Creates or grows the geometry buffers and renderable.</summary>
        internal void EnsureBatchObjects(FilamentSharp.Scene scene)
        {
            if (_capacity >= QuadCount && _vertexBuffer != null)
            {
                return;
            }

            int capacity = Math.Max(InitialCapacity, 1);
            while (capacity < QuadCount && capacity < MaxCapacity)
            {
                capacity = Math.Min(capacity * 2, MaxCapacity);
            }

            // Rebuild at the new capacity. Never called inside a frame.
            _renderable?.Dispose();
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            if (_renderableEntity.IsValid)
            {
                scene.RemoveEntity(_renderableEntity);
                EntityManager.Destroy(_renderableEntity);
            }

            var engine = _owner.Engine;
            var attributes = new VertexAttributeDescription[]
            {
                new(VertexAttribute.Position, ElementType.Float3, 0, 0, VertexStride),
                new(VertexAttribute.Uv0, ElementType.Float2, 0, 12, VertexStride),
                new(VertexAttribute.Color, ElementType.Float4, 0, 20, VertexStride),
            };
            _vertexBuffer = engine.CreateVertexBuffer((uint)(capacity * 4), 1, attributes);

            // Static index buffer: two triangles per quad.
            var indices = new byte[capacity * 6 * sizeof(ushort)];
            for (int q = 0; q < capacity; q++)
            {
                int baseVertex = q * 4;
                int o = q * 6 * sizeof(ushort);
                foreach (var (index, at) in new[] { (0, 0), (1, 2), (2, 4), (2, 6), (1, 8), (3, 10) })
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        indices.AsSpan(o + at), (ushort)(baseVertex + index));
                }
            }
            _indexBuffer = engine.CreateIndexBuffer((uint)(capacity * 6), IndexType.UnsignedShort);
            _indexBuffer.SetBuffer(indices);

            _renderableEntity = EntityManager.Create();
            var instance = EnsureInstance(_owner.Material);
            _renderable = Renderable.Create(engine, _renderableEntity, new RenderableOptions
            {
                Vertices = _vertexBuffer,
                Indices = _indexBuffer,
                PrimitiveCount = (uint)(capacity * 6),
                // Screen-space quads: oversized bounds + no culling so the
                // ortho camera never frustum-culls text.
                BoundsMin = new Vector3(-1e6f, -1e6f, -10.0f),
                BoundsMax = new Vector3(1e6f, 1e6f, 10.0f),
                Material = instance,
                Culling = false,
            });
            scene.AddEntity(_renderableEntity);

            _capacity = capacity;
        }

        /// <summary>
        /// Uploads the used quads; unused capacity is degenerate-filled
        /// (all-zero positions produce zero-area triangles).
        /// </summary>
        internal void Upload()
        {
            int usedBytes = QuadCount * 4 * VertexStride;
            int totalBytes = _capacity * 4 * VertexStride;
            Array.Clear(_vertexData, usedBytes, totalBytes - usedBytes);
            _vertexBuffer!.SetBufferAt(0, _vertexData.AsSpan(0, totalBytes));
        }

        internal void EndFrame()
        {
            QuadCount = 0;
        }

        public void Dispose()
        {
            _renderable?.Dispose();
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            if (_renderableEntity.IsValid)
            {
                EntityManager.Destroy(_renderableEntity);
            }
            _instance?.Dispose();
            _renderable = null;
            _vertexBuffer = null;
            _indexBuffer = null;
            _instance = null;
            BoundTexture = null;
        }
    }
}
