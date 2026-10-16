using PAL.Platform;
namespace PAL;

/// <summary>
/// The public facade over the platform window: open/close state, size, title
/// and the native handle for advanced use.
/// </summary>
/// <remarks>
/// <para>
/// The window is created by <c>AppBuilder.WithWindow</c> and owned by the
/// <see cref="App"/>; dispose the app to destroy it. Filament's native swap
/// chain is bound to <see cref="NativeHandle"/> internally and PAL rebuilds
/// it on resize before the next rendered frame.
/// </para>
/// <para>
/// Events (close, resize, keys, mouse) are delivered while
/// <see cref="App.PumpEvents"/> runs; query state afterwards.
/// </para>
/// </remarks>
public sealed class Window
{
    private readonly IPlatformWindow _platform;
    private bool _wasResized;

    internal Window(IPlatformWindow platform)
    {
        _platform = platform;
    }

    /// <summary>Whether the window is still open (not closed by the user or API).</summary>
    public bool IsOpen => _platform.IsOpen;

    /// <summary>
    /// Current client size in pixels. Updated during
    /// <see cref="App.PumpEvents"/> when resize events arrive.
    /// </summary>
    public int Width => _platform.Width;

    /// <summary>Current client height in pixels.</summary>
    public int Height => _platform.Height;

    /// <summary>
    /// True when the window's size changed during the most recent pump
    /// cycle (analogous to a "pressed" edge). Rendering code consumes resizes
    /// automatically; this property is for application logic that also cares.
    /// </summary>
    public bool WasResized => _wasResized;

    /// <summary>The window title.</summary>
    public string Title
    {
        get => _platform.Title;
        set => _platform.Title = value;
    }

    /// <summary>
    /// Whether the window has been mapped and exposed — i.e. it is
    /// presentable. Rendering into an unexposed window is skipped by
    /// <see cref="Graphics.Render"/>.
    /// </summary>
    public bool IsVisible => _platform.IsVisible;

    /// <summary>
    /// The platform window handle (the X11 window id on Linux). Advanced
    /// users can pass it to FilamentSharp's
    /// <c>Engine.CreateNativeSwapChain</c> themselves; PAL already does this
    /// for the main frame.
    /// </summary>
    public IntPtr NativeHandle => _platform.NativeHandle;

    /// <summary>
    /// Requests that the window close: <see cref="IsOpen"/> turns false and
    /// the app's loop should end. The native window is destroyed when the
    /// app is disposed.
    /// </summary>
    public void Close() => _platform.Close();

    internal void Pump(IPlatformEventSink sink)
    {
        _wasResized = false;
        _platform.PumpEvents(sink);
    }

    internal void OnResized(int width, int height)
    {
        _wasResized = true;
    }
}
