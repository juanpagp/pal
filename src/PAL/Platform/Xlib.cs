// Minimal, AOT-safe Xlib (X11 core protocol) interop for PAL's Linux/X11
// platform window layer.
//
// Scope: exactly what a presentable PAL window needs — open/close the
// display, create/map/destroy a window, window-manager close protocol,
// select + pump events (keyboard with auto-repeat suppression, mouse,
// resize, focus), and flush.
//
// Everything goes through source-generated LibraryImport (no reflection, no
// DllImport), so PAL stays Native-AOT friendly.
using System.Runtime.InteropServices;

namespace PAL.Platform;

/// <summary>Raw Xlib imports (libX11.so.6) plus the event structs we read.</summary>
internal static unsafe partial class Xlib
{
    internal const string LibraryName = "libX11.so.6";

    // ---- Event constants (X11 core) --------------------------------------
    internal const int KeyPress = 2;
    internal const int KeyRelease = 3;
    internal const int ButtonPress = 4;
    internal const int ButtonRelease = 5;
    internal const int MotionNotify = 6;
    internal const int EnterNotify = 7;
    internal const int LeaveNotify = 8;
    internal const int FocusIn = 9;
    internal const int FocusOut = 10;
    internal const int KeymapNotify = 11;
    internal const int Expose = 12;
    internal const int GraphicsExpose = 13;
    internal const int NoExpose = 14;
    internal const int CirculateNotify = 26;
    internal const int ConfigureNotify = 22;
    internal const int CreateNotify = 16;
    internal const int DestroyNotify = 17;
    internal const int GravityNotify = 24;
    internal const int MapNotify = 19;
    internal const int ReparentNotify = 18;
    internal const int UnmapNotify = 20;
    internal const int MapRequest = 20;
    internal const int ClientMessage = 33;

    // ---- Input masks -------------------------------------------------------
    internal const nuint KeyPressMask = 0x1;
    internal const nuint KeyReleaseMask = 0x2;
    internal const nuint ButtonPressMask = 0x4;
    internal const nuint ButtonReleaseMask = 0x8;
    internal const nuint PointerMotionMask = 0x1 << 6;
    internal const nuint FocusChangeMask = 0x1 << 18;
    internal const nuint ExposureMask = 0x1 << 15;
    internal const nuint StructureNotifyMask = 0x1 << 17;

    // ---- Types -------------------------------------------------------------

    /// <summary>
    /// XEvent is a union whose largest member is 192 bytes on x86-64; we
    /// treat it as a fixed buffer and overlay typed structs when reading.
    /// XNextEvent writes into it; stack-locals are pinned during the call.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XEvent
    {
        public fixed byte Raw[192];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XConfigureEvent
    {
        public int Type;              // 0
        public nuint Serial;          // 8
        public int SendEvent;         // 16
        public IntPtr Display;        // 24
        public nuint Event;           // 32
        public nuint Window;          // 40
        public int X, Y;              // 48
        public uint Width, Height;    // 56
        public int BorderWidth;       // 64
        public nuint Above;           // 72
        public int OverrideRedirect;  // 80
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XClientMessageEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint MessageType;
        public int Format;
        public ulong Data0, Data1, Data2, Data3, Data4;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XKeyEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint Root, Subwindow;
        public nuint Time;
        public int X, Y, XRoot, YRoot;
        public uint State;
        public byte Keycode;
        public byte Pad0, Pad1;
        public byte SameScreen;
    }

    /// <summary>XButtonEvent / XMotionEvent share this layout (same first fields).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct XPointerEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint Root, Subwindow;
        public nuint Time;
        public int X, Y, XRoot, YRoot;
        public uint State;
        public byte Button;
        public byte Pad0, Pad1, Pad2;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XCrossingEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public nuint Root, Subwindow;
        public nuint Time;
        public int X, Y, XRoot, YRoot;
        public uint Mode;    // NotifyNormal / NotifyGrab / NotifyUngrab
        public uint Detail;  // NotifyVirtual etc.
    }

    // ---- Imports ------------------------------------------------------------

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr XOpenDisplay(string? displayName);

    [LibraryImport(LibraryName)]
    internal static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibraryName)]
    internal static partial int XDefaultScreen(IntPtr display);

    [LibraryImport(LibraryName)]
    internal static partial nuint XRootWindow(IntPtr display, int screenNumber);

    [LibraryImport(LibraryName)]
    internal static partial nuint XBlackPixel(IntPtr display, int screenNumber);

    [LibraryImport(LibraryName)]
    internal static partial nuint XCreateSimpleWindow(
        IntPtr display, nuint parent,
        int x, int y, uint width, uint height,
        uint borderWidth, nuint border, nuint background);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int XStoreName(IntPtr display, nuint window, string name);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nuint XInternAtom(IntPtr display, string atomName,
        [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [LibraryImport(LibraryName)]
    internal static partial int XSetWMProtocols(IntPtr display, nuint window,
        nuint* protocols, int count);

    [LibraryImport(LibraryName)]
    internal static partial int XSelectInput(IntPtr display, nuint window, nuint eventMask);

    [LibraryImport(LibraryName)]
    internal static partial int XMapWindow(IntPtr display, nuint window);

    [LibraryImport(LibraryName)]
    internal static partial int XSync(IntPtr display, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [LibraryImport(LibraryName)]
    internal static partial int XFlush(IntPtr display);

    [LibraryImport(LibraryName)]
    internal static partial int XPending(IntPtr display);

    [LibraryImport(LibraryName)]
    internal static partial int XNextEvent(IntPtr display, ref XEvent ev);

    [LibraryImport(LibraryName)]
    internal static partial int XPeekEvent(IntPtr display, ref XEvent ev);

    [LibraryImport(LibraryName)]
    internal static partial int XDestroyWindow(IntPtr display, nuint window);

    [LibraryImport(LibraryName)]
    internal static partial nuint XkbKeycodeToKeysym(IntPtr display, byte keycode,
        uint group, uint level);

    [LibraryImport(LibraryName)]
    internal static partial byte XKeysymToKeycode(IntPtr display, nuint keysym);

    // XSendEvent injects an event into a window's queue from outside; used
    // by integration tests to deliver genuine key events through the real
    // event pipeline (XPending/XNextEvent -> platform window -> App.Input).
    [LibraryImport(LibraryName)]
    internal static partial int XSendEvent(
        IntPtr display, nuint window,
        [MarshalAs(UnmanagedType.Bool)] bool propagate,
        nint eventMask,
        ref XEvent ev);
}
