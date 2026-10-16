// PAL MinimalWindow: a real OS window with input and clean exit, no scene.
//
// Demonstrates:
//   - Window creation (real X11 on Linux; needs DISPLAY or Xvfb).
//   - The application-owned loop: pump, query input, decide, exit.
//   - Keyboard state (pressed/down) and window close detection.
//   - Optional graphics: this sample initializes the NOOP backend so the
//     frame loop is real (presentable frames) without requiring a GPU.
//
// Run from the repository root:
//   dotnet run --project samples/MinimalWindow -- --backend vk|noop --frames 240
// Headless machine with a virtual display:
//   Xvfb :99 -screen 0 1024x768x24 &
//   DISPLAY=:99 dotnet run --project samples/MinimalWindow -- --frames 240
using System.Numerics;
using FilamentSharp;
using PAL;

return Run(args);

static int Run(string[] args)
{
    var backend = Backend.Vulkan;
    int frameLimit = 0; // 0 = run until the window is closed
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--backend":
                backend = args[++i] == "noop" ? Backend.Noop : Backend.Vulkan;
                break;
            case "--frames":
                frameLimit = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--help" or "-h":
                Console.WriteLine("usage: MinimalWindow [--backend vk|noop] [--frames N]");
                return 0;
            default:
                Console.WriteLine($"unknown argument '{args[i]}' (see --help)");
                return 2;
        }
    }
    return RunWindow(backend, frameLimit);
}

static int RunWindow(Backend backend, int frameLimit)
{
    using var app = App.CreateBuilder()
        .WithWindow("PAL MinimalWindow", 800, 600)
        .WithGraphics(backend)
        .Build();

    Console.WriteLine($"PAL MinimalWindow ({app.Graphics!.ActiveBackend} backend; " +
                      "close the window, or press Esc/Q to exit)");

    // Scene and camera are the application's own objects, created once
    // outside the loop. There is no component system to plug into.
    using var scene = app.Graphics!.CreateScene();
    var camera = scene.CreateCamera();
    camera.SetPerspective(60, 0.1f, 100);
    camera.LookAt(new Vector3(0, 0, 5), Vector3.Zero);

    int frames = 0;
    while (app.Window!.IsOpen)
    {
        app.PumpEvents(); // events + input snapshot + timing; never renders

        if (app.Input.IsKeyPressed(Key.Escape) || app.Input.IsKeyPressed(Key.Q))
        {
            break;
        }

        if (app.Window.WasResized)
        {
            Console.WriteLine($"  resized to {app.Window.Width}x{app.Window.Height}");
        }

        // No scene content in this sample: Render presents the clear color.
        app.Graphics.Render(scene, camera);

        frames++;
        if (frameLimit > 0 && frames >= frameLimit)
        {
            break;
        }
    }

    Console.WriteLine($"exited after {frames} frame(s); deterministic disposal on exit");
    return 0;
}
