// PAL TextRendering: readable text over a rendered 3D scene, with content
// that updates every frame.
//
// Demonstrates:
//   - Loading a redistributable font (Inter, SIL OFL) and drawing text.
//   - Text over a moving 3D scene (a spinning lit model), composited on top.
//   - Repeated DrawText calls per frame: text is per-frame, not cumulative.
//   - MeasureText for layout (right-aligned FPS counter, dynamic widths).
//   - The application-owned loop, input and timing.
//
// Run from the repository root:
//   dotnet run --project samples/TextRendering -- --frames 600
//   Xvfb :99 -screen 0 1024x768x24 &
//   DISPLAY=:99 dotnet run --project samples/TextRendering -- --frames 300
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
                Console.WriteLine("usage: TextRendering [--frames N] [--backend vk|noop]");
                return 0;
        }
    }

    using var app = App.CreateBuilder()
        .WithWindow("PAL TextRendering", 1024, 768)
        .WithGraphics(backend)
        .Build();

    var graphics = app.Graphics!;
    Console.WriteLine($"PAL TextRendering ({graphics.ActiveBackend} backend; " +
                      "text renders on Vulkan; on NOOP frames present with no content)");

    using var scene = graphics.CreateScene();
    var camera = scene.CreateCamera();
    camera.SetPerspective(60, 0.1f, 100);
    camera.LookAt(new Vector3(0, 1.2f, 4.5f), Vector3.Zero);

    var modelPath = FindAsset("assets", "Models", "triangle.glb");
    using var model = scene.LoadModel(modelPath);
    using var light = scene.AddDirectionalLight(
        new Vector3(-0.4f, -1.0f, -0.6f), Color.White, 100_000);
    graphics.SetClearColor(Color.FromBytes(24, 26, 34));

    // The font is loaded once and reused (glyphs cache in its atlas).
    var fontPath = FindAsset("assets", "Fonts", "Inter-Regular.ttf");
    using var font = graphics.LoadFont(fontPath);

    int frame = 0;
    float spin = 0.0f;
    while (app.Window!.IsOpen)
    {
        app.PumpEvents();
        if (app.Input.IsKeyPressed(Key.Escape) || app.Input.IsKeyPressed(Key.Q))
        {
            break;
        }
        float dt = app.Time.DeltaTime;

        // Update the 3D scene: the model spins behind the text.
        spin += dt * 1.1f;
        model.Transform = Matrix4x4.CreateRotationY(spin) * Matrix4x4.CreateScale(2.2f);
        camera.LookAt(
            new Vector3(MathF.Sin(spin * 0.3f) * 1.0f, 1.2f, 4.5f),
            Vector3.Zero,
            new Vector3(0, 1, 0));

        // Draw text for THIS frame. Positions use the top-left origin.
        graphics.DrawText("Hello from PAL", font, new Vector2(24, 20), 36, Color.White);

        string status = $"frame {frame,5}   dt {(dt * 1000),6:0.0} ms   " +
                        $"fps {app.Time.Fps,5:0}   spin {spin % (2 * MathF.PI),5:0.00}";
        graphics.DrawText(status, font, new Vector2(24, 66), 22, Color.FromBytes(190, 200, 255));

        // Measured layout: right-align a label against the window edge.
        string right = $"backend: {graphics.ActiveBackend}";
        var size = graphics.MeasureText(right, font, 22);
        graphics.DrawText(right, font,
            new Vector2(app.Window.Width - size.X - 24, app.Window.Height - size.Y - 16),
            22, Color.FromBytes(160, 220, 170));

        // Multi-line text with a newline works too.
        graphics.DrawText("PAL text:\n kerning, multi-line,\n per-frame updates",
            font, new Vector2(24, app.Window.Height - 130), 22, Color.FromBytes(255, 230, 140));

        graphics.Render(scene, camera);

        frame++;
        if (frameLimit > 0 && frame >= frameLimit)
        {
            break;
        }
        Thread.Sleep(1);
    }

    Console.WriteLine($"clean exit after {frame} frame(s)");
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
