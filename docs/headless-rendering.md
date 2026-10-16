# Headless rendering

PAL can render without any display: a headless swap chain renders into
an offscreen surface of a configured size, frames are presented
internally, and pixels can be read back and saved. This is the workflow
for build servers, image-diff tests, thumbnails and batch asset
generation.

## The application

```csharp
using var app = App.CreateBuilder()
    .WithHeadlessGraphics(width: 512, height: 384, Backend.Vulkan)
    .Build();
var graphics = app.Graphics!;

using var scene = graphics.CreateScene();
using var model = scene.LoadModel("assets/Models/triangle.glb");
using var light = scene.AddDirectionalLight(new Vector3(0, -1, 0), Color.White, 100_000);
var camera = scene.CreateCamera();
camera.SetPerspective(45, 0.1f, 100);
camera.LookAt(new Vector3(0, 1, 3), Vector3.Zero);

FramePixels frame = graphics.CaptureFrame(scene, camera);
frame.SavePpm("output.ppm");
```

`samples/HeadlessRendering` is exactly this program, runnable with
`dotnet run --project samples/HeadlessRendering` (writes
`pal-headless.ppm` next to the repository root by default).

## `CaptureFrame`

`CaptureFrame(scene, camera, maxFrames = 8)` renders frames until one
completes a full pixel readback and returns those pixels:

- Each attempt is a complete frame (render → optional readback → end).
  The readback completes on a following frame; between attempts PAL
  yields 1 ms (bounded by `maxFrames`) so software drivers can retire
  presents — see [architecture.md](architecture.md#frame-pacing).
- Only one readback may be pending per renderer; retries while the
  original is in flight wait for the original (it belongs to the frame
  that had the content) instead of issuing a second request, which the
  backend rejects.
- If no readback completes within `maxFrames`, a `PALException` is
  thrown explaining the likely cause (backpressure starving readback on
  tiny frame budgets) rather than returning fake empty pixels.
- Text queued by `DrawText` is included in the captured frame.

`CaptureFrame` also works windowed — it is the same frame path with the
window's swap chain (verified by `WindowedVulkanRendersAndCaptures`).

## `FramePixels`

- `Width`, `Height`, `GetPixel(x, y)` (rows are top-down, matching the
  PPM convention — verified against the pinned Vulkan stack by the
  pixel-placement tests) and `SavePpm(path)`.
- PPM (P6) is the output format: trivially parseable, no image-library
  dependency, convertible with any tool (`magick in.ppm out.png`).
- `FramePixels.Write(path, w, h, rgba)` exists as a standalone helper
  (unit tested) for applications that produce their own RGBA buffers.

## Backend notes

- **Vulkan** requires a usable ICD even headless. On GPU-less machines,
  llvmpipe (lavapipe) is the intended ICD; FilamentSharp's
  `scripts/setup-headless-vulkan.sh` stages a local copy, and PAL's
  `scripts/env.sh` exports `VK_ICD_FILENAMES` pointing at it.
- **Noop** runs the same API surface with no GPU: `CaptureFrame`
  returns a valid-sized, zero-filled buffer (nothing is drawn), which
  keeps the frame plumbing testable everywhere.
- Filament aborts on Vulkan engine creation without an ICD (at the
  pinned release), so PAL probes first (`VulkanAvailability`) —
  applications on unknown machines should follow the same
  probe-then-fallback pattern the integration tests use.

## What headless rendering is verified for

Rendering, readback and PPM output are verified by pixel-content
assertions under llvmpipe (clear colors, a lit glTF triangle, text
placement and orientation) — see [testing.md](testing.md). The output
image itself is written by `CaptureFrameWritesValidPpm` and the
headless sample; "visually correct" beyond those pixel assertions
(e.g. exact PBR appearance) is not claimed.
