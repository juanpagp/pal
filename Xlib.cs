// TODO: remove, put here just to compile and run the sample
// Minimal, AOT-safe Xlib (X11 core protocol) interop for the
// WindowedRendering sample.
//
// Scope: exactly what a presentable Filament window needs — open/close the
// display, create/map/destroy a window, talk to the window manager
// (WM_DELETE_WINDOW), select events, pump them, and ask X to flush.
// Everything goes through source-generated LibraryImport (no reflection),
// so the sample publishes as Native AOT.
//
// This is a *sample*: it is deliberately not part of the FilamentSharp
// binding. Windowing is an application concern (docs/windowed-rendering.md);
// if you need a fuller X11 surface, extend this file or use a windowing
// library — the swap chain only ever needs the Window XID.
using System.Runtime.InteropServices;

namespace WindowedRendering;

/// <summary>Raw Xlib imports (libX11.so.6) plus the event structs we read.</summary>
internal static unsafe partial class Xlib
{
    internal const string LibraryName = "libX11.so.6";

    // ---- Event constants (X11 core) ------------------------------------
    internal const int KeyPress = 2;
    internal const int MapNotify = 19;
    internal const int Expose = 12;
    internal const int ConfigureNotify = 22;
    internal const int ClientMessage = 33;

    // ---- Input masks (subset) ------------------------------------------
    internal const nuint KeyPressMask = 0x1;
    internal const nuint ExposureMask = 0x1 << 15;
    internal const nuint StructureNotifyMask = 0x1 << 17;

    // ---- Keysyms we care about -----------------------------------------
    internal const nuint XK_Escape = 0xff1b;
    internal const nuint XK_q = 0x0071;

    // ---- Types ----------------------------------------------------------
    // Window/Atom/KeySym are `unsigned long` (8 bytes on LP64).
    // XEvent is a union whose largest member is 192 bytes on x86-64; we
    // treat it as a fixed buffer and overlay typed structs when reading.

    [StructLayout(LayoutKind.Sequential)]
    internal struct XEvent
    {
        public fixed byte Raw[192];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XAnyEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XExposeEvent
    {
        public int Type;
        public nuint Serial;
        public int SendEvent;
        public IntPtr Display;
        public nuint Window;
        public int X, Y;
        public uint Width, Height;
        public int Count;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XConfigureEvent
    {
        public int Type;              // 0
        public nuint Serial;          // 8
        public int SendEvent;         // 16
        public IntPtr Display;        // 24
        public nuint Event;           // 32 (the window being configured)
        public nuint Window;          // 40
        public int X, Y;              // 48
        public uint Width, Height;    // 56
        public int BorderWidth;       // 64
        public nuint Above;           // 72 (sibling window or None)
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
        public nuint MessageType;     // the Atom (e.g. WM_PROTOCOLS)
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
        public byte SameScreenPadding0;
        public byte SameScreenPadding1;
        public byte SameScreen;
    }

    // ---- Imports ---------------------------------------------------------
    // NOTE on strings: Xlib owns none of the strings we pass; the generated
    // marshalling (UTF-8, caller-allocated) is exactly right for names.

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
    internal static partial int XDestroyWindow(IntPtr display, nuint window);

    [LibraryImport(LibraryName)]
    internal static partial nuint XkbKeycodeToKeysym(IntPtr display, byte keycode,
        uint group, uint level);
}
