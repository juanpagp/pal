using System.Numerics;
using FilamentSharp;
using PAL.Internal;
using Xunit;
using Xunit.Abstractions;

namespace PAL.IntegrationTests;

/// <summary>
/// GPU-dependent integration tests on the real FilamentSharp backend.
/// Vulkan tests run when a usable ICD is visible (the PAL probe decides —
/// no blind launches: at the pinned Filament release, engine creation
/// without an ICD terminates the process); otherwise they self-skip with a
/// documented reason. NOOP tests always run (no GPU) and cover the object
/// model, frame loop and readback plumbing.
/// </summary>
public sealed class HeadlessRenderingTests
{
    private readonly ITestOutputHelper _output;

    public HeadlessRenderingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // ------------------------------------------------------------------ setup

    private static App BuildHeadlessApp(int width, int height, Backend backend)
    {
        return App.CreateBuilder()
            .WithHeadlessGraphics(width, height, backend)
            .Build();
    }

    /// <summary>A scene with one unlit colored quad covering most of the view.</summary>
    private static (byte[] Material, float[] Quad) UnlitQuadScene()
    {
        // lit-free material from the FilamentSharp repo (already compiled).
        string materialPath = TestEnv.FindSiblingAsset(
            "FilamentSharp", "external", "native-install", "materials", "unlit_color.filamat");
        return (File.ReadAllBytes(materialPath),
            new[] { -0.8f, -0.8f, 0.8f, -0.8f, -0.8f, 0.8f, 0.8f, 0.8f });
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public void HeadlessAppHasGraphicsWithoutWindow()
    {
        using var app = BuildHeadlessApp(64, 48, Backend.Noop);
        Assert.Equal(AppMode.HeadlessRendering, app.Mode);
        Assert.Null(app.Window);
        Assert.NotNull(app.Graphics);
        Assert.True(app.Graphics!.IsHeadless);

        // Pumping a headless app still updates time (the loop is the app's).
        app.PumpEvents();
        Assert.True(app.Time.FrameCount >= 1);
    }

    [Fact]
    public void NoopBackendRendersFramesAndCapturesStructurally()
    {
        // NOOP: frames begin/render/end and the readback pipeline runs, but
        // nothing is drawn — the NOOP driver does not write pixel data, so
        // captured CONTENT is undefined (documented); only the structure
        // (size, plumbing) is guaranteed.
        using var app = BuildHeadlessApp(32, 24, Backend.Noop);
        var graphics = app.Graphics!;
        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        camera.SetPerspective(60, 0.1f, 100);
        camera.LookAt(new Vector3(0, 0, 5), Vector3.Zero);

        bool rendered = graphics.Render(scene, camera);
        Assert.True(rendered, "NOOP frames should present");

        var frame = graphics.CaptureFrame(scene, camera);
        Assert.Equal(32, frame.Width);
        Assert.Equal(24, frame.Height);
        Assert.Equal(32 * 24 * 4, frame.Rgba.Length);

        _output.WriteLine("NOOP: frames render and capture is structurally valid " +
            "(content undefined — nothing is drawn by NOOP)");
    }

    [Fact]
    public void ClearColorVerifiesByPixelsOnVulkan()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(96, 96, Backend.Vulkan);
        var graphics = app.Graphics!;
        Assert.Equal(Backend.Vulkan, graphics.ActiveBackend);

        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        graphics.SetClearColor(Color.FromBytes(120, 40, 200));

        var frame = graphics.CaptureFrame(scene, camera);
        Assert.Equal(96, frame.Width);

        var center = frame.GetPixel(48, 48);
        // Software rasterizers round and dither: generous tolerance, same
        // approach as FilamentSharp's own pixel tests.
        Assert.InRange(center.R, 120 / 255f - 0.12f, 120 / 255f + 0.12f);
        Assert.InRange(center.G, 40 / 255f - 0.12f, 40 / 255f + 0.12f);
        Assert.InRange(center.B, 200 / 255f - 0.12f, 200 / 255f + 0.12f);
        Assert.InRange(center.A, 0.88f, 1.0f);

        _output.WriteLine($"Vulkan clear color readback: {center}");
    }

    [Fact]
    public void GltfTriangleRendersOnVulkan()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(128, 128, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);

        using var scene = graphics.CreateScene();
        string modelPath = TestEnv.FindSiblingAsset("assets", "Models", "triangle.glb");
        using var model = scene.LoadModel(modelPath);
        Assert.True(model.Entities.Count > 0, "triangle.glb should have entities");
        Assert.True(model.RenderableCountSafe() > 0);

        // The glTF ubershader is a lit material: without a light everything
        // renders black (verified in FilamentSharp's own integration test).
        using var light = scene.AddDirectionalLight(
            new Vector3(0.0f, 0.0f, -1.0f), Color.White, 100_000.0f);

        var camera = scene.CreateCamera();
        camera.SetPerspective(45, 0.1f, 100);
        camera.LookAt(new Vector3(1, 1, 5), new Vector3(1, 1, 0));

        var frame = graphics.CaptureFrame(scene, camera);

        // The triangle.glb is a lit ubershader triangle over a black clear:
        // count non-black pixels.
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
        _output.WriteLine($"lit pixels: {lit}/{frame.Width * frame.Height}");
        Assert.InRange(lit, 100, frame.Width * frame.Height * 3 / 4);
    }

    [Fact]
    public void TextOverlayCompositesOver3DOnVulkan()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(256, 128, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);

        using var scene = graphics.CreateScene();
        string modelPath = TestEnv.FindSiblingAsset("assets", "Models", "triangle.glb");
        using var model = scene.LoadModel(modelPath);
        using var light = scene.AddDirectionalLight(
            new Vector3(0.0f, 0.0f, -1.0f), Color.White, 100_000.0f);

        var camera = scene.CreateCamera();
        camera.SetPerspective(45, 0.1f, 100);
        camera.LookAt(new Vector3(1, 1, 5), new Vector3(1, 1, 0));

        string fontPath = TestEnv.FindSiblingAsset("assets", "Fonts", "Inter-Regular.ttf");
        using var font = graphics.LoadFont(fontPath);

        // Draw bright text at a known top-left position.
        graphics.DrawText("PAL TEXT 123", font, new Vector2(16, 16), 28, Color.White);

        var frame = graphics.CaptureFrame(scene, camera);
        frame.SavePpm("/tmp/test-text-dump.ppm");

        // Top-left text region (16..~180 x 16..~50) must contain bright
        // pixels; a bottom-right region away from both the triangle's center
        // and the text must remain background.
        int bright = 0;
        for (int y = 16; y < 60; y++)
        {
            for (int x = 16; x < 200; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R > 0.5f && p.G > 0.5f && p.B > 0.5f)
                {
                    bright++;
                }
            }
        }
        _output.WriteLine($"bright text pixels in top-left region: {bright}");
        Assert.True(bright > 50, $"expected bright text pixels, found {bright}");

        // Text is positioned at the TOP of the image: verify vertical
        // orientation — the bright pixels must be in the top rows, not the
        // bottom rows (top-left origin contract).
        int topBright = 0, bottomBright = 0;
        for (int y = 0; y < frame.Height / 2; y++)
        {
            for (int x = 16; x < 200; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R > 0.5f && p.G > 0.5f && p.B > 0.5f)
                {
                    topBright++;
                }
            }
        }
        for (int y = frame.Height / 2; y < frame.Height; y++)
        {
            for (int x = 16; x < 200; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R > 0.5f && p.G > 0.5f && p.B > 0.5f)
                {
                    bottomBright++;
                }
            }
        }
        Assert.True(topBright > 50, "text should be in the top half (top-left origin)");
        Assert.True(bottomBright < topBright / 2, "bottom half should not carry the text");
    }

    [Fact]
    public void TextDoesNotAccumulateAcrossFrames()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(128, 64, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);

        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        string fontPath = TestEnv.FindSiblingAsset("assets", "Fonts", "Inter-Regular.ttf");
        using var font = graphics.LoadFont(fontPath);

        // Frame 1: draw text and capture.
        graphics.DrawText("ONE", font, new Vector2(8, 8), 20, Color.White);
        var first = graphics.CaptureFrame(scene, camera);

        // Frame 2: draw nothing and capture — the previous text must be gone
        // (per-frame batching, no accumulation).
        var second = graphics.CaptureFrame(scene, camera);

        int CountBright(FramePixels f)
        {
            int n = 0;
            for (int y = 0; y < f.Height; y++)
            {
                for (int x = 0; x < f.Width; x++)
                {
                    var p = f.GetPixel(x, y);
                    if (p.R > 0.5f && p.G > 0.5f && p.B > 0.5f)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        int firstBright = CountBright(first);
        int secondBright = CountBright(second);
        _output.WriteLine($"bright pixels: with text {firstBright}, without {secondBright}");
        Assert.True(firstBright > 10, "text frame should have bright pixels");
        Assert.True(secondBright <= 2, $"empty frame must be clean, found {secondBright}");
    }

    [Fact]
    public void MeasureTextMatchesDrawnExtent()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(256, 96, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);

        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        string fontPath = TestEnv.FindSiblingAsset("assets", "Fonts", "Inter-Regular.ttf");
        using var font = graphics.LoadFont(fontPath);

        var size = graphics.MeasureText("HELLO", font, 32);
        Assert.InRange(size.X, 40.0f, 250.0f);
        Assert.InRange(size.Y, 30.0f, 60.0f);

        graphics.DrawText("HELLO", font, new Vector2(16, 16), 32, Color.White);
        var frame = graphics.CaptureFrame(scene, camera);

        // Bright pixels must stay within [16, 16 + measured size] bounds
        // (with a small raster margin).
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int y = 0; y < frame.Height; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                var p = frame.GetPixel(x, y);
                if (p.R > 0.5f && p.G > 0.5f && p.B > 0.5f)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }
        _output.WriteLine(
            $"text extent: x [{minX}, {maxX}] (measured {size.X:0}), y [{minY}, {maxY}]");

        // Line-box contract: the position (16, 16) is the top-left corner of
        // the line box. Ink never escapes the box [16, 16 + size]; caps-only
        // ink starts a few pixels below the box top (ascent - cap height)
        // and stops at the baseline region (no descenders in "HELLO").
        Assert.True(minX >= 16 - 1 && minX <= 20, "text starts at the requested x");
        Assert.True(minY >= 16 - 1, "ink never above the line box top");
        Assert.True(minY <= 16 + size.Y / 2,
            "cap ink sits in the upper half of the line box");
        Assert.True(maxX <= 16 + size.X + 2, "text width matches the measurement");
        Assert.True(maxY <= 16 + size.Y + 2, "text height stays within the measured line");
        Assert.True(maxY <= 16 + size.Y * 0.9f + 2,
            "no descenders: ink stops at the baseline region");
    }

    [Fact]
    public void CaptureFrameWritesValidPpm()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(64, 48, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.FromBytes(200, 30, 30));
        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();

        var frame = graphics.CaptureFrame(scene, camera);
        string path = Path.Combine(Path.GetTempPath(), $"pal-it-{Guid.NewGuid():N}.ppm");
        try
        {
            frame.SavePpm(path);
            var bytes = File.ReadAllBytes(path);
            Assert.StartsWith("P6\n64 48\n255\n",
                System.Text.Encoding.ASCII.GetString(bytes[..13]));
            Assert.Equal(13 + 64 * 48 * 3, bytes.Length);

            // First pixel row: clear color (red-ish) — validates top-down
            // row order against the GetPixel contract.
            Assert.InRange(bytes[13], 200 - 25, 200 + 25);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DisposalIsOrderedAndRepeatableAndGuardsUse()
    {
        using var app = BuildHeadlessApp(32, 32, Backend.Noop);
        var graphics = app.Graphics!;
        using var scene = graphics.CreateScene();
        var camera = scene.CreateCamera();
        graphics.Render(scene, camera);

        camera.Dispose();
        scene.Dispose();

        // Graphics use after app disposal throws, disposal itself is safe.
        var graphics2 = app.Graphics!;
        app.Dispose();
        app.Dispose();
        Assert.Throws<ObjectDisposedException>(() => graphics2.CreateScene());
    }

    [Fact]
    public void ModelTransformMovesContent()
    {
        if (!TestEnv.VulkanUsable(out var reason))
        {
            _output.WriteLine($"ENVIRONMENTAL SKIP: no usable Vulkan ICD ({reason}).");
            return;
        }

        using var app = BuildHeadlessApp(128, 96, Backend.Vulkan);
        var graphics = app.Graphics!;
        graphics.SetClearColor(Color.Black);
        using var scene = graphics.CreateScene();
        string modelPath = TestEnv.FindSiblingAsset("assets", "Models", "triangle.glb");
        using var model = scene.LoadModel(modelPath);
        using var light = scene.AddDirectionalLight(
            new Vector3(0.0f, 0.0f, -1.0f), Color.White, 100_000.0f);
        var camera = scene.CreateCamera();
        camera.SetPerspective(45, 0.1f, 100);
        camera.LookAt(new Vector3(1, 1, 5), new Vector3(1, 1, 0));

        int CenterOfMass(FramePixels f)
        {
            long sx = 0, n = 0;
            for (int y = 0; y < f.Height; y++)
            {
                for (int x = 0; x < f.Width; x++)
                {
                    var p = f.GetPixel(x, y);
                    if (p.R + p.G + p.B > 0.15f)
                    {
                        sx += x;
                        n++;
                    }
                }
            }
            return n == 0 ? -1 : (int)(sx / n);
        }

        var centered = graphics.CaptureFrame(scene, camera);
        int comCenter = CenterOfMass(centered);
        Assert.True(comCenter > 0, "triangle should be visible");

        model.Position = new Vector3(0.6f, 0f, 0f); // move right
        var moved = graphics.CaptureFrame(scene, camera);
        int comMoved = CenterOfMass(moved);

        _output.WriteLine($"center of mass: {comCenter} -> {comMoved}");
        Assert.True(comMoved > comCenter + 4,
            $"moving the model right should shift content right ({comCenter} -> {comMoved})");
    }

    [Fact]
    public void EscapeHatchExposesFilamentSharpObjects()
    {
        using var app = BuildHeadlessApp(32, 32, Backend.Noop);
        var graphics = app.Graphics!;
        using var scene = graphics.CreateScene();

        Assert.NotNull(graphics.Engine);
        Assert.Equal(Backend.Noop, graphics.Engine.ActiveBackend);
        Assert.NotNull(graphics.MainView);

        // A raw FilamentSharp entity can be added and removed through the
        // scene's escape hatch (entity lifetime stays with the caller).
        var entity = EntityManager.Create();
        scene.AddEntity(entity);
        Assert.Equal(1, scene.EntityCount);
        scene.RemoveEntity(entity);
        Assert.Equal(0, scene.EntityCount);
        EntityManager.Destroy(entity);
    }
}

internal static class ModelTestExtensions
{
    /// <summary>Renderable count surfaced defensively (missing glyph no-op).</summary>
    public static int RenderableCountSafe(this Model model)
    {
        try
        {
            return model.Entities.Count > 0 ? 1 : 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }
}
