# PAL — Portable Applications Library

PAL is a lightweight C# application and rendering framework for writing real
graphical programs against [Filament](https://github.com/google/filament),
Google's physically based renderer, through the
[FilamentSharp](https://github.com/juanpagp/FilamentSharp) bindings.

PAL is a **framework, not an engine**. Your application owns the main loop and
all program state; PAL owns the infrastructure: a real window, input, timing,
the renderer, model loading and text. There is no game loop, no scheduler, no
component system and no entity hierarchy — and each part is usable
independently. A pure console application that only wants a monotonic clock
never touches Filament at all.

```csharp
using var app = App.CreateBuilder()
    .WithWindow("My PAL Application", 1280, 720)
    .WithGraphics(Backend.Vulkan)
    .Build();

var graphics = app.Graphics!;
using var scene = graphics.CreateScene();
var camera = scene.CreateCamera();
camera.SetPerspective(fieldOfViewDegrees: 60, near: 0.1, far: 1000);
camera.LookAt(new Vector3(0, 2, 5), Vector3.Zero);

using var model = scene.LoadModel("assets/model.glb");
using var font = graphics.LoadFont("assets/Fonts/Inter-Regular.ttf");

while (app.Window!.IsOpen)
{
    app.PumpEvents();                      // events, input edges, timing
    if (app.Input.IsKeyPressed(Key.Escape)) break;

    float dt = app.Time.DeltaTime;         // clamped, monotonic

    graphics.DrawText("Hello from PAL", font, new Vector2(20, 20), 24, Color.White);
    graphics.Render(scene, camera);        // false = frame skipped, try later
}
```

## What PAL provides

| Area | Entry points | Notes |
|------|-------------|-------|
| Application lifecycle | `App`, `AppBuilder` | Three modes: windowed, headless rendering, core (no graphics). Builder validates configuration and rolls back partially-initialized state on failure. |
| Windowing | `AppBuilder.WithWindow`, `Window` | Real X11 windows on Linux (Xlib via source-generated interop, Native AOT friendly). Resize rebuilds the native swap chain outside frame boundaries. |
| Input | `App.Input` | Keyboard and mouse with per-pump edge semantics (`IsKeyPressed`/`IsKeyDown`/`IsKeyReleased`), auto-repeat suppression, a public `Key` enum. |
| Timing | `App.Time` | Monotonic `DeltaTime`/`TotalTime`, `Fps` over a 30-frame window, delta clamping (`WithMaxDeltaTime`). |
| Rendering | `Graphics` | The frame contract: `Render` returns `false` on backpressure — skip and retry, PAL never busy-waits. |
| Scene graph conveniences | `Scene`, `Camera`, `Model`, `Light` | Plain-C façades over FilamentSharp types; escape hatch to the raw bindings via `.Native` / `.Engine`. |
| Text | `Graphics.DrawText` / `MeasureText`, `Font` | First-class CPU-rasterized glyph atlas, batched screen-space quads, precompiled material. |
| Headless output | `Graphics.CaptureFrame`, `FramePixels` | Offscreen rendering with pixel readback and PPM image output — no display required. |

## Requirements

- .NET 10 SDK (`global.json` in the sibling FilamentSharp repo pins the SDK).
- Linux with X11 for windows (the platform layer; verified under Xvfb).
- A Vulkan-capable GPU or a software ICD (Mesa llvmpipe / lavapipe works and
  is what the test suite runs on). `Backend.Noop` runs the full frame loop
  without a GPU.
- The FilamentSharp repository checked out **next to** this one: PAL's
  `nuget.config` consumes the locally packed `FilamentSharp` NuGet and the
  native `libFilamentSharp.Native.so` it produces. See
  [docs/getting-started.md](docs/getting-started.md).

## Documentation

- [Getting started](docs/getting-started.md) — checkout, build, run the
  samples, structure of a PAL application.
- [Architecture](docs/architecture.md) — design rules, layering, the frame
  contract, ownership, threading.
- [Text rendering](docs/text-rendering.md) — the glyph pipeline, coordinate
  conventions, atlas behavior.
- [Headless rendering](docs/headless-rendering.md) — offscreen rendering and
  pixel verification without a display.
- [Testing](docs/testing.md) — unit and GPU integration suites, environment
  gating, how to run them.
- [Implementation report](docs/implementation-report.md) — what is tested,
  what is implemented but not visually verified, and what is not
  implemented. Read this for an honest status.

## Samples

| Sample | Shows |
|--------|-------|
| `samples/MinimalApp` | Core mode: timing and a simulation loop, no window, no graphics initialized. |
| `samples/MinimalWindow` | A real window and the frame loop (Vulkan or Noop). |
| `samples/First3DScene` | Scene, camera, glTF model, lights, input-driven updates. |
| `samples/TextRendering` | Text over a moving 3D scene, per-frame updates, measurement. |
| `samples/HeadlessRendering` | Offscreen render, pixel readback, PPM image output. |

## Repository layout

```
src/PAL/            the library (App, Window, Input, Time, Graphics, Scene,
                    Camera, Model, Light, Text/, Platform/, Internal/)
samples/            five runnable samples
tests/PAL.Tests             unit tests (no GPU, no window)
tests/PAL.IntegrationTests  real-backend tests (Vulkan when an ICD exists,
                            Noop always; real-window tests opt-in)
assets/             self-contained sample assets (Inter font, GLB, materials)
docs/               the documentation set
scripts/            environment + test runner helpers
```

## Status

Linux/X11 with Vulkan (or llvmpipe) is the implemented and tested platform.
See [docs/implementation-report.md](docs/implementation-report.md) for the
full, honest status table — including what is deliberately not implemented.
