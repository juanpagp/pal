// PAL First3DScene: a real window with a camera, a glTF model, input-driven
// updates, animation and rendering.
//
// Demonstrates:
//   - Windowed rendering through the real Filament backend (Vulkan default).
//   - Scene, camera with perspective + look-at, model loading from GLB.
//   - Application-owned updates: the model spins, arrow keys move the camera.
//   - Input (pressed edges, held keys), timing, frame pacing decision.
//   - Deterministic disposal.
//
// Run from the repository root (assets are located by walking up):
//   dotnet run --project samples/First3DScene -- --frames 600
//   Xvfb :99 -screen 0 1024x768x24 &
//   DISPLAY=:99 dotnet run --project samples/First3DScene -- --frames 300
using System.Numerics;
using FilamentSharp;
using PAL;

return Run(args);

static int Run(string[] args)
{
    int frameLimit = 0;
    var backend = Backend.Vulkan;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--frames":
                frameLimit = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--backend":
                backend = args[++i] == "noop" ? Backend.Noop : Backend.Vulkan;
                break;
            case "--help" or "-h":
                Console.WriteLine("usage: First3DScene [--frames N] [--backend vk|noop]");
                return 0;
        }
    }

    using var app = App.CreateBuilder()
        .WithWindow("PAL First3DScene", 1024, 768)
        .WithGraphics(backend)
        .Build();

    var graphics = app.Graphics!;
    Console.WriteLine($"PAL First3DScene ({graphics.ActiveBackend} backend)");

    // --- The application builds its scene as ordinary code -----------------
    using var scene = graphics.CreateScene();
    var camera = scene.CreateCamera();
    camera.SetPerspective(fieldOfViewDegrees: 60, near: 0.1f, far: 100);
    camera.LookAt(new Vector3(0, 1.5f, 4), Vector3.Zero);

    // A glTF model from the repository's assets (self-contained GLB).
    var modelPath = FindAsset("assets", "Models", "triangle.glb");
    using var model = scene.LoadModel(modelPath);
    model.Scale(2.0f); // the sample triangle is small; PAL exposes transforms

    // A directional light: lit glTF materials need one.
    using var light = scene.AddDirectionalLight(
        new Vector3(-0.4f, -1.0f, -0.6f), Color.White, 100_000);

    graphics.SetClearColor(Color.FromBytes(24, 26, 34));

    // --- The application's own state (no framework structures) ------------
    float spin = 0.0f;
    var cameraTarget = Vector3.Zero;
    var cameraDistance = 4.0f;

    int frames = 0;
    var lastReport = -5.0;
    while (app.Window!.IsOpen)
    {
        app.PumpEvents();

        if (app.Input.IsKeyPressed(Key.Escape) || app.Input.IsKeyPressed(Key.Q))
        {
            break;
        }

        float dt = app.Time.DeltaTime;

        // Application update: spin the model; arrow keys orbit the camera.
        spin += dt * 0.8f;
        model.Transform =
            Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateScale(2.0f);

        if (app.Input.IsKeyDown(Key.Left))
        {
            cameraTarget.X -= dt * 2.0f;
        }
        if (app.Input.IsKeyDown(Key.Right))
        {
            cameraTarget.X += dt * 2.0f;
        }
        if (app.Input.IsKeyDown(Key.Up))
        {
            cameraDistance = MathF.Max(1.5f, cameraDistance - dt * 2.0f);
        }
        if (app.Input.IsKeyDown(Key.Down))
        {
            cameraDistance = MathF.Min(12.0f, cameraDistance + dt * 2.0f);
        }
        camera.LookAt(
            new Vector3(cameraTarget.X, 1.5f, cameraDistance),
            cameraTarget,
            new Vector3(0, 1, 0));

        bool presented = graphics.Render(scene, camera);
        if (presented)
        {
            frames++;
        }

        // The application decides how to report progress (and how to pace).
        if (app.Time.TotalTime - lastReport > 5.0)
        {
            lastReport = app.Time.TotalTime;
            Console.WriteLine($"  {frames} frames, {app.Time.Fps:0} fps, " +
                              $"camera {cameraDistance:0.0}m from target");
        }

        if (frameLimit > 0 && frames >= frameLimit)
        {
            break;
        }
        Thread.Sleep(1); // polite pacing; vsync-capable swap chains self-pace
    }

    Console.WriteLine($"clean exit after {frames} presented frame(s)");
    return 0;
}

static string FindAsset(params string[] relative)
{
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10; i++)
    {
        var candidate = Path.Combine([dir, .. relative]);
        if (File.Exists(candidate))
        {
            return candidate;
        }
        var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent))
        {
            break;
        }
        dir = parent;
    }
    throw new FileNotFoundException(
        $"Asset not found: {Path.Combine(relative)}. Run the sample from the " +
        "repository root.");
}
