using System.Numerics;
using Xunit;
using PAL.Platform;
namespace PAL.Tests;

public class InputStateTests
{
    [Fact]
    public void KeyDownProducesPressedEdgeAndDownState()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.A);

        Assert.True(keyboard.IsDown(Key.A));
        Assert.True(keyboard.IsPressed(Key.A));
        Assert.False(keyboard.IsReleased(Key.A));
    }

    [Fact]
    public void AutoRepeatDoesNotReproducePressedEdge()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.A);   // real press
        keyboard.KeyDown(Key.A);   // X11 auto-repeat press

        Assert.True(keyboard.IsDown(Key.A));
        Assert.True(keyboard.IsPressed(Key.A));

        // Next pump with only repeats: no edge, still down.
        keyboard.BeginPump();
        keyboard.KeyDown(Key.A);   // repeat continues

        Assert.True(keyboard.IsDown(Key.A));
        Assert.False(keyboard.IsPressed(Key.A));
        Assert.False(keyboard.IsReleased(Key.A));
    }

    [Fact]
    public void KeyUpProducesReleasedEdgeAndClearsDown()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.A);

        keyboard.BeginPump();
        keyboard.KeyUp(Key.A);

        Assert.False(keyboard.IsDown(Key.A));
        Assert.False(keyboard.IsPressed(Key.A));
        Assert.True(keyboard.IsReleased(Key.A));
    }

    [Fact]
    public void PumpWithNoEventsClearsEdgesButKeepsDownState()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.W);

        keyboard.BeginPump(); // no events at all

        Assert.True(keyboard.IsDown(Key.W));
        Assert.False(keyboard.IsPressed(Key.W));
        Assert.False(keyboard.IsReleased(Key.W));
    }

    [Fact]
    public void ReleaseWithoutPressIsIgnored()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyUp(Key.A); // stray release (e.g. press happened pre-focus)

        Assert.False(keyboard.IsDown(Key.A));
        Assert.False(keyboard.IsReleased(Key.A));
    }

    [Fact]
    public void RapidTapAcrossPumpsReportsAllEdges()
    {
        var keyboard = new KeyboardState();

        keyboard.BeginPump();
        keyboard.KeyDown(Key.Space);
        Assert.True(keyboard.IsPressed(Key.Space));

        keyboard.BeginPump();
        keyboard.KeyUp(Key.Space);
        Assert.True(keyboard.IsReleased(Key.Space));
        Assert.False(keyboard.IsDown(Key.Space));

        keyboard.BeginPump();
        keyboard.KeyDown(Key.Space);
        Assert.True(keyboard.IsPressed(Key.Space));
        Assert.True(keyboard.IsDown(Key.Space));
    }

    [Fact]
    public void ClearResetsEverythingWithoutEdges()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.LeftControl);
        keyboard.KeyDown(Key.C);

        keyboard.Clear();

        Assert.False(keyboard.IsDown(Key.LeftControl));
        Assert.False(keyboard.IsDown(Key.C));
        Assert.False(keyboard.IsPressed(Key.C));
        Assert.False(keyboard.IsReleased(Key.C));
    }

    [Fact]
    public void UnknownKeyIsTrackedLikeAnyOther()
    {
        var keyboard = new KeyboardState();
        keyboard.BeginPump();
        keyboard.KeyDown(Key.Unknown);

        Assert.True(keyboard.IsDown(Key.Unknown));
        Assert.True(keyboard.IsPressed(Key.Unknown));
    }

    [Fact]
    public void MouseStateTracksEdgesPositionAndWheel()
    {
        var mouse = new MouseState();
        mouse.BeginPump();

        mouse.MouseMotion(10.0f, 20.0f);
        mouse.MouseButtonDown(MouseButton.Left);
        mouse.MouseWheel(+1.0f);
        mouse.MouseWheel(+1.0f);

        Assert.Equal(new Vector2(10.0f, 20.0f), mouse.Position);
        Assert.True(mouse.IsDown(MouseButton.Left));
        Assert.True(mouse.IsPressed(MouseButton.Left));
        Assert.Equal(2.0f, mouse.WheelDelta);

        mouse.BeginPump(); // new cycle: edges and wheel reset, position kept
        Assert.Equal(new Vector2(10.0f, 20.0f), mouse.Position);
        Assert.False(mouse.IsPressed(MouseButton.Left));
        Assert.True(mouse.IsDown(MouseButton.Left));
        Assert.Equal(0.0f, mouse.WheelDelta);

        mouse.MouseWheel(-1.0f);
        Assert.Equal(-1.0f, mouse.WheelDelta);
    }

    [Fact]
    public void MouseRepeatPressDoesNotReproduceEdge()
    {
        var mouse = new MouseState();
        mouse.BeginPump();
        mouse.MouseButtonDown(MouseButton.Right);
        mouse.MouseButtonDown(MouseButton.Right); // duplicated press event

        Assert.True(mouse.IsDown(MouseButton.Right));
        Assert.True(mouse.IsPressed(MouseButton.Right));

        mouse.BeginPump();
        mouse.MouseButtonDown(MouseButton.Right);
        Assert.True(mouse.IsDown(MouseButton.Right));
        Assert.False(mouse.IsPressed(MouseButton.Right));
    }
}

public class KeyMapTests
{
    [Theory]
    [InlineData(0x61u, Key.A)]
    [InlineData(0x41u, Key.A)]          // uppercase keysym -> same key
    [InlineData(0x7Au, Key.Z)]
    [InlineData(0x30u, Key.D0)]
    [InlineData(0x39u, Key.D9)]
    [InlineData(0x20u, Key.Space)]
    [InlineData(0xff1bu, Key.Escape)]   // XK_Escape
    [InlineData(0xff0du, Key.Enter)]    // XK_Return
    [InlineData(0xff08u, Key.Backspace)]
    [InlineData(0xff09u, Key.Tab)]
    [InlineData(0xff51u, Key.Left)]
    [InlineData(0xff54u, Key.Down)]
    [InlineData(0xffbeu, Key.F1)]
    [InlineData(0xffc9u, Key.F12)]
    [InlineData(0xffe1u, Key.LeftShift)]
    [InlineData(0xffe4u, Key.RightControl)]
    [InlineData(0xffe9u, Key.LeftAlt)]
    [InlineData(0xffb0u, Key.Keypad0)]
    [InlineData(0xffabu, Key.KeypadAdd)]
    [InlineData(0x2cu, Key.Comma)]
    [InlineData(0x60u, Key.Grave)]
    [InlineData(0x5bu, Key.LeftBracket)]
    [InlineData(0x27u, Key.Apostrophe)]
    [InlineData(0x3du, Key.Equal)]
    [InlineData(0x1234u, Key.Unknown)]  // unmapped
    public void KeysymsMapToExpectedKeys(uint keysym, Key expected)
    {
        Assert.Equal(expected, XKeyMap.ToKey(keysym));
    }
}
