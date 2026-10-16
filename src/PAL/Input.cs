using System.Numerics;
namespace PAL;

/// <summary>
/// Keyboard state machine with per-pump edge tracking. Pure logic, no
/// platform dependency: the platform layer feeds events in, applications
/// query state out. Unit tested directly.
/// </summary>
internal sealed class KeyboardState
{
    private readonly bool[] _down = new bool[(int)Key.Count];
    private readonly bool[] _pressed = new bool[(int)Key.Count];
    private readonly bool[] _released = new bool[(int)Key.Count];

    /// <summary>
    /// Starts a new pump cycle: pressed/released edges are cleared. Keys that
    /// remain down without a new event keep their down state (documented
    /// PumpEvents semantics).
    /// </summary>
    public void BeginPump()
    {
        Array.Clear(_pressed);
        Array.Clear(_released);
    }

    /// <summary>
    /// A key press event. Auto-repeats (press events while the key is already
    /// down) do not produce new "pressed" edges.
    /// </summary>
    public void KeyDown(Key key)
    {
        int i = (int)key;
        if (_down[i])
        {
            return; // auto-repeat: already down, no edge
        }
        _down[i] = true;
        _pressed[i] = true;
    }

    /// <summary>A key release event.</summary>
    public void KeyUp(Key key)
    {
        int i = (int)key;
        if (!_down[i])
        {
            return;
        }
        _down[i] = false;
        _released[i] = true;
    }

    /// <summary>
    /// Drops all key state (focus loss). Keys become up without emitting
    /// "released" edges, so applications polling edges do not see synthetic
    /// events.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_down);
        Array.Clear(_pressed);
        Array.Clear(_released);
    }

    public bool IsDown(Key key) => _down[(int)key];

    public bool IsPressed(Key key) => _pressed[(int)key];

    public bool IsReleased(Key key) => _released[(int)key];
}

/// <summary>
/// Mouse state machine with per-pump edge tracking and wheel accumulation.
/// Pure logic, fed by the platform layer.
/// </summary>
internal sealed class MouseState
{
    private readonly bool[] _down = new bool[(int)MouseButton.Count];
    private readonly bool[] _pressed = new bool[(int)MouseButton.Count];
    private readonly bool[] _released = new bool[(int)MouseButton.Count];
    private Vector2 _position;
    private float _wheelDelta;

    public Vector2 Position => _position;

    public float WheelDelta => _wheelDelta;

    public void BeginPump()
    {
        Array.Clear(_pressed);
        Array.Clear(_released);
        _wheelDelta = 0.0f;
    }

    public void MouseMotion(float x, float y)
    {
        _position = new Vector2(x, y);
    }

    public void MouseButtonDown(MouseButton button)
    {
        int i = (int)button;
        if (_down[i])
        {
            return;
        }
        _down[i] = true;
        _pressed[i] = true;
    }

    public void MouseButtonUp(MouseButton button)
    {
        int i = (int)button;
        if (!_down[i])
        {
            return;
        }
        _down[i] = false;
        _released[i] = true;
    }

    /// <summary>Wheel scroll: +1 per notch up, -1 per notch down (per pump).</summary>
    public void MouseWheel(float delta)
    {
        _wheelDelta += delta;
    }

    public bool IsDown(MouseButton button) => _down[(int)button];

    public bool IsPressed(MouseButton button) => _pressed[(int)button];

    public bool IsReleased(MouseButton button) => _released[(int)button];
}

/// <summary>
/// Input snapshot for the current pump cycle: keyboard down/pressed/released
/// state, mouse position, buttons and wheel.
/// </summary>
/// <remarks>
/// <para>
/// State is updated by <see cref="App.PumpEvents"/>; nothing is polled
/// behind your back. "Pressed" and "released" refer to transitions observed
/// during the most recent pump cycle: calling <see cref="App.PumpEvents"/>
/// twice in a row reports no presses on the second call unless new events
/// arrived, while keys held down remain down (the documented semantics).
/// </para>
/// <para>
/// In headless (no window) applications the input snapshot exists but never
/// receives events; it is safe to query.
/// </para>
/// <para>
/// When the window loses focus, all keys are reset to up without synthetic
/// release events; call <see cref="ClearKeys"/> yourself to reset state on
/// other focus transitions if your application needs it.
/// </para>
/// </remarks>
public sealed class Input
{
    private readonly KeyboardState _keyboard = new();
    private readonly MouseState _mouse = new();

    internal Input()
    {
    }

    /// <summary>Whether the key is currently held down.</summary>
    public bool IsKeyDown(Key key) => _keyboard.IsDown(key);

    /// <summary>
    /// Whether the key went down during the most recent pump cycle
    /// (auto-repeats excluded).
    /// </summary>
    public bool IsKeyPressed(Key key) => _keyboard.IsPressed(key);

    /// <summary>Whether the key was released during the most recent pump cycle.</summary>
    public bool IsKeyReleased(Key key) => _keyboard.IsReleased(key);

    /// <summary>
    /// Resets all keyboard state (keys become up, edges cleared). Called
    /// automatically on window focus loss.
    /// </summary>
    public void ClearKeys() => _keyboard.Clear();

    /// <summary>
    /// Mouse position in window pixels, origin at the top-left corner of the
    /// client area, x growing right and y growing down.
    /// </summary>
    public Vector2 MousePosition => _mouse.Position;

    /// <summary>Whether the mouse button is currently held down.</summary>
    public bool IsMouseButtonDown(MouseButton button) => _mouse.IsDown(button);

    /// <summary>Whether the button went down during the most recent pump cycle.</summary>
    public bool IsMouseButtonPressed(MouseButton button) => _mouse.IsPressed(button);

    /// <summary>Whether the button was released during the most recent pump cycle.</summary>
    public bool IsMouseButtonReleased(MouseButton button) => _mouse.IsReleased(button);

    /// <summary>
    /// Vertical wheel movement accumulated during the most recent pump cycle:
    /// +1 per notch scrolled up, -1 per notch scrolled down.
    /// </summary>
    public float MouseWheelDelta => _mouse.WheelDelta;

    // ---- Platform event feed (internal) ----------------------------------

    internal void BeginPump()
    {
        _keyboard.BeginPump();
        _mouse.BeginPump();
    }

    internal void OnKeyDown(Key key) => _keyboard.KeyDown(key);

    internal void OnKeyUp(Key key) => _keyboard.KeyUp(key);

    internal void OnFocusLost() => _keyboard.Clear();

    internal void OnMouseMotion(float x, float y) => _mouse.MouseMotion(x, y);

    internal void OnMouseButtonDown(MouseButton button) => _mouse.MouseButtonDown(button);

    internal void OnMouseButtonUp(MouseButton button) => _mouse.MouseButtonUp(button);

    internal void OnMouseWheel(float delta) => _mouse.MouseWheel(delta);
}
