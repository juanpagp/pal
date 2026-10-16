using System.Numerics;
using FilamentSharp;
using PAL.Internal;
using PAL.Platform;

namespace PAL;

/// <summary>The application mode selected through the builder.</summary>
public enum AppMode
{
    /// <summary>
    /// Ordinary application utilities (time, optionally input) with no
    /// window and no graphics: nothing from Filament is initialized.
    /// </summary>
    Core,

    /// <summary>
    /// A real on-screen window (X11 on Linux) with input and timing; the
    /// graphics backend may be initialized alongside (windowed rendering).
    /// </summary>
    Windowed,

    /// <summary>
    /// An offscreen (headless) graphics system without an OS window:
    /// rendering, pixel readback and image output without a display.
    /// </summary>
    HeadlessRendering,
}

/// <summary>
/// A PAL application: windowing, input, timing and (optionally) graphics,
/// with the application owning the loop.
/// </summary>
/// <remarks>
/// <para>
/// PAL never starts threads, schedulers or a game loop. The canonical
/// application shape:
/// </para>
/// <code>
/// using var app = App.CreateBuilder()
///     .WithWindow("Title", 1280, 720)
///     .WithGraphics(FilamentSharp.Backend.Vulkan)
///     .Build();
///
/// var graphics = app.Graphics!;
/// var scene = graphics.CreateScene();
/// var camera = scene.CreateCamera();
/// // ... set up the scene ...
///
/// while (app.Window!.IsOpen)
/// {
///     app.PumpEvents();               // events + input snapshot + timing
///     if (app.Input.IsKeyPressed(Key.Escape)) break;
///     UpdateSimulation(app.Time.DeltaTime); // your code, your order
///     graphics.Render(scene, camera); // one frame, when you say so
/// }
/// </code>
/// <para>
/// Disposal is deterministic and ordered: graphics (and its swap chain) go
/// before the window; partially initialized applications roll back
/// everything they created.
/// </para>
/// </remarks>
public sealed class App : IPlatformEventSink, IDisposable
{
    private readonly Time _time;
    private readonly Window? _window;
    private readonly IPlatformWindow? _platformWindow;
    private readonly Graphics? _graphics;
    private bool _disposed;

    internal App(IPlatformWindow? platformWindow, Graphics? graphics, Time time)
    {
        _time = time;
        _platformWindow = platformWindow;
        _window = platformWindow != null ? new Window(platformWindow) : null;
        _graphics = graphics;
        Input = new Input();
    }

    /// <summary>Starts building an application.</summary>
    public static AppBuilder CreateBuilder() => new();

    /// <summary>The mode this application was built for.</summary>
    public AppMode Mode => _graphics == null
        ? (_window == null ? AppMode.Core : AppMode.Windowed)
        : (_window == null ? AppMode.HeadlessRendering : AppMode.Windowed);

    /// <summary>
    /// The window, or null in core/headless mode. Null when the application
    /// was built without <c>WithWindow</c>.
    /// </summary>
    public Window? Window => _window;

    /// <summary>The input snapshot (exists in every mode; inert without a window).</summary>
    public Input Input { get; }

    /// <summary>Frame timing.</summary>
    public Time Time => _time;

    /// <summary>
    /// The graphics system, or null when the application was built without
    /// graphics (core or window-only applications).
    /// </summary>
    public Graphics? Graphics => _graphics;

    /// <summary>
    /// Pumps all pending window events without blocking, updates the input
    /// snapshot and advances timing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call once per loop iteration, before your update and render code.
    /// Semantics:
    /// </para>
    /// <list type="bullet">
    /// <item>All events that are pending at the time of the call are
    /// dispatched (drained fully, never blocking).</item>
    /// <item>Key and mouse "pressed"/"released" edges reflect transitions
    /// observed during <b>this</b> pump. A second pump with no new events
    /// reports no edges, while held keys stay down.</item>
    /// <item><see cref="Time.DeltaTime"/> is the time since the previous
    /// pump (clamped; see <see cref="Time"/>).</item>
    /// <item>Window resizes are recorded on <see cref="Window"/> and applied
    /// by the next <c>Graphics.Render</c> (the swap chain is rebuilt outside
    /// </item>
    /// <item>When the window is already closed, this is a cheap timing-only
    /// update.</item>
    /// </list>
    /// <para>
    /// Nothing here renders, sleeps or schedules: frame pacing and update
    /// order are the application's decisions.
    /// </para>
    /// </remarks>
    public void PumpEvents()
    {
        ThrowIfDisposed();

        Input.BeginPump();
        if (_window != null && _window.IsOpen)
        {
            _window.Pump(this);
        }
        _time.Tick();
    }

    /// <summary>
    /// Disposes the application deterministically: graphics first (its swap
    /// chain dies before the window), then the OS window.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _graphics?.Dispose();
        _platformWindow?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(App),
                "The PAL application has been disposed.");
        }
    }

    // ---- IPlatformEventSink ------------------------------------------------

    void IPlatformEventSink.OnCloseRequested()
    {
    }

    void IPlatformEventSink.OnResized(int width, int height)
    {
        _window?.OnResized(width, height);
    }

    void IPlatformEventSink.OnExpose()
    {
    }

    void IPlatformEventSink.OnMapped()
    {
    }

    void IPlatformEventSink.OnUnmapped()
    {
    }

    void IPlatformEventSink.OnFocusLost()
    {
        Input.OnFocusLost();
    }

    void IPlatformEventSink.OnFocusGained()
    {
    }

    void IPlatformEventSink.OnKeyDown(Key key)
    {
        Input.OnKeyDown(key);
    }

    void IPlatformEventSink.OnKeyUp(Key key)
    {
        Input.OnKeyUp(key);
    }

    void IPlatformEventSink.OnMouseMotion(int x, int y)
    {
        Input.OnMouseMotion(x, y);
    }

    void IPlatformEventSink.OnMouseButtonDown(MouseButton button)
    {
        Input.OnMouseButtonDown(button);
    }

    void IPlatformEventSink.OnMouseButtonUp(MouseButton button)
    {
        Input.OnMouseButtonUp(button);
    }

    void IPlatformEventSink.OnMouseWheel(float deltaY)
    {
        Input.OnMouseWheel(deltaY);
    }
}
