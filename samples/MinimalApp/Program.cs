// PAL MinimalApp: an application with no window, no graphics and no
// component hierarchy — ordinary C# with PAL's application utilities.
//
// Demonstrates:
//   - Core app mode: nothing from Filament is initialized.
//   - The application owns the loop: a plain `while` and a plain method.
//   - Frame timing (delta time, elapsed time, FPS).
//
// Run from the repository root:
//   dotnet run --project samples/MinimalApp
using System.Numerics;
using PAL;

return Run();

static int Run()
{
    using var app = App.CreateBuilder()
        .WithMaxDeltaTime(0.1f)
        .Build();

    Console.WriteLine("PAL MinimalApp (core mode: no window, no graphics)");
    Console.WriteLine($"  mode:      {app.Mode}");
    string graphicsState = app.Graphics == null
        ? "not initialized (by design)"
        : "unexpected!";
    Console.WriteLine($"  graphics:  {graphicsState}");

    // A perfectly ordinary simulation: no engine classes, no lifecycle.
    var state = new SimulationState { Position = new Vector3(0, 0, 0) };

    const int frames = 240;
    for (int i = 0; i < frames; i++)
    {
        // The application decides when to pump events and advance time.
        app.PumpEvents();

        UpdateSimulation(state, app.Time.DeltaTime);

        if (i % 60 == 0)
        {
            Console.WriteLine(
                $"  t={app.Time.TotalTime:0.00}s  dt={app.Time.DeltaTime * 1000:0.0}ms  " +
                $"fps={app.Time.Fps:0}  pos=({state.Position.X:0.00}, {state.Position.Y:0.00})");
        }

        // Nothing renders here: there is no graphics system to render with.
        Thread.Sleep(16); // sample pacing; PAL does not pace frames for you
    }

    Console.WriteLine("clean exit; nothing graphics-related was ever created");
    return 0;
}

/// <summary>The application's own data structure: PAL imposes none.</summary>
static void UpdateSimulation(SimulationState state, float dt)
{
    // Simple orbit, updated with the clamped frame delta.
    state.Time += dt;
    state.Position = new Vector3(
        MathF.Cos(state.Time) * 2.0f,
        MathF.Sin(state.Time * 1.3f) * 0.5f,
        0.0f);
}

internal sealed class SimulationState
{
    public float Time;
    public Vector3 Position;
}
