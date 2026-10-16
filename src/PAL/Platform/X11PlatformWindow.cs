using System.Runtime.InteropServices;

namespace PAL.Platform;

/// <summary>
/// The X11 implementation of PAL's platform window (libX11 via
/// <see cref="Xlib"/>), extracted from FilamentSharp's WindowedRendering
/// sample and extended with mouse, focus and auto-repeat handling.
/// </summary>
/// <remarks>
/// Owns the X display connection and window for its lifetime; dispose
/// destroys the window and closes the display. The XID is exposed through
/// <see cref="NativeHandle"/> for the native swap chain; Filament derives
/// the Vulkan surface from it.
/// </remarks>
internal sealed unsafe class X11PlatformWindow : IPlatformWindow
{
    private readonly IntPtr _display;
    private readonly nuint _window;
    private readonly nuint _wmDeleteAtom;
    private readonly nuint _wmProtocolsAtom;
    private readonly int _screen;
    private bool _open = true;
    private bool _visible;
    private uint _width;
    private uint _height;
    private string _title;

    public X11PlatformWindow(string title, int width, int height)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _title = title;
        _width = (uint)width;
        _height = (uint)height;

        _display = Xlib.XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            string display = Environment.GetEnvironmentVariable("DISPLAY") ?? "";
            throw new PALException(
                $"Could not open an X11 display (XOpenDisplay failed; DISPLAY='{display}'). " +
                "Windowed applications need a reachable X server: a desktop session, X " +
                "forwarding, or a virtual server such as Xvfb " +
                "(Xvfb :99 -screen 0 1024x768x24, then set DISPLAY=:99). " +
                "Headless machines can use WithHeadlessGraphics instead.");
        }
        _screen = Xlib.XDefaultScreen(_display);

        _window = Xlib.XCreateSimpleWindow(
            _display, Xlib.XRootWindow(_display, _screen),
            0, 0, _width, _height, 0,
            Xlib.XBlackPixel(_display, _screen), Xlib.XBlackPixel(_display, _screen));
        if (_window == 0)
        {
            _ = Xlib.XCloseDisplay(_display);
            throw new PALException("XCreateSimpleWindow failed; the X server refused the window.");
        }

        _ = Xlib.XStoreName(_display, _window, _title);

        // Ask the window manager to notify us on close (WM_DELETE_WINDOW via
        // WM_PROTOCOLS) instead of killing the connection.
        _wmDeleteAtom = Xlib.XInternAtom(_display, "WM_DELETE_WINDOW", false);
        _wmProtocolsAtom = Xlib.XInternAtom(_display, "WM_PROTOCOLS", false);
        nuint deleteAtom = _wmDeleteAtom;
        _ = Xlib.XSetWMProtocols(_display, _window, &deleteAtom, 1);

        _ = Xlib.XSelectInput(_display, _window,
            Xlib.KeyPressMask | Xlib.KeyReleaseMask |
            Xlib.ButtonPressMask | Xlib.ButtonReleaseMask |
            Xlib.PointerMotionMask | Xlib.FocusChangeMask |
            Xlib.ExposureMask | Xlib.StructureNotifyMask);

        _ = Xlib.XMapWindow(_display, _window);
        _ = Xlib.XSync(_display, false);
    }

    public bool IsOpen => _open;

    public bool IsVisible => _visible;

    public IntPtr NativeHandle => (IntPtr)_window;

    public int Width => checked((int)_width);

    public int Height => checked((int)_height);

    public string Title
    {
        get => _title;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _title = value;
            _ = Xlib.XStoreName(_display, _window, value);
        }
    }

    public void PumpEvents(IPlatformEventSink sink)
    {
        while (Xlib.XPending(_display) > 0)
        {
            var ev = new Xlib.XEvent();
            _ = Xlib.XNextEvent(_display, ref ev);
            byte* raw = ev.Raw; // locals are stack-pinned; no fixed needed
            Dispatch(sink, raw);
        }
    }

    private void Dispatch(IPlatformEventSink sink, byte* raw)
    {
        int type = *(int*)raw;
        switch (type)
        {
            case Xlib.Expose:
                sink.OnExpose();
                break;

            case Xlib.MapNotify:
                _visible = true;
                sink.OnMapped();
                sink.OnExpose();
                break;

            case Xlib.UnmapNotify:
                _visible = false;
                sink.OnUnmapped();
                break;

            case Xlib.ConfigureNotify:
            {
                var cfg = *(Xlib.XConfigureEvent*)raw;
                if (cfg.Window == _window && cfg.Width > 0 && cfg.Height > 0)
                {
                    bool changed = cfg.Width != _width || cfg.Height != _height;
                    _width = cfg.Width;
                    _height = cfg.Height;
                    if (changed)
                    {
                        sink.OnResized(checked((int)cfg.Width), checked((int)cfg.Height));
                    }
                }
                break;
            }

            case Xlib.KeyPress:
            {
                var key = *(Xlib.XKeyEvent*)raw;
                OnKeyEvent(sink, key.Keycode, down: true);
                break;
            }

            case Xlib.KeyRelease:
            {
                var key = *(Xlib.XKeyEvent*)raw;

                // Auto-repeat suppression: X synthesizes KeyRelease+KeyPress
                // pairs with the same keycode and timestamp. Peek the queue:
                // if the very next event is that paired press, both are a
                // repeat — drop the release and let the press arrive (it will
                // be swallowed by the keyboard state machine since the key is
                // already down).
                if (IsAutoRepeatRelease(key))
                {
                    var next = new Xlib.XEvent();
                    _ = Xlib.XNextEvent(_display, ref next);
                    break;
                }
                OnKeyEvent(sink, key.Keycode, down: false);
                break;
            }

            case Xlib.ButtonPress:
            {
                var btn = *(Xlib.XPointerEvent*)raw;
                HandleButton(sink, btn.Button, pressed: true);
                break;
            }

            case Xlib.ButtonRelease:
            {
                var btn = *(Xlib.XPointerEvent*)raw;
                HandleButton(sink, btn.Button, pressed: false);
                break;
            }

            case Xlib.MotionNotify:
            {
                var motion = *(Xlib.XPointerEvent*)raw;
                if (motion.Window == _window)
                {
                    sink.OnMouseMotion(motion.X, motion.Y);
                }
                break;
            }

            case Xlib.FocusOut:
                sink.OnFocusLost();
                break;

            case Xlib.FocusIn:
                sink.OnFocusGained();
                break;

            case Xlib.ClientMessage:
            {
                var msg = *(Xlib.XClientMessageEvent*)raw;
                if (msg.MessageType == _wmProtocolsAtom && msg.Data0 == _wmDeleteAtom)
                {
                    _open = false;
                    sink.OnCloseRequested();
                }
                break;
            }
        }
    }

    private bool IsAutoRepeatRelease(Xlib.XKeyEvent release)
    {
        if (Xlib.XPending(_display) == 0)
        {
            return false;
        }
        var peek = new Xlib.XEvent();
        _ = Xlib.XPeekEvent(_display, ref peek);
        byte* raw = peek.Raw; // locals are stack-pinned; no fixed needed
        if (*(int*)raw != Xlib.KeyPress)
        {
            return false;
        }
        var next = *(Xlib.XKeyEvent*)raw;
        return next.Keycode == release.Keycode && next.Time == release.Time;
    }

    private void OnKeyEvent(IPlatformEventSink sink, byte keycode, bool down)
    {
        // Level 0 keysym = the unshifted key at this physical position, so
        // Shift+1 reports Key.D1, not '!'.
        nuint keysym = Xlib.XkbKeycodeToKeysym(_display, keycode, 0, 0);
        Key key = XKeyMap.ToKey(keysym);
        if (down)
        {
            sink.OnKeyDown(key);
        }
        else
        {
            sink.OnKeyUp(key);
        }
    }

    private static void HandleButton(IPlatformEventSink sink, byte button, bool pressed)
    {
        // X11 mouse buttons 4/5 are the vertical wheel; they arrive as button
        // presses and are reported through the wheel API, not as buttons.
        switch (button)
        {
            case 1:
                Report(sink, MouseButton.Left, pressed);
                break;
            case 2:
                Report(sink, MouseButton.Middle, pressed);
                break;
            case 3:
                Report(sink, MouseButton.Right, pressed);
                break;
            case 4:
                if (pressed)
                {
                    sink.OnMouseWheel(+1.0f);
                }
                break;
            case 5:
                if (pressed)
                {
                    sink.OnMouseWheel(-1.0f);
                }
                break;
            case 8:
                Report(sink, MouseButton.Back, pressed);
                break;
            case 9:
                Report(sink, MouseButton.Forward, pressed);
                break;

            // Buttons 6/7 (horizontal wheel) and exotic buttons are ignored.
        }

        static void Report(IPlatformEventSink sink, MouseButton b, bool p)
        {
            if (p)
            {
                sink.OnMouseButtonDown(b);
            }
            else
            {
                sink.OnMouseButtonUp(b);
            }
        }
    }

    public void Close()
    {
        _open = false;
    }

    public void Dispose()
    {
        if (!_open)
        {
            _open = false;
        }
        // Destroy the window before closing the display connection it lives
        // on. Callers (App) dispose the native swap chain before this runs.
        _ = Xlib.XDestroyWindow(_display, _window);
        _ = Xlib.XCloseDisplay(_display);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Maps X11 keysyms to <see cref="Key"/> values. Latin-1 low bytes cover
/// letters, digits and punctuation position keys; the 0xff** range covers
/// control, navigation, keypad and modifier keys.
/// </summary>
internal static class XKeyMap
{
    public static Key ToKey(nuint keysym)
    {
        return keysym switch
        {
            // ---- Latin-1 letters (both cases map to the same key) ----
            >= 0x61 and <= 0x7A => Key.A + (int)(keysym - 0x61),
            >= 0x41 and <= 0x5A => Key.A + (int)(keysym - 0x41),

            // ---- Digits ----
            >= 0x30 and <= 0x39 => Key.D0 + (int)(keysym - 0x30),

            // ---- Punctuation position keys ----
            0x20 => Key.Space,
            0x27 => Key.Apostrophe,
            0x2C => Key.Comma,
            0x2D => Key.Minus,
            0x2E => Key.Period,
            0x2F => Key.Slash,
            0x3B => Key.Semicolon,
            0x3D => Key.Equal,
            0x5B => Key.LeftBracket,
            0x5C => Key.Backslash,
            0x5D => Key.RightBracket,
            0x60 => Key.Grave,

            // ---- Control / navigation ----
            0xFF1B => Key.Escape,
            0xFF0D => Key.Enter,
            0xFF09 => Key.Tab,
            0xFF08 => Key.Backspace,
            0xFF63 => Key.Insert,
            0xFFFF => Key.Delete,
            0xFF50 => Key.Home,
            0xFF57 => Key.End,
            0xFF55 => Key.PageUp,
            0xFF56 => Key.PageDown,
            0xFF61 => Key.PrintScreen,
            0xFF14 => Key.ScrollLock,
            0xFF13 => Key.Pause,
            0xFFE5 => Key.CapsLock,
            0xFF7F => Key.NumLock,

            // ---- Arrows ----
            0xFF51 => Key.Left,
            0xFF53 => Key.Right,
            0xFF52 => Key.Up,
            0xFF54 => Key.Down,

            // ---- Function keys ----
            >= 0xFFBE and <= 0xFFC9 => Key.F1 + (int)(keysym - 0xFFBE),

            // ---- Modifiers ----
            0xFFE1 => Key.LeftShift,
            0xFFE2 => Key.RightShift,
            0xFFE3 => Key.LeftControl,
            0xFFE4 => Key.RightControl,
            0xFFE9 => Key.LeftAlt,
            0xFFEA => Key.RightAlt,
            0xFFE7 => Key.LeftSuper,
            0xFFE8 => Key.RightSuper,

            // ---- Keypad ----
            >= 0xFFB0 and <= 0xFFB9 => Key.Keypad0 + (int)(keysym - 0xFFB0),
            0xFFAE => Key.KeypadDecimal,
            0xFFAF => Key.KeypadDivide,
            0xFFAA => Key.KeypadMultiply,
            0xFFAD => Key.KeypadSubtract,
            0xFFAB => Key.KeypadAdd,
            0xFF8D => Key.KeypadEnter,
            0xFFBD => Key.KeypadEqual,

            _ => Key.Unknown,
        };
    }
}
