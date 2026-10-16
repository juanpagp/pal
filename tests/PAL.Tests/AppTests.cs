using System.Numerics;
using Xunit;
using PAL.Platform;

namespace PAL.Tests;

/// <summary>
/// A deterministic in-memory platform window for testing App's event flow,
/// resize reporting and input plumbing without X11.
/// </summary>
internal sealed class FakePlatformWindow : IPlatformWindow
{
    private readonly List<Action<IPlatformEventSink>> _pending = new();

    public bool IsOpen { get; private set; } = true;

    public bool IsVisible { get; private set; } = true;

    public IntPtr NativeHandle => (IntPtr)0x1234;

    public int Width { get; private set; } = 800;

    public int Height { get; private set; } = 600;

    public string Title { get; set; } = "Fake";

    /// <summary>Queues events to deliver on the next pump.</summary>
    public void Enqueue(Action<IPlatformEventSink> action) => _pending.Add(action);

    public void SimulateResize(int width, int height)
    {
        Width = width;
        Height = height;
        _pending.Add(sink => sink.OnResized(width, height));
    }

    public void SimulateClose() => _pending.Add(sink => sink.OnCloseRequested());

    public void PumpEvents(IPlatformEventSink sink)
    {
        foreach (var action in _pending)
        {
            action(sink);
        }
        _pending.Clear();
    }

    public void Close() => IsOpen = false;

    public void Dispose()
    {
    }
}

/// <summary>
/// Constructs Apps around a fake platform window. App's constructor is
/// internal and takes IPlatformWindow directly, so windowed app logic is
/// testable without a display; graphics stay null (never initialized).
/// </summary>
internal static class TestAppFactory
{
    public static (App App, FakePlatformWindow Window) CreateWindowlessGraphicsApp(
        double startTime = 0.0, float maxDelta = 0.25f)
    {
        var platform = new FakePlatformWindow();
        var app = new App(platform, null, new Time(() => startTime, maxDelta));
        return (app, platform);
    }
}

public class AppTests
{
    [Fact]
    public void CoreAppHasNoWindowAndNoGraphics()
    {
        using var app = App.CreateBuilder().Build();
        Assert.Equal(AppMode.Core, app.Mode);
        Assert.Null(app.Window);
        Assert.Null(app.Graphics);
        Assert.NotNull(app.Input);
        Assert.NotNull(app.Time);
    }

    [Fact]
    public void CoreAppPumpUpdatesTimeOnly()
    {
        double now = 0.0;
        var app = new App(null, null, new Time(() => now, 1.0f));
        now = 0.016;
        app.PumpEvents();
        Assert.Equal(0.016f, app.Time.DeltaTime, 5);
        Assert.False(app.Input.IsKeyDown(Key.A)); // inert input, safe to query
    }

    [Fact]
    public void DisposingTwiceIsSafe()
    {
        var app = App.CreateBuilder().Build();
        app.Dispose();
        app.Dispose();
    }

    [Fact]
    public void UsingAppAfterDisposeThrows()
    {
        var app = App.CreateBuilder().Build();
        app.Dispose();
        Assert.Throws<ObjectDisposedException>(() => app.PumpEvents());
    }

    [Fact]
    public void WindowedAppPumpsEventsIntoInput()
    {
        var (app, window) = TestAppFactory.CreateWindowlessGraphicsApp();
        window.Enqueue(sink => sink.OnKeyDown(Key.W));
        window.Enqueue(sink => sink.OnKeyUp(Key.W));
        app.PumpEvents();

        // Both edges in one pump: pressed and released, currently up.
        Assert.True(app.Input.IsKeyPressed(Key.W));
        Assert.True(app.Input.IsKeyReleased(Key.W));
        Assert.False(app.Input.IsKeyDown(Key.W));

        app.PumpEvents(); // second pump, no events: edges clear
        Assert.False(app.Input.IsKeyPressed(Key.W));
        Assert.False(app.Input.IsKeyReleased(Key.W));
    }

    [Fact]
    public void HeldKeyStaysDownAcrossPumps()
    {
        var (app, window) = TestAppFactory.CreateWindowlessGraphicsApp();
        window.Enqueue(sink => sink.OnKeyDown(Key.Left));
        app.PumpEvents();
        Assert.True(app.Input.IsKeyDown(Key.Left));

        app.PumpEvents(); // no release event arrives
        app.PumpEvents();
        Assert.True(app.Input.IsKeyDown(Key.Left));
    }

    [Fact]
    public void ResizeIsReportedOnWindow()
    {
        var (app, window) = TestAppFactory.CreateWindowlessGraphicsApp();
        Assert.False(app.Window!.WasResized);

        window.SimulateResize(1024, 768);
        app.PumpEvents();

        Assert.Equal(1024, app.Window.Width);
        Assert.Equal(768, app.Window.Height);
        Assert.True(app.Window.WasResized);

        app.PumpEvents();
        Assert.False(app.Window.WasResized); // edge only in the pump it arrived
    }

    [Fact]
    public void FocusLossClearsKeysWithoutReleaseEdges()
    {
        var (app, window) = TestAppFactory.CreateWindowlessGraphicsApp();
        window.Enqueue(sink => sink.OnKeyDown(Key.Space));
        app.PumpEvents();
        Assert.True(app.Input.IsKeyDown(Key.Space));

        window.Enqueue(sink => sink.OnFocusLost());
        app.PumpEvents();

        Assert.False(app.Input.IsKeyDown(Key.Space));
        Assert.False(app.Input.IsKeyReleased(Key.Space)); // no synthetic edge
    }

    [Fact]
    public void MouseEventsFlowIntoInput()
    {
        var (app, window) = TestAppFactory.CreateWindowlessGraphicsApp();
        window.Enqueue(sink => sink.OnMouseMotion(100, 200));
        window.Enqueue(sink => sink.OnMouseButtonDown(MouseButton.Left));
        window.Enqueue(sink => sink.OnMouseWheel(-1.0f));
        app.PumpEvents();

        Assert.Equal(new Vector2(100, 200), app.Input.MousePosition);
        Assert.True(app.Input.IsMouseButtonDown(MouseButton.Left));
        Assert.True(app.Input.IsMouseButtonPressed(MouseButton.Left));
        Assert.Equal(-1.0f, app.Input.MouseWheelDelta);

        app.PumpEvents();
        Assert.Equal(0.0f, app.Input.MouseWheelDelta);
    }

    [Fact]
    public void PumpAfterCloseKeepsTickingTime()
    {
        double now = 0.0;
        var platform = new FakePlatformWindow();
        var app = new App(platform, null, new Time(() => now, 1.0f));
        platform.SimulateClose();
        app.PumpEvents();
        now += 0.5;
        app.PumpEvents();
        Assert.Equal(0.5f, app.Time.DeltaTime, 5);
    }
}

public class AppBuilderValidationTests
{
    [Fact]
    public void HeadlessGraphicsAndWindowAreRejected()
    {
        var builder = App.CreateBuilder()
            .WithHeadlessGraphics(256, 256);
        Assert.Throws<InvalidOperationException>(() => builder.WithWindow("x"));

        var builder2 = App.CreateBuilder()
            .WithWindow("x");
        Assert.Throws<InvalidOperationException>(() => builder2.WithHeadlessGraphics(256, 256));
    }

    [Fact]
    public void GraphicsAfterHeadlessIsRejected()
    {
        var builder = App.CreateBuilder()
            .WithHeadlessGraphics(256, 256);
        Assert.Throws<InvalidOperationException>(() => builder.WithGraphics());
    }

    [Fact]
    public void GraphicsWithoutWindowIsRejectedAtBuild()
    {
        var builder = App.CreateBuilder()
            .WithGraphics(FilamentSharp.Backend.Noop);
        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("WithHeadlessGraphics", ex.Message);
    }

    [Fact]
    public void WindowWithoutGraphicsBuildsACleanWindowedApp()
    {
        // Window creation itself does not require graphics: use the fake
        // platform via the internal constructor instead of the real X11 one.
        using var app = TestAppFactory.CreateWindowlessGraphicsApp().App;
        Assert.Equal(AppMode.Windowed, app.Mode);
        Assert.Null(app.Graphics);
    }

    [Fact]
    public void WindowDimensionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithWindow("x", 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithWindow("x", 100, 0));
        Assert.Throws<ArgumentException>(() =>
            App.CreateBuilder().WithWindow(""));
    }

    [Fact]
    public void HeadlessDimensionsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithHeadlessGraphics(0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithHeadlessGraphics(100, -1));
    }

    [Fact]
    public void UnsupportedBackendsAreRejectedBeforeAnythingIsCreated()
    {
        var ex = Assert.Throws<PALException>(() =>
            App.CreateBuilder().WithWindow("x").WithGraphics(FilamentSharp.Backend.Metal).Build());
        Assert.Contains("Metal", ex.Message);

        Assert.Throws<PALException>(() =>
            App.CreateBuilder().WithHeadlessGraphics(64, 64, FilamentSharp.Backend.WebGpu).Build());
    }

    [Fact]
    public void OpenGLWithoutDisplayIsRejected()
    {
        string? previous = Environment.GetEnvironmentVariable("DISPLAY");
        Environment.SetEnvironmentVariable("DISPLAY", null);
        try
        {
            var ex = Assert.Throws<PALException>(() =>
                App.CreateBuilder().WithWindow("x")
                    .WithGraphics(FilamentSharp.Backend.OpenGl).Build());
            Assert.Contains("X server", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", previous);
        }
    }

    [Fact]
    public void NullOrInvalidArgumentsThrowEarly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithWindow("x", -1, 100));
    }
}
