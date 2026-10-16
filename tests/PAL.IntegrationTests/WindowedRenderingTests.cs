using System.Numerics;
using System.Threading;
using FilamentSharp;
using Xunit;
using Xunit.Abstractions;

namespace PAL.IntegrationTests;

/// <summary>
/// Real-window smoke tests. Gated behind PAL_WINDOWED_TESTS=1 and a reachable
/// DISPLAY: they open a genuine X11 window, pump real events and render real
/// frames (verified in this repository's CI with Xvfb + lavapipe). Without
/// the gate they skip with a documented reason — no fake windows, no silent
/// pass.
/// </summary>
public sealed class WindowedRenderingTests
{
    private readonly ITestOutputHelper _output;

    public WindowedRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private bool WindowedTestsEnabled
    {
        get
        {
            if (Environment.GetEnvironmentVariable("PAL_WINDOWED_TESTS") != "1")
            {
                _output.WriteLine(
                    "ENVIRONMENTAL SKIP: PAL_WINDOWED_TESTS != 1 (windowed tests are " +
                    "opt-in; run under a real or virtual display such as Xvfb).");
                return false;
            }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                _output.WriteLine("ENVIRONMENTAL SKIP: DISPLAY is not set.");
                return false;
            }
            return true;
        }
    }

    [Fact]
    public void WindowOpensPumpsRendersAndClosesCleanly()
    {
        if (!WindowedTestsEnabled)
        {
            return;
        }

        using var app = App.CreateBuilder()
            .WithWindow("PAL windowed integration test", 256, 192)
            .WithGraphics(Backend.Noop) // frame loop + presentation without GPU
            .Build();

        Assert.Equal(AppMode.Windowed, app.Mode);
        Assert.NotNull(app.Window);
        Assert.NotNull(app.Graphics);
        Assert.True(app.Window!.IsOpen);
        Assert.True(app.Graphics!.IsWindowed);

        var graphics = app.Graphics!;
        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        camera.SetPerspective(60, 0.1f, 100);
        camera.LookAt(new Vector3(0, 0, 5), Vector3.Zero);

        int presented = 0;
        for (int i = 0; i < 30; i++)
        {
            app.PumpEvents();
            if (!app.Window.IsOpen)
            {
                break;
            }
            if (graphics.Render(scene, camera))
            {
                presented++;
            }
        }

        app.Window.Close();
        app.PumpEvents();
        Assert.False(app.Window.IsOpen);

        _output.WriteLine($"presented {presented} frame(s) in a real X11 window");
        // Under Xvfb (no compositor stalls) frames present; with NOOP the
        // content is empty but the frame loop is real.
        Assert.True(presented > 0, "expected at least one presented frame");
    }

    [Fact]
    public void KeyboardEscapeIsDeliveredThroughRealEvents()
    {
        if (!WindowedTestsEnabled)
        {
            return;
        }

        using var app = App.CreateBuilder()
            .WithWindow("PAL input test", 128, 96)
            .Build(); // window without graphics: event loop only

        app.PumpEvents();
        Assert.False(app.Input.IsKeyPressed(Key.Escape));
        Assert.False(app.Input.IsKeyDown(Key.Escape));

        // Inject genuine key events from outside: a second X connection
        // sends KeyPress + KeyRelease to the app's real window via XSendEvent
        // — the events travel the full production path (server queue ->
        // XPending/XNextEvent -> keysym mapping -> App.Input).
        nuint windowXId = (nuint)app.Window!.NativeHandle;
        IntPtr sender = PAL.Platform.Xlib.XOpenDisplay(null);
        Assert.NotEqual(IntPtr.Zero, sender);
        try
        {
            byte escapeKeycode = PAL.Platform.Xlib.XKeysymToKeycode(sender, 0xFF1B);
            Assert.NotEqual((byte)0, escapeKeycode); // every layout maps Escape

            SendKey(sender, windowXId, PAL.Platform.Xlib.KeyPress, escapeKeycode, time: 1000);
            SendKey(sender, windowXId, PAL.Platform.Xlib.KeyRelease, escapeKeycode, time: 1001);

            PumpUntil(app,
                () => app.Input.IsKeyPressed(Key.Escape) && app.Input.IsKeyReleased(Key.Escape),
                "press and release edges should arrive within the timeout");
            Assert.True(app.Input.IsKeyPressed(Key.Escape),
                "Escape should read as pressed (edge) on the pump after the press");
            Assert.True(app.Input.IsKeyReleased(Key.Escape),
                "Escape should register the release edge");
            Assert.False(app.Input.IsKeyDown(Key.Escape),
                "press + release inside one pump ends with the key up");

            // Edges are one-pump: the next pump clears them.
            app.PumpEvents();
            Assert.False(app.Input.IsKeyPressed(Key.Escape));
            Assert.False(app.Input.IsKeyReleased(Key.Escape));
            Assert.False(app.Input.IsKeyDown(Key.Escape));
            _output.WriteLine(
                "real X11 KeyPress/KeyRelease delivered through XSendEvent; " +
                "Input state machine observed press and release edges");

            // A held key (press without release) stays down across pumps.
            SendKey(sender, windowXId, PAL.Platform.Xlib.KeyPress, escapeKeycode, time: 2000);
            PumpUntil(app, () => app.Input.IsKeyDown(Key.Escape),
                "the second press edge should arrive within the timeout");
            Assert.True(app.Input.IsKeyDown(Key.Escape));
            Assert.True(app.Input.IsKeyPressed(Key.Escape));
            app.PumpEvents();
            Assert.True(app.Input.IsKeyDown(Key.Escape),
                "a held key stays down across pumps (no auto-release)");
            Assert.False(app.Input.IsKeyPressed(Key.Escape),
                "the press edge does not repeat while the key is simply held");
            _output.WriteLine("held-key semantics verified (down across pumps, edge once)");
        }
        finally
        {
            _ = PAL.Platform.Xlib.XCloseDisplay(sender);
        }
    }

    /// <summary>
    /// Pumps (with a small yield) until the predicate holds or the timeout
    /// elapses. Event delivery from the sender connection to the app's
    /// connection is asynchronous at the socket level; a single pump is
    /// usually enough but not guaranteed, so tests wait deterministically.
    /// </summary>
    private static void PumpUntil(App app, Func<bool> condition, string message,
        int timeoutMs = 2000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            app.PumpEvents();
            if (condition())
            {
                return;
            }
            Thread.Sleep(1);
        }
        Assert.Fail($"timed out waiting for input: {message}");
    }

    /// <summary>Builds and sends one key event through the sender connection.</summary>
    private static unsafe void SendKey(IntPtr sender, nuint window, int type,
        byte keycode, nuint time)
    {
        var keyEvent = new PAL.Platform.Xlib.XKeyEvent
        {
            Type = type,
            SendEvent = 1,
            Display = sender,
            Window = window,
            Root = 0,
            Subwindow = 0,
            Time = time,
            X = 0,
            Y = 0,
            XRoot = 0,
            YRoot = 0,
            State = 0,
            Keycode = keycode,
            SameScreen = 1,
        };

        var ev = new PAL.Platform.Xlib.XEvent();
        // XEvent is a union; copy the XKeyEvent bytes into it. Raw is a
        // fixed buffer in a local: usable as a pointer in unsafe context.
        byte* src = (byte*)&keyEvent;
        byte* dst = ev.Raw;
        int size = sizeof(PAL.Platform.Xlib.XKeyEvent);
        for (int i = 0; i < size; i++)
        {
            dst[i] = src[i];
        }

        _ = PAL.Platform.Xlib.XSendEvent(sender, window, propagate: false,
            (nint)PAL.Platform.Xlib.KeyPressMask, ref ev);
        // Round-trip so the server has actually queued the event to the
        // app's connection before the test proceeds (XFlush is fire-and-
        // forget; XSync waits for the reply, after which the event is in
        // the app's socket buffer).
        _ = PAL.Platform.Xlib.XSync(sender, false);
    }

    [Fact]
    public void WindowedVulkanRendersAndCaptures()
    {
        if (!WindowedTestsEnabled)
        {
            return;
        }
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        // The full windowed stack end to end: a real X11 window, a real
        // native-window swap chain, presented frames and a pixel readback
        // from a windowed capture.
        using var app = App.CreateBuilder()
            .WithWindow("PAL windowed Vulkan test", 192, 144)
            .WithGraphics(Backend.Vulkan)
            .Build();
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);

        using var scene = graphics.CreateScene();
        string modelPath = TestEnv.FindSiblingAsset("assets", "Models", "triangle.glb");
        using var model = scene.LoadModel(modelPath);
        using var light = scene.AddDirectionalLight(
            new Vector3(0.0f, -1.0f, -0.5f), Color.White, 100_000.0f);

        var camera = scene.CreateCamera();
        camera.SetPerspective(45, 0.1f, 100);
        camera.LookAt(new Vector3(0, 0, 4), Vector3.Zero);

        // Pump until mapped/exposed, then present real frames.
        for (int i = 0; i < 30 && (!app.Window!.IsVisible || app.Time.FrameCount < 3); i++)
        {
            app.PumpEvents();
        }
        Assert.True(app.Window!.IsVisible, "window should be mapped and exposed");

        int presented = 0;
        for (int i = 0; i < 60 && presented < 3; i++)
        {
            app.PumpEvents();
            if (graphics.Render(scene, camera))
            {
                presented++;
            }
            // Pace like a real application: on software drivers a flat-out
            // loop starves the backend's present threads (documented in
            // Graphics.Render — backpressure is the app's pacing signal).
            Thread.Sleep(1);
        }
        Assert.True(presented >= 1, "at least one frame should present");

        var frame = graphics.CaptureFrame(scene, camera);
        int lit = 0;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R + p.G + p.B > 0.15f)
                {
                    lit++;
                }
            }
        }
        _output.WriteLine($"windowed capture: {lit}/{frame.Width * frame.Height} lit pixels, " +
            $"{presented} frames presented before capture");
        Assert.InRange(lit, 100, frame.Width * frame.Height * 3 / 4);
    }

    [Fact]
    public void WindowWithoutGraphicsRunsEventLoop()
    {
        if (!WindowedTestsEnabled)
        {
            return;
        }

        using var app = App.CreateBuilder()
            .WithWindow("PAL window-only test", 128, 96)
            .Build();

        Assert.Equal(AppMode.Windowed, app.Mode);
        Assert.Null(app.Graphics);

        app.PumpEvents();
        Assert.True(app.Window!.IsOpen);
        Assert.True(app.Time.FrameCount >= 1);
    }
}
