using PAL.Platform;
using System.Numerics;
using System.Threading;
using FilamentSharp;
using PAL.Text;

namespace PAL;

/// <summary>
/// PAL's rendering facade over the real FilamentSharp backend: scenes,
/// cameras, models, fonts, text, frame rendering and headless capture.
/// </summary>
/// <remarks>
/// <para>
/// Created by <c>AppBuilder.WithGraphics</c> (windowed) or
/// <c>WithHeadlessGraphics</c> (offscreen); owned by the <see cref="App"/>.
/// The application decides when to render: <see cref="Render(Scene, Camera)"/>
/// presents one frame and returns whether it was drawn.
/// </para>
/// <para>
/// Frame flow: <see cref="Renderer.BeginFrame(SwapChain)"/> → render the
/// 3D view → render the text overlay (if text was drawn this frame) →
/// <see cref="Renderer.EndFrame"/>. When <c>BeginFrame</c> reports
/// backpressure, <see cref="Render(Scene, Camera)"/> returns false without
/// ending a frame that never began; PAL performs no retries, sleeps or
/// hidden pacing — the application decides what to do (typically: continue
/// the loop and try again next frame).
/// </para>
/// <para>
/// Threading: like the underlying Filament engine, graphics and everything
/// created from it are single-threaded; use them from the thread that
/// created the app.
/// </para>
/// </remarks>
public sealed class Graphics : IDisposable
{
    private readonly Engine _engine;
    private readonly Renderer _renderer;
    private readonly View _view;
    private readonly TextRenderer _textRenderer;
    private readonly IPlatformWindow? _window;

    private SwapChain? _swapChain;
    private GltfLoader? _gltfLoader;
    private int _gltfLoaderLeases;
    private int _width;
    private int _height;
    private Color _clearColor = Color.FromBytes(10, 12, 20);
    private bool _clearColorApplied;
    private bool _readbackPending;
    private bool _disposed;

    internal Graphics(Engine engine, Renderer renderer, View view,
        TextRenderer textRenderer, IPlatformWindow? window, int width, int height)
    {
        _engine = engine;
        _renderer = renderer;
        _view = view;
        _textRenderer = textRenderer;
        _window = window;
        _width = width;
        _height = height;
    }

    /// <summary>
    /// The underlying FilamentSharp engine (borrowed; PAL owns its
    /// lifetime). Escape hatch for anything PAL has not wrapped: create
    /// textures, buffers, lights or swap chains directly.
    /// </summary>
    public Engine Engine => _disposed
        ? throw NewDisposedException()
        : _engine;

    /// <summary>The backend actually in use (e.g. Vulkan, Noop).</summary>
    public Backend ActiveBackend => Engine.ActiveBackend;

    /// <summary>The main 3D view (borrowed; PAL owns its lifetime).</summary>
    public View MainView => _disposed
        ? throw NewDisposedException()
        : _view;

    /// <summary>The renderer PAL drives (borrowed; PAL owns its lifetime).</summary>
    public Renderer MainRenderer => _disposed
        ? throw NewDisposedException()
        : _renderer;

    /// <summary>
    /// Current frame size in pixels: the window client size when windowed,
    /// the configured offscreen size when headless.
    /// </summary>
    public (int Width, int Height) FrameSize => (_width, _height);

    /// <summary>Whether the app runs a real on-screen window.</summary>
    public bool IsWindowed => _window != null;

    /// <summary>Whether this graphics system renders offscreen (headless).</summary>
    public bool IsHeadless => _window == null;

    /// <summary>
    /// Sets the frame clear color (sRGB authored; converted to linear for
    /// the renderer). Applied from the next rendered frame.
    /// </summary>
    public void SetClearColor(Color color)
    {
        _clearColor = color;
        _clearColorApplied = false;
    }

    /// <summary>
    /// Creates a new, empty scene. Multiple scenes may exist; each render
    /// call decides which one is drawn.
    /// </summary>
    public Scene CreateScene()
    {
        ThrowIfDisposed();
        return new Scene(this);
    }

    /// <summary>
    /// Loads a font for <see cref="DrawText"/> and <see cref="MeasureText"/>.
    /// </summary>
    /// <param name="path">Path to a .ttf/.otf file (not a .ttc collection).</param>
    public Font LoadFont(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Font file not found: '{path}'. Provide a TrueType (.ttf) or OpenType " +
                "(.otf) font; .ttc collections are not supported by the rasterizer.", path);
        }
        return LoadFont(File.ReadAllBytes(path));
    }

    /// <summary>Loads a font from raw TTF/OTF bytes.</summary>
    public Font LoadFont(byte[] ttfData)
    {
        ThrowIfDisposed();
        return new Font(_engine, ttfData);
    }

    /// <summary>Loads a font from a stream (read fully; the stream may close after).</summary>
    public Font LoadFont(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return LoadFont(memory.ToArray());
    }

    /// <summary>
    /// Queues text to be drawn on top of the next rendered frame (screen
    /// space overlay).
    /// </summary>
    /// <param name="text">The text to draw; '\n' starts a new line, '\t' advances four spaces.</param>
    /// <param name="font">A font loaded from this graphics system.</param>
    /// <param name="position">
    /// Top-left corner of the first line's line box, in window pixels,
    /// origin at the top-left of the frame (y grows down). The baseline sits
    /// <c>ascent</c> pixels below <paramref name="position"/>.Y — the CSS/
    /// text-layout convention, so a fixed position is stable regardless of
    /// which glyphs are drawn. <see cref="MeasureText"/> returns exactly the
    /// box the text occupies from this corner.
    /// </param>
    /// <param name="pixelSize">Glyph height in pixels (rounded to whole pixels for caching).</param>
    /// <param name="color">Text color (sRGB authored; white over dark scenes works best).</param>
    /// <remarks>
    /// Text is batched per frame and composited over the 3D scene by the
    /// next <see cref="Render(Scene, Camera)"/> or
    /// <see cref="CaptureFrame(Scene, Camera, int)"/> call; nothing accumulates
    /// across frames. Drawing is CPU-layout + GPU-quads: repeated calls with
    /// the same string reuse the cached glyph atlas, not duplicated
    /// resources.
    /// </remarks>
    public void DrawText(string text, Font font, Vector2 position, float pixelSize,
        Color color)
    {
        ThrowIfDisposed();
        // The current viewport height converts the top-left-origin position
        // to the overlay camera's y-up space when quads are baked.
        _textRenderer.DrawText(text, font, position, pixelSize, color, _height);
    }

    /// <summary>
    /// Measures laid-out text: the size of the bounding box the text will
    /// occupy when drawn at the same size, with the same origin convention
    /// (height includes the full line height, also for the last line).
    /// Rasterizes glyphs at that size if they are not cached yet.
    /// </summary>
    public Vector2 MeasureText(string text, Font font, float pixelSize)
    {
        ThrowIfDisposed();
        return Text.TextRenderer.MeasureText(text, font, pixelSize);
    }

    /// <summary>
    /// Renders one frame of the scene with the camera and presents it.
    /// </summary>
    /// <returns>
    /// <c>true</c> when a frame was rendered and presented. <c>false</c>
    /// when the frame was skipped: backpressure (the previous present has
    /// not retired — retry later, PAL never busy-waits), or the window is
    /// not yet presentable (unexposed). Text drawn since the previous frame
    /// stays queued when the frame is skipped and is consumed by the next
    /// presented frame.
    /// </returns>
    public bool Render(Scene scene, Camera camera)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);

        RenderInternal(scene, camera, readback: false);
        return _lastFramePresented;
    }

    private bool _lastFramePresented;

    /// <summary>
    /// Renders frames until one completes a full pixel readback and returns
    /// its pixels — the headless workflow's verification and save entry
    /// point (also usable windowed).
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="camera">The camera to render with.</param>
    /// <param name="maxFrames">
    /// Maximum frames to render while waiting for the readback (the
    /// readback completes on the following frame).
    /// </param>
    /// <remarks>
    /// Text queued by <see cref="DrawText"/> is included in the captured
    /// frame. On the NOOP backend the returned buffer is valid-sized but
    /// zero-filled (nothing is drawn by NOOP).
    /// </remarks>
    public FramePixels CaptureFrame(Scene scene, Camera camera, int maxFrames = 8)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);

        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (frame > 0)
            {
                // Yield between retry frames: a capture loop is an explicit
                // render-until-done request, but spinning flat out starves
                // the backend's present/retire threads on multi-threaded
                // software drivers (observed: llvmpipe on 2-core hosts keeps
                // returning backpressure forever without a yield). One
                // millisecond lets presents retire; bounded by maxFrames.
                Thread.Sleep(1);
            }
            byte[]? pixels = RenderInternal(scene, camera, readback: true);
            if (pixels != null)
            {
                return new FramePixels(_width, _height, pixels);
            }
        }

        throw new PALException(
            $"Frame capture did not complete within {maxFrames} frames " +
            $"(backend: {ActiveBackend}). Backpressure can starve readback on tiny " +
            "frame budgets; retry the capture or render normally first.");
    }

    /// <summary>
    /// The frame core. Returns the readback pixels when
    /// <paramref name="readback"/> is requested and completed, else null.
    /// </summary>
    private byte[]? RenderInternal(Scene scene, Camera camera, bool readback)
    {
        var swapChain = EnsureSwapChain(); // resizes rebuild outside any frame pair

        if (_window != null && (!_window.IsVisible || !_window.IsOpen))
        {
            return null; // not presentable (unexposed or closed)
        }
        double aspect = _width / (double)Math.Max(_height, 1);
        camera.ApplyAspect(aspect);

        if (!_clearColorApplied)
        {
            var linear = _clearColor.ToLinear();
            _renderer.SetClearOptions(linear.R, linear.G, linear.B, linear.A);
            _clearColorApplied = true;
        }

        _view.SetScene(scene.Native);
        _view.SetCamera(camera.Native);
        _view.SetViewport(0, 0, (uint)_width, (uint)_height);

        // Text preparation (uploads, camera, viewport) before BeginFrame.
        _textRenderer.PrepareFrame(_width, _height);

        if (!_renderer.BeginFrame(swapChain))
        {
            _lastFramePresented = false;
            return null; // backpressure: no EndFrame for a frame that didn't begin
        }

        _renderer.Render(_view);
        _textRenderer.RenderOverlay(_renderer);

        // Only one readback may be pending per renderer; when a retry frame
        // runs while an earlier capture's readback is still in flight, keep
        // waiting for the original (it belongs to the frame that had the
        // content) instead of issuing a second request, which the backend
        // rejects.
        if (readback && !_readbackPending)
        {
            _renderer.ReadPixels(0, 0, (uint)_width, (uint)_height);
            _readbackPending = true;
        }

        _renderer.EndFrame();
        _textRenderer.EndFrame();
        _lastFramePresented = true;

        if (readback)
        {
            var pixels = _renderer.CompleteReadPixels();
            if (pixels != null)
            {
                _readbackPending = false;
            }
            return pixels;
        }
        return null;
    }

    /// <summary>
    /// Tracks the window size and rebuilds the native swap chain on resize
    /// (outside any frame). Headless swap chains are created once at the
    /// configured size.
    /// </summary>
    private void ApplyWindowSize()
    {
        if (_window == null)
        {
            return;
        }

        int w = _window.Width;
        int h = _window.Height;
        if (w == _width && h == _height && _swapChain != null)
        {
            return;
        }

        // First use or resize: (re)bind the swap chain to the window handle.
        // Filament's contract for changed extents is destroy + recreate on
        // the same handle — never inside a begin/end frame pair (we are
        // outside one by construction).
        _swapChain?.Dispose();
        _swapChain = _engine.CreateNativeSwapChain(_window.NativeHandle);
        _width = w;
        _height = h;
    }

    internal SwapChain EnsureSwapChain()
    {
        ApplyWindowSize();
        return _swapChain ??= _window != null
            ? _engine.CreateNativeSwapChain(_window.NativeHandle)
            : _engine.CreateHeadlessSwapChain((uint)_width, (uint)_height);
    }

    internal GltfLoader LeaseGltfLoader()
    {
        ThrowIfDisposed();
        _gltfLoaderLeases++;
        return _gltfLoader ??= _engine.CreateGltfLoader();
    }

    internal void ReleaseGltfLoader()
    {
        _gltfLoaderLeases = Math.Max(0, _gltfLoaderLeases - 1);
    }

    /// <summary>
    /// Disposes PAL's graphics resources deterministically: text overlay,
    /// swap chain, views, renderer — then the engine, whose registry
    /// destroys any leaked children in reverse creation order. Dispose the
    /// graphics before the window (App does this in the right order).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // User-owned resources (scenes, models, fonts, lights, cameras)
        // are disposed by their owners; the engine's LIFO teardown is the
        // documented safety net for leaks.
        _textRenderer.Dispose();
        _gltfLoader?.Dispose();
        _swapChain?.Dispose();
        _view.Dispose();
        _renderer.Dispose();
        _engine.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw NewDisposedException();
        }
    }

    private ObjectDisposedException NewDisposedException() =>
        new(nameof(Graphics),
            "The PAL graphics system has been disposed (dispose the App).");
}
