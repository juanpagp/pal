// FilamentSharp WindowedRendering sample.
//
// A real X11 window with live-rendered content — the counterpart of the
// headless BasicRendering sample. The full walkthrough lives in
// docs/windowed-rendering.md; the shape is:
//
//   1. X11 window (Xlib interop in Xlib.cs): create, map, WM_DELETE protocol
//   2. Filament engine (Vulkan; NOOP fallback with a warning)
//   3. Native-window swap chain from the Window XID
//   4. Scene: lit textured quad + directional light; camera orbits, mesh spins
//   5. Event loop: X events (close/resize/keys) + frame loop at the target fps
//   6. Resize: destroy + recreate the swap chain on the SAME XID, then update
//      viewport and camera aspect — never inside begin/end frame
//   7. Ordered teardown: swap chain and children, engine, then the X window
//
// Run from the repository root:
//   dotnet run --project samples/WindowedRendering -- --frames 240
// Publish as a Native AOT executable: scripts/publish-aot.sh
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using FilamentSharp;
using WindowedRendering;

return SampleApp.Run(args);

internal sealed unsafe class SampleApp
{
    private static uint s_width = 800;
    private static uint s_height = 600;
    private static int s_frameLimit;
    private static int s_fps = 60;
    private static string s_title = "FilamentSharp WindowedRendering";
    private static string s_backend = "vk";

    public static int Run(string[] args)
    {
        if (!ParseArgs(args))
        {
            return 2;
        }

        Console.WriteLine("FilamentSharp WindowedRendering sample (real on-screen frames)");
        Console.WriteLine($"  ABI version:    {NativeInfo.AbiVersion}");
        Console.WriteLine($"  Filament:       {NativeInfo.FilamentVersion} " +
                          $"({NativeInfo.FilamentCommit[..8]})");
        Console.WriteLine($"  window:         {s_width}x{s_height}, target {s_fps} fps, " +
                          (s_frameLimit > 0 ? $"{s_frameLimit} frames" : "until closed"));

        // --- 1. X11 window --------------------------------------------------
        IntPtr display = Xlib.XOpenDisplay(null);
        if (display == IntPtr.Zero)
        {
            Console.WriteLine("ERROR: XOpenDisplay failed (DISPLAY='" +
                              Environment.GetEnvironmentVariable("DISPLAY") +
                              "'). Windowed rendering needs a reachable X server " +
                              "(real display or Xvfb).");
            return 1;
        }
        int screen = Xlib.XDefaultScreen(display);
        nuint window = Xlib.XCreateSimpleWindow(
            display, Xlib.XRootWindow(display, screen),
            0, 0, s_width, s_height, 0,
            Xlib.XBlackPixel(display, screen), Xlib.XBlackPixel(display, screen));
        if (window == 0)
        {
            Console.WriteLine("ERROR: XCreateSimpleWindow failed.");
            _ = Xlib.XCloseDisplay(display);
            return 1;
        }
        _ = Xlib.XStoreName(display, window, s_title);

        // Ask the window manager to tell us about close instead of killing the
        // client: WM_DELETE_WINDOW arrives as a ClientMessage on WM_PROTOCOLS.
        nuint wmDelete = Xlib.XInternAtom(display, "WM_DELETE_WINDOW", false);
        nuint wmProtocols = Xlib.XInternAtom(display, "WM_PROTOCOLS", false);
        nuint* atoms = &wmDelete;
        _ = Xlib.XSetWMProtocols(display, window, atoms, 1);

        _ = Xlib.XSelectInput(display, window,
            Xlib.KeyPressMask | Xlib.ExposureMask | Xlib.StructureNotifyMask);
        _ = Xlib.XMapWindow(display, window);
        _ = Xlib.XSync(display, false);

        try
        {
            return RenderLoop(display, window, wmDelete, wmProtocols);
        }
        finally
        {
            _ = Xlib.XDestroyWindow(display, window);
            _ = Xlib.XCloseDisplay(display);
        }
    }

    private static int RenderLoop(IntPtr display, nuint window, nuint wmDelete, nuint wmProtocols)
    {
        // --- 2. Engine ------------------------------------------------------
        // --backend noop is an explicit escape hatch for machines without any
        // Vulkan ICD (where the vk attempt would abort inside Filament's worker
        // thread at the pinned release; see docs/windowed-rendering.md).
        Engine? engine = null;
        if (s_backend == "noop")
        {
            if (!Engine.TryCreate(Backend.Noop, out engine))
            {
                Console.WriteLine("ERROR: NOOP engine creation failed.");
                return 1;
            }
            Console.WriteLine("  backend:        NOOP (--backend noop: frames present, nothing is drawn)");
        }
        else if (!Engine.TryCreate(Backend.Vulkan, out engine))
        {
            Console.WriteLine("  backend:        Vulkan unavailable; falling back to NOOP " +
                              "(frames present, nothing is drawn)");
            if (!Engine.TryCreate(Backend.Noop, out engine))
            {
                Console.WriteLine("ERROR: no usable backend (a visible Vulkan ICD is " +
                                  "required; see docs/windowed-rendering.md)");
                return 1;
            }
        }
        Console.WriteLine($"  backend:        {engine!.ActiveBackend}");

        int exitCode;
        using (engine)
        using (var renderer = engine.CreateRenderer())
        using (var view = engine.CreateView())
        using (var scene = engine.CreateScene())
        {
            // --- 3. Native-window swap chain from the XID -------------------
            var swapChain = engine.CreateNativeSwapChain((IntPtr)window);
            bool exposed = false;
            bool running = true;
            int frames = 0, resizes = 0;

            // --- 4. Scene: lit textured quad + light -------------------------
            using var material = engine.CreateMaterial(LoadAsset("Assets", "Materials", "lit_textured.filamat"));
            using var instance = material.CreateInstance();

            const uint T = 8;
            var texels = new byte[T * T * 4];
            for (uint y = 0; y < T; y++)
            {
                for (uint x = 0; x < T; x++)
                {
                    bool red = ((x / 4) + (y / 4)) % 2 == 0;
                    int i = (int)((y * T + x) * 4);
                    texels[i] = red ? byte.MaxValue : byte.MinValue;
                    texels[i + 1] = red ? byte.MinValue : byte.MaxValue;
                    texels[i + 2] = byte.MinValue;
                    texels[i + 3] = byte.MaxValue;
                }
            }
            using var texture = engine.CreateTexture2D(T, T, 1, TextureFormat.Srgb8A8);
            texture.SetImageRgba8(0, T, T, texels);
            instance.SetTexture("baseColorMap", texture,
                TextureMinFilter.Linear, TextureMagFilter.Linear,
                TextureWrap.ClampToEdge, TextureWrap.ClampToEdge);
            instance.SetFloat("roughness", 0.4f);
            instance.SetFloat("metallic", 0.0f);

            var attributes = new VertexAttributeDescription[]
            {
                new(VertexAttribute.Position, ElementType.Float3, 0, 0, 28),
                new(VertexAttribute.Uv0, ElementType.Float2, 0, 12, 28),
                new(VertexAttribute.Tangents, ElementType.Short4, 0, 20, 28),
            };
            using var vb = engine.CreateVertexBuffer(4, 1, attributes);
            vb.SetBufferAt(0, BuildQuadVertices());
            using var ib = engine.CreateIndexBuffer(6, IndexType.UnsignedShort);
            ib.SetBuffer(MemoryMarshal.AsBytes<ushort>(new ushort[] { 0, 1, 2, 2, 1, 3 }));

            var cameraEntity = EntityManager.Create();
            using var camera = engine.CreateCamera(cameraEntity);

            var lightEntity = EntityManager.Create();
            using var light = Light.Create(engine, lightEntity, new LightOptions
            {
                Type = LightType.Directional,
                Color = Vector3.One,
                Intensity = 100_000.0f,
                Direction = Vector3.Normalize(new Vector3(-0.4f, -0.5f, -1.0f)),
            });

            var meshEntity = EntityManager.Create();
            using var renderable = Renderable.Create(engine, meshEntity, new RenderableOptions
            {
                Vertices = vb,
                Indices = ib,
                PrimitiveCount = 6,
                BoundsMin = new Vector3(-2, -2, -2),
                BoundsMax = new Vector3(2, 2, 2),
                Material = instance,
                Culling = false,
            });

            scene.AddEntity(lightEntity);
            scene.AddEntity(meshEntity);
            view.SetScene(scene);
            view.SetCamera(camera);
            renderer.SetClearOptions(0.02f, 0.02f, 0.05f, 1.0f);

            ApplyCameraAndViewport(camera, view);
            Console.WriteLine("  window mapped; rendering (close the window, press q/Esc, " +
                              "or wait for --frames)");

            // --- 5. Event + frame loop --------------------------------------
            var stopwatch = Stopwatch.StartNew();
            while (running)
            {
                // Drain all pending X events without blocking.
                while (Xlib.XPending(display) > 0)
                {
                    var ev = new Xlib.XEvent();
                    _ = Xlib.XNextEvent(display, ref ev);
                    byte* raw = ev.Raw; // locals are stack-pinned; no fixed needed
                    {
                        int type = *(int*)raw;
                        switch (type)
                        {
                            case Xlib.Expose:
                                exposed = true;
                                break;
                            case Xlib.MapNotify:
                                exposed = true; // mapped counts as presentable
                                break;
                            case Xlib.ConfigureNotify:
                            {
                                var cfg = *(Xlib.XConfigureEvent*)raw;
                                if (cfg.Window == window && cfg.Width > 0 && cfg.Height > 0 &&
                                    (cfg.Width != s_width || cfg.Height != s_height))
                                {
                                    // --- 6. Resize: swap chain must be rebuilt ---
                                    // Never inside begin/end frame; same XID, so
                                    // Filament re-derives the surface extents.
                                    swapChain.Dispose();
                                    swapChain = engine.CreateNativeSwapChain((IntPtr)window);
                                    s_width = cfg.Width;
                                    s_height = cfg.Height;
                                    ApplyCameraAndViewport(camera, view);
                                    resizes++;
                                    exposed = true;
                                }
                                break;
                            }
                            case Xlib.KeyPress:
                            {
                                var key = *(Xlib.XKeyEvent*)raw;
                                nuint sym = Xlib.XkbKeycodeToKeysym(display, key.Keycode, 0, 0);
                                if (sym == Xlib.XK_q || sym == Xlib.XK_Escape)
                                {
                                    running = false;
                                }
                                break;
                            }
                            case Xlib.ClientMessage:
                            {
                                var msg = *(Xlib.XClientMessageEvent*)raw;
                                if (msg.MessageType == wmProtocols && msg.Data0 == (ulong)wmDelete)
                                {
                                    running = false; // the user closed the window
                                }
                                break;
                            }
                        }
                    }
                }

                if (s_frameLimit > 0 && frames >= s_frameLimit)
                {
                    running = false;
                }

                if (!running)
                {
                    break;
                }

                if (!exposed)
                {
                    Thread.Sleep(4); // not yet presentable; do not render
                    continue;
                }

                // Animate: the mesh spins around Z, the camera orbits.
                float t = (float)(stopwatch.Elapsed.TotalSeconds * 0.7);
                var spin = Matrix4x4.CreateRotationZ(t);
                engine.SetTransform(meshEntity, spin);
                double a = stopwatch.Elapsed.TotalSeconds * 0.5;
                var eye = new Vector3(4 * MathF.Cos((float)a), 1.2f, 4 * MathF.Sin((float)a));
                camera.LookAt(eye, Vector3.Zero);

                if (renderer.BeginFrame(swapChain))
                {
                    renderer.Render(view);
                    renderer.EndFrame();
                    frames++;
                    if (frames % 120 == 0)
                    {
                        Console.WriteLine($"  frame {frames} ({s_width}x{s_height}, " +
                                          $"{frames / stopwatch.Elapsed.TotalSeconds:0} fps avg)");
                    }
                }
                else
                {
                    Thread.Sleep(1); // backpressure; retry next tick
                }

                // Pace to the target frame rate (vsync-capable swap chains will
                // block inside EndFrame instead; this keeps the loop polite).
                double budget = 1.0 / s_fps;
                var elapsed = stopwatch.Elapsed;
                var renderedSoFar = frames * budget;
                if (renderedSoFar > elapsed.TotalSeconds + budget)
                {
                    Thread.Sleep(1);
                }
            }

            // --- 7. Ordered teardown ----------------------------------------
            scene.RemoveEntity(meshEntity);
            scene.RemoveEntity(lightEntity);
            EntityManager.Destroy(meshEntity);
            EntityManager.Destroy(lightEntity);
            EntityManager.Destroy(cameraEntity);
            swapChain.Dispose();

            Console.WriteLine($"  rendered {frames} frame(s), {resizes} resize(s) " +
                              $"at {s_width}x{s_height}");
            exitCode = 0;
        }
        Console.WriteLine("engine disposed; window closed; sample completed cleanly");
        return exitCode;
    }

    private static void ApplyCameraAndViewport(Camera camera, View view)
    {
        double aspect = s_width / (double)Math.Max(s_height, 1);
        camera.SetProjection(45.0, aspect, 0.1, 100.0);
        view.SetViewport(0, 0, s_width, s_height);
    }

    private static bool ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--width":
                    s_width = uint.Parse(args[++i]);
                    break;
                case "--height":
                    s_height = uint.Parse(args[++i]);
                    break;
                case "--frames":
                    s_frameLimit = int.Parse(args[++i]);
                    break;
                case "--fps":
                    s_fps = int.Clamp(int.Parse(args[++i]), 1, 240);
                    break;
                case "--title":
                    s_title = args[++i];
                    break;
                case "--backend":
                    s_backend = args[++i].ToLowerInvariant();
                    if (s_backend is not ("vk" or "noop"))
                    {
                        Console.WriteLine($"ERROR: unknown backend '{args[i]}' (vk|noop).");
                        return false;
                    }
                    break;
                case "--help" or "-h":
                    Console.WriteLine(
                        "usage: WindowedRendering [--width N] [--height N] [--frames N] " +
                        "[--fps N] [--title S] [--backend vk|noop]");
                    return false;
                default:
                    Console.WriteLine($"ERROR: unknown argument '{args[i]}'.");
                    return false;
            }
        }
        if (s_width == 0 || s_height == 0)
        {
            Console.WriteLine("ERROR: --width/--height must be non-zero.");
            return false;
        }
        return true;
    }

    /// <summary>Finds a repo asset by walking up from the executable.</summary>
    private static byte[] LoadAsset(params string[] relative)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            var candidate = Path.Combine([dir, .. relative]);
            if (File.Exists(candidate))
            {
                return File.ReadAllBytes(candidate);
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
            "repository root (assets are located by walking up from the executable).");
    }

    private static byte[] BuildQuadVertices()
    {
        var data = new byte[4 * 28];
        var perVertex = new (float X, float Y, float Z, float U, float V)[]
        {
            (-0.9f, -0.9f, 0.0f, 0.0f, 0.0f),
            ( 0.9f, -0.9f, 0.0f, 1.0f, 0.0f),
            (-0.9f,  0.9f, 0.0f, 0.0f, 1.0f),
            ( 0.9f,  0.9f, 0.0f, 1.0f, 1.0f),
        };
        for (int v = 0; v < 4; v++)
        {
            int b = v * 28;
            var (x, y, z, u, vv) = perVertex[v];
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(b), x);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(b + 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(b + 8), z);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(b + 12), u);
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(b + 16), vv);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(b + 20), 0);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(b + 22), 0);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(b + 24), 0);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(b + 26), short.MaxValue);
        }
        return data;
    }
}
