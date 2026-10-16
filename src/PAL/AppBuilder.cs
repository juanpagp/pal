using FilamentSharp;
using PAL.Internal;
using PAL.Platform;
using PAL.Text;

namespace PAL;

/// <summary>
/// Configures and builds an <see cref="App"/>: window, graphics backend,
/// headless offscreen size and timing limits. Invalid combinations are
/// rejected early with clear exceptions.
/// </summary>
/// <remarks>
/// <para>
/// Three supported application shapes:
/// </para>
/// <list type="bullet">
/// <item><b>Windowed</b>: <c>WithWindow(...)</c> plus optional
/// <c>WithGraphics(backend)</c> — a real OS window (X11 on Linux) with
/// on-screen rendering.</item>
/// <item><b>Headless rendering</b>: <c>WithHeadlessGraphics(width, height,
/// backend)</c> — Filament engine + offscreen swap chain, no OS window;
/// render, read pixels back, save images.</item>
/// <item><b>Core</b>: no window, no graphics — PAL's application utilities
/// (time, input types) without initializing Filament at all.</item>
/// </list>
/// <para>
/// A window without graphics is allowed (event loop applications); graphics
/// without a window is not (use headless graphics instead).
/// </para>
/// </remarks>
public sealed class AppBuilder
{
    private string? _title;
    private int _width = 1280;
    private int _height = 720;
    private bool _window;

    private Backend? _backend;
    private bool _headless;
    private int _headlessWidth;
    private int _headlessHeight;

    private float _maxDeltaTime = 0.25f;

    /// <summary>
    /// Configures a real on-screen window (X11 on Linux). Requires a
    /// reachable display (desktop session or Xvfb).
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="width">Initial client width in pixels (&gt; 0).</param>
    /// <param name="height">Initial client height in pixels (&gt; 0).</param>
    public AppBuilder WithWindow(string title, int width = 1280, int height = 720)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (_headless)
        {
            throw new InvalidOperationException(
                "WithWindow cannot be combined with WithHeadlessGraphics: headless " +
                "applications deliberately have no OS window. Drop one of the two " +
                "(windowed rendering uses WithGraphics).");
        }

        _title = title;
        _width = width;
        _height = height;
        _window = true;
        return this;
    }

    /// <summary>
    /// Initializes real windowed rendering through the given Filament backend.
    /// Requires <see cref="WithWindow(string, int, int)"/> (call it first or
    /// after; ordering does not matter).
    /// </summary>
    /// <param name="backend">
    /// Vulkan is the primary desktop backend (PAL probes for a usable
    /// Vulkan installation first and fails with an actionable error instead
    /// of letting Filament abort). Noop creates the full object model
    /// without a GPU (nothing is drawn). OpenGL requires a running X server.
    /// </param>
    public AppBuilder WithGraphics(Backend backend = Backend.Vulkan)
    {
        if (_headless)
        {
            throw new InvalidOperationException(
                "WithGraphics cannot follow WithHeadlessGraphics; the headless call already " +
                "selected an offscreen graphics configuration. Keep exactly one of them.");
        }

        _backend = backend;
        return this;
    }

    /// <summary>
    /// Initializes headless (offscreen) rendering: a Filament engine and a
    /// headless swap chain of the given size, no OS window. Render with
    /// <see cref="Graphics.Render(Scene, Camera)"/>, read pixels with
    /// <see cref="Graphics.CaptureFrame(Scene, Camera, int)"/>.
    /// </summary>
    /// <param name="width">Offscreen width in pixels (&gt; 0).</param>
    /// <param name="height">Offscreen height in pixels (&gt; 0).</param>
    /// <param name="backend">See <see cref="WithGraphics(Backend)"/>.</param>
    public AppBuilder WithHeadlessGraphics(int width, int height,
        Backend backend = Backend.Vulkan)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (_window)
        {
            throw new InvalidOperationException(
                "WithHeadlessGraphics cannot be combined with WithWindow: headless " +
                "rendering deliberately has no OS window. Use WithGraphics for windowed " +
                "rendering.");
        }

        _headless = true;
        _backend = backend;
        _headlessWidth = width;
        _headlessHeight = height;
        return this;
    }

    /// <summary>
    /// Caps <see cref="Time.DeltaTime"/> (seconds). Long pauses (window
    /// drags, suspend, breakpoints) produce this maximum instead of an
    /// unbounded step; <see cref="Time.TotalTime"/> is unaffected. Default
    /// 0.25 s.
    /// </summary>
    public AppBuilder WithMaxDeltaTime(float seconds)
    {
        if (seconds <= 0.0f || !float.IsFinite(seconds))
        {
            throw new ArgumentOutOfRangeException(nameof(seconds),
                "Maximum delta time must be a positive, finite number of seconds.");
        }
        _maxDeltaTime = seconds;
        return this;
    }

    /// <summary>
    /// Builds the application. Window creation, backend probing and graphics
    /// initialization happen here; a failure rolls back everything already
    /// created and throws with an actionable message.
    /// </summary>
    public App Build()
    {
        Backend? backend = _backend;
        bool wantsWindow = _window;
        bool wantsGraphics = backend.HasValue;

        if (wantsGraphics && !wantsWindow && !_headless)
        {
            throw new InvalidOperationException(
                "WithGraphics requires a window: call WithWindow too, or use " +
                "WithHeadlessGraphics(width, height) for offscreen rendering without " +
                "an OS window.");
        }

        // Validate the backend early, before any window or engine exists
        // (Filament may abort the process at the pinned release when the
        // Vulkan backend has no usable ICD — PAL probes first).
        if (wantsGraphics)
        {
            EnsureBackendAvailable(backend!.Value);
        }

        IPlatformWindow? platformWindow = null;
        Graphics? graphics = null;
        try
        {
            if (wantsWindow)
            {
                platformWindow = PlatformFactory.CreateWindow(_title!, _width, _height);
            }

            if (wantsGraphics)
            {
                int width = _headless ? _headlessWidth : platformWindow!.Width;
                int height = _headless ? _headlessHeight : platformWindow!.Height;
                graphics = CreateGraphics(backend!.Value, platformWindow, width, height);
            }

            return new App(platformWindow, graphics, Time.CreateDefault(_maxDeltaTime));
        }
        catch
        {
            // Roll back partially created resources in reverse order.
            graphics?.Dispose();
            platformWindow?.Dispose();
            throw;
        }
    }

    private static void EnsureBackendAvailable(Backend backend)
    {
        switch (backend)
        {
            case Backend.Vulkan:
                if (!VulkanAvailability.IsUsable(out var reason))
                {
                    throw new PALException(
                        "Vulkan was requested but is not usable in this environment: " +
                        reason +
                        " Alternatively build with Backend.Noop (full object model, " +
                        "nothing drawn) on GPU-less machines.");
                }
                return;

            case Backend.OpenGl:
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    throw new PALException(
                        "The OpenGL backend requires a running X server (GLX) at engine " +
                        "creation on Linux, but DISPLAY is not set. Use the Vulkan " +
                        "backend (works headless with a software ICD) or set DISPLAY.");
                }
                return;

            case Backend.Noop:
                return; // works everywhere, draws nothing

            case Backend.Metal:
            case Backend.WebGpu:
                throw new PALException(
                    $"The {backend} backend is not supported by FilamentSharp's native " +
                    "bridge on this platform; use Vulkan (or Noop for structural runs).");

            case Backend.Default:
                // Filament resolves Default to Vulkan on Linux at the pinned
                // release; probe that path.
                if (!VulkanAvailability.IsUsable(out var defaultReason))
                {
                    throw new PALException(
                        "The default backend resolves to Vulkan here, which is not " +
                        "usable in this environment: " + defaultReason +
                        " Pass an explicit backend (e.g. Backend.Noop) to override.");
                }
                return;

            default:
                return;
        }
    }

    private static Graphics CreateGraphics(Backend backend, IPlatformWindow? window,
        int width, int height)
    {
        Engine? engine = null;
        Renderer? renderer = null;
        View? view = null;
        try
        {
            engine = Engine.Create(backend);
            renderer = engine.CreateRenderer();
            view = engine.CreateView();

            var textRenderer = new TextRenderer(engine);

            return new Graphics(engine, renderer, view, textRenderer, window, width, height);
        }
        catch
        {
            view?.Dispose();
            renderer?.Dispose();
            engine?.Dispose();
            throw;
        }
    }
}
