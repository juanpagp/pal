namespace PAL.Platform;

/// <summary>
/// Receives window events drained by <see cref="IPlatformWindow.PumpEvents"/>.
/// Implemented by <see cref="App"/>; keeping the sink as an interface lets
/// window/event logic be unit tested with fake platform windows.
/// </summary>
internal interface IPlatformEventSink
{
    void OnCloseRequested();

    void OnResized(int width, int height);

    void OnExpose();

    void OnMapped();

    void OnUnmapped();

    void OnFocusLost();

    void OnFocusGained();

    void OnKeyDown(Key key);

    void OnKeyUp(Key key);

    void OnMouseMotion(int x, int y);

    void OnMouseButtonDown(MouseButton button);

    void OnMouseButtonUp(MouseButton button);

    void OnMouseWheel(float deltaY);
}

/// <summary>
/// The platform abstraction behind <see cref="Window"/>. One implementation
/// exists today (X11 on Linux); the interface keeps application and
/// rendering code free of windowing details so further platforms can be
/// added without touching them.
/// </summary>
internal interface IPlatformWindow : IDisposable
{
    /// <summary>Whether the window is still open (not closed by the user or API).</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Whether the window has been mapped and exposed at least once — i.e.
    /// it is presentable and rendering into it is valid.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// The platform window handle passed to Filament's native swap chain
    /// (the X11 window id on Linux).
    /// </summary>
    IntPtr NativeHandle { get; }

    int Width { get; }

    int Height { get; }

    string Title { get; set; }

    /// <summary>Drains all pending events without blocking, dispatching each.</summary>
    void PumpEvents(IPlatformEventSink sink);

    /// <summary>Marks the window closed ( destruction happens at <see cref="IDisposable.Dispose"/>).</summary>
    void Close();
}

/// <summary>Creates the platform window implementation for the current OS.</summary>
internal static class PlatformFactory
{
    /// <summary>
    /// Creates an X11 window on Linux. Throws an actionable
    /// <see cref="PALException"/> when the platform or display is not
    /// available.
    /// </summary>
    public static IPlatformWindow CreateWindow(string title, int width, int height)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PALException(
                "Window creation is only implemented for Linux/X11 in this PAL release. " +
                "macOS, Windows and Wayland-native windows are not implemented (XWayland " +
                "runs X11 clients unchanged); build an application without a window " +
                "(headless graphics or core mode) on other platforms.");
        }
        return new X11PlatformWindow(title, width, height);
    }
}
