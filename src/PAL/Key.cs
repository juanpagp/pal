#pragma warning disable CS1591 // enum members are documented by the enum-level remarks
namespace PAL;

/// <summary>
/// A keyboard key, mapped from the physical key position (not the shifted
/// character): pressing <c>Shift+1</c> on a US layout reports
/// <see cref="D1"/> plus <see cref="LeftShift"/>, not "!".
/// </summary>
/// <remarks>
/// The mapping currently covers a standard US layout subset: Latin letters,
/// digits, punctuation position keys, function keys, navigation keys, keypad
/// keys and modifiers. Layout-dependent keys outside this set report
/// <see cref="Unknown"/> and are still tracked by state queries on
/// <c>Unknown</c> (see <see cref="Input"/>).
/// </remarks>
public enum Key
{
    /// <summary>Not a recognized key (unmapped keysym). State is still tracked.</summary>
    Unknown = 0,

    // Letters (A through Z).
    A, B, C, D, E, F, G, H, I, J, K, L, M,
    N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

    // Digits (top row, D0 through D9).
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

    // Punctuation (position based)
    Space,
    Apostrophe,
    Comma,
    Minus,
    Period,
    Slash,
    Semicolon,
    Equal,
    LeftBracket,
    Backslash,
    RightBracket,
    Grave,

    // Control / navigation
    Escape,
    Enter,
    Tab,
    Backspace,
    Insert,
    Delete,
    Home,
    End,
    PageUp,
    PageDown,
    PrintScreen,
    ScrollLock,
    Pause,
    CapsLock,
    NumLock,

    // Arrows
    Left,
    Right,
    Up,
    Down,

    // Function keys
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

    // Modifiers
    LeftShift,
    RightShift,
    LeftControl,
    RightControl,
    LeftAlt,
    RightAlt,
    LeftSuper,
    RightSuper,

    // Keypad
    Keypad0,
    Keypad1,
    Keypad2,
    Keypad3,
    Keypad4,
    Keypad5,
    Keypad6,
    Keypad7,
    Keypad8,
    Keypad9,
    KeypadDecimal,
    KeypadDivide,
    KeypadMultiply,
    KeypadSubtract,
    KeypadAdd,
    KeypadEnter,
    KeypadEqual,

    /// <summary>Number of recognized keys (sentinel for array sizing).</summary>
    Count,
}

/// <summary>A mouse button.</summary>
public enum MouseButton
{
    /// <summary>The primary (usually left) button.</summary>
    Left = 0,

    /// <summary>The middle button.</summary>
    Middle = 1,

    /// <summary>The secondary (usually right) button.</summary>
    Right = 2,

    /// <summary>Extra button (X11 buttons 8, back).</summary>
    Back = 3,

    /// <summary>Extra button (X11 buttons 9, forward).</summary>
    Forward = 4,

    /// <summary>Number of buttons (sentinel for array sizing).</summary>
    Count = 5,
}

#pragma warning restore CS1591
