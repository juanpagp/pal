# Implementation report

PAL's first complete version, as of this repository's current commit.
This report is the honest ledger: what is implemented and **tested**,
what is implemented but **not visually verified**, and what is
**deliberately not implemented**. Nothing in this report overstates.

The development and verification environment: Linux (Debian 13), .NET
10 SDK, X11 windows under Xvfb, Filament 1.78 through FilamentSharp's
native bridge, Vulkan via the Mesa software rasterizer (llvmpipe /
lavapipe, Vulkan API 1.4). No hardware GPU was present.

## 1. Tested (automated, behavior- and pixel-asserted)

Everything in this section passes in this repository's environment:
**106 tests (91 unit + 15 integration; 12 of the integration tests run
their real path always, 3 more with the window gate enabled)**, plus
the five samples executed end to end.

| Area | What is asserted | Where |
|------|------------------|-------|
| App/builder | Three modes; validation (headless+window, dimensions, unsupported backends, OpenGL without a display) **before** any native object is created; rollback on partial failure; idempotent double dispose; use-after-dispose throws; core mode never touches Filament | `PAL.Tests` AppTests |
| Input | Press/hold/release edges per pump; auto-repeat produces no new edge; release-without-press ignored; focus loss clears keys without release edges; mouse edges/position/wheel; keysym→`Key` mapping | `PAL.Tests` InputStateTests (through the real `App` + fake platform window) |
| Input, real events | Genuine X11 `KeyPress`/`KeyRelease` injected via a second X connection (`XSendEvent`, round-tripped with `XSync`) travel the production path: server queue → `XPending`/`XNextEvent` → keysym mapping → `Input`. Held key stays down across pumps; edges clear | `WindowedRenderingTests.KeyboardEscapeIsDeliveredThroughRealEvents` (Xvfb) |
| Timing | Monotonic `DeltaTime`/`TotalTime`; clamping; FPS 30-frame window with first-frame exclusion; `FrameCount` | `PAL.Tests` TimeTests; sample output observed at ~62 fps |
| Text, CPU | Metrics/line-height identity; advance sums; kerning; measurement incl. newlines/tabs/surrogates; atlas packing, growth and occupancy; CPU-only fonts usable without an engine | `PAL.Tests` TextPipelineTests |
| Text, GPU | Composites over 3D **verified by pixel content**: bright pixels at the requested top-left region, upright glyphs in the top half, no accumulation across frames, drawn extent matches `MeasureText` (line-box contract: ink within the box, caps upper half, no-descender ink stops at baseline) | `HeadlessRenderingTests` (llvmpipe, PPM pixel analysis) |
| Rendering, GPU | Clear color verified by readback; lit glTF triangle renders (pixel count in range); `CaptureFrame` writes a valid P6 PPM | `HeadlessRenderingTests` (llvmpipe) |
| Windowed, GPU | Real 192×144 X11 window + real native swap chain: frames presented (paced per the backpressure contract), then `CaptureFrame` readback with pixel-content assertions | `WindowedRenderingTests.WindowedVulkanRendersAndCaptures` (Xvfb + llvmpipe) |
| Windowed, no GPU | Window opens/pumps/renders (Noop)/closes cleanly; event loop without graphics | `WindowedRenderingTests` |
| Readback contract | `BeginFrame` false ⇒ skip without `EndFrame`; one pending readback; retry frames wait for the original readback's pixels; PPM writer byte format | Integration tests + `PpmWriterTests` |
| Samples | All five run to clean exit in this environment: `MinimalApp` (no graphics initialized), `MinimalWindow` (Vulkan and Noop), `First3DScene` (60 presented frames), `TextRendering` (60 frames), `HeadlessRendering` (512×384 PPM with ~190k colored pixels from a lit scene) | executed, output inspected |

Specific bugs found and fixed during verification (all covered by
regression tests): the missing top-left→y-up conversion that placed
text mirrored around the frame's midline; the texture-V orientation
that mirrored glyphs; a race where `ReadPixels` could be issued twice
on retry frames; FPS sampling off-by-one that let a near-zero first
delta inflate the average; a null `VkInstanceCreateInfo` the loader
rejects before probing ICDs (backend safety probe).

## 2. Implemented, but not visually verified

These are fully implemented, exercised by automated assertions, and
run without error — but no human has looked at them on a real display
with a hardware GPU, so "looks right" is not claimed:

- **On-screen rendering on a real desktop**: windowed frames were
  presented under Xvfb with pixel readback verified, but visual
  quality/compositing under a real window manager/compositor is
  unverified.
- **Hardware Vulkan**: everything runs on llvmpipe. Device selection
  on multi-GPU machines, present modes on real drivers, and hardware
  performance are untested.
- **Text rendering appearance beyond pixel assertions**: placement and
  orientation are pixel-asserted; anti-aliasing quality and subpixel
  positioning are not visually reviewed (and subpixel positioning is
  known-absent: quads are snapped to integer atlas pixels).
- **Model loading beyond the test asset**: `LoadModel` (glTF 2.0 GLB
  via gltfio) is exercised with the repository's `triangle.glb` (lit
  ubershader, indexed triangles, transforms, one animation);
  skinning, morph targets, and exotic materials are implemented by
  the underlying binding but not asserted here.
- **Window behaviors**: resize handling (swap-chain rebuild outside
  frames) is implemented and the resize event path is unit tested
  through the fake platform window, but live interactive resizing
  under a window manager was not exercised.

## 3. Not implemented

Deliberate gaps, documented rather than hidden:

- **Platforms other than Linux/X11**: the platform layer
  (`IPlatformWindow`) exists so Windows/macOS/Wayland can be added;
  only `X11PlatformWindow` exists.
- **OpenGL rendering**: rejected at build time (also when no display
  is present, matching the binding's constraint); only Vulkan and
  Noop are available.
- **Complex text**: no shaping engine — no ligatures, joining, bidi
  reordering or RTL; kerning-only layout for simple scripts (Latin,
  Cyrillic, Greek, …). `.ttc` collections and CFF-outline OTF fonts
  are rejected by the loader with clear messages.
- **Multi-window**, **audio**, **clipboard**, **drag-and-drop**,
  **HiDPI scaling**, **gamepad input**: not present, not stubbed.
- **Runtime material compilation**: PAL ships one precompiled
  text-overlay material (embedded); user materials are consumed as
  precompiled `.filamat` bytes via the escape hatch.

## 4. Known behavior notes

- **Backpressure pacing** (`Render` returning false indefinitely in a
  flat-out loop on llvmpipe/2-core hosts): intended, documented
  behavior (the app paces; PAL never busy-waits). See
  [architecture.md](architecture.md#frame-pacing).
- **Threading**: single-threaded contract; calling into the engine
  from another thread panics inside Filament (observed; documented).
- **First-frame latency** under llvmpipe: the first frame that
  compiles shaders can take seconds — visible in the First3DScene
  sample's first progress report. This is backend shader compilation,
  not PAL.

## 5. How this was verified

```
scripts/run-tests.sh                              # 106/106 pass
PAL_WINDOWED_TESTS=1 DISPLAY=:99 scripts/run-tests.sh
dotnet run --project samples/<each>               # all five, clean exits
```

PPM outputs from the text tests were additionally inspected by an
ASCII-art rendering of the pixel data to confirm glyph orientation
("P A L" readable, upright, over the triangle) before the
pixel-assertion tests were trusted.
