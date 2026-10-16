// PAL HeadlessRendering: render offscreen without an OS window, read pixels
// back and write a PPM image.
//
// Demonstrates:
//   - Headless graphics (Filament engine + offscreen swap chain, no window).
//   - Rendering a scene and capturing the exact pixels.
//   - Writing a PPM image and verifying it programmatically.
//   - Text included in the captured frame.
//   - The application-owned flow: no loop hidden inside PAL.
//
// Run from the repository root (needs a Vulkan ICD; lavapipe works):
//   source scripts/env.sh   # picks up the sibling repo's lavapipe, if any
//   dotnet run --project samples/HeadlessRendering -- [output-directory]
//
// On a machine without any Vulkan ICD, pass --backend noop to exercise the
// pipeline (frames render; pixels are undefined/empty, still written).
using System.Numerics;
using FilamentSharp;
using PAL;

return Run(args);

static int Run(string[] args)
{
    string outputDir = Directory.GetCurrentDirectory();
    var backend = Backend.Vulkan;
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--backend":
                backend = args[++i] == "noop" ? Backend.Noop : Backend.Vulkan;
                break;
            default:
                outputDir = args[i];
                break;
        }
    }

    Console.WriteLine("PAL HeadlessRendering (no OS window was created)");

    // Headless graphics: engine + offscreen swap chain at a fixed size.
    using var app = App.CreateBuilder()
        .WithHeadlessGraphics(512, 384, backend)
        .Build();

    var graphics = app.Graphics!;
    Console.WriteLine($"  mode:     {app.Mode}");
    Console.WriteLine($"  backend:  {graphics.ActiveBackend}");
    Console.WriteLine($"  size:     {graphics.FrameSize.Width}x{graphics.FrameSize.Height}");

    // A scene like any other: camera, model, light, and text.
    using var scene = graphics.CreateScene();
    var camera = scene.CreateCamera();
    camera.SetPerspective(45, 0.1f, 100);
    camera.LookAt(new Vector3(0, 1.0f, 4.5f), Vector3.Zero);

    var modelPath = FindAsset("assets", "Models", "triangle.glb");
    using var model = scene.LoadModel(modelPath);
    model.Transform = Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateScale(2.4f);

    using var light = scene.AddDirectionalLight(
        new Vector3(-0.4f, -1.0f, -0.6f), Color.White, 100_000);
    graphics.SetClearColor(Color.FromBytes(24, 26, 34));

    var fontPath = FindAsset("assets", "Fonts", "Inter-Regular.ttf");
    using var font = graphics.LoadFont(fontPath);

    // Draw text into the frame, then capture: text composites over the scene.
    graphics.DrawText("Headless PAL", font, new Vector2(20, 18), 40, Color.White);
    graphics.DrawText("rendered without a window", font, new Vector2(20, 70), 24,
        Color.FromBytes(200, 210, 255));

    // The application asks for pixels when it wants them.
    var frame = graphics.CaptureFrame(scene, camera);
    Console.WriteLine($"  captured: {frame.Width}x{frame.Height}, " +
                      $"{frame.Rgba.Length} bytes");

    // Validate and save (the sample verifies its own output: dimensions and
    // a non-trivial image; on NOOP the content is empty by design).
    string ppmPath = Path.Combine(outputDir, "pal-headless.ppm");
    frame.SavePpm(ppmPath);
    var fileInfo = new FileInfo(ppmPath);
    Console.WriteLine($"  wrote:    {ppmPath} ({fileInfo.Length} bytes)");
    Console.WriteLine();
    Console.WriteLine($"  top-left pixel: {frame.GetPixel(2, 2)}");
    Console.WriteLine("  (convert with any PPM tool, e.g. magick pal-headless.ppm out.png)");
    Console.WriteLine();
    Console.WriteLine("headless capture complete; app disposed cleanly");
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
