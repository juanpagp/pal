# Architecture

How PAL is put together, and the rules it follows.

## Design rules

1. **Framework, not engine.** The application owns the main loop, all
   state and all pacing decisions. PAL provides calls, never callbacks
   (the only inversion is `PumpEvents` draining the platform's event
   queue into PAL's input state, which the application calls
   explicitly).
2. **No scheduler, no game loop, no component system, no entity
   hierarchy.** Scene objects are plain managed objects over Filament
   entities; there is no hidden update order.
3. **Parts usable independently.** `AppMode.Core` never initializes
   Filament — `Time` works in a console application. A window without
   graphics runs a pure event loop. The text pipeline's CPU side
   (metrics, measurement, rasterization into the atlas) works without a
   GPU and is unit tested that way.
4. **Honest failure.** Partial initialization is rolled back
   deterministically (see the builder below); backpressure is reported,
   not hidden behind sleeps; environmental skips in tests print why.
5. **Escape hatch, not cage.** `Graphics.Engine`, `Scene.Native`,
   `Camera.Native`, `Model.Native` expose the underlying FilamentSharp
   objects for anything PAL does not cover. PAL adds convenience on top
   of FilamentSharp; it does not pretend to hide it.

## Layering

```
application
    │  public API
    ▼
PAL.App / AppBuilder ── Window / Input / Time
    │                      │
    │                      ▼
    │                 Platform (X11PlatformWindow + Xlib interop)
    │
    ▼
PAL.Graphics ── Scene / Camera / Model / Light / Text(Font, GlyphCache,
    │           GlyphAtlas, TextRenderer) / FramePixels
    ▼
FilamentSharp (Engine, Renderer, View, SwapChain, VertexBuffer, ...)
    ▼
libFilamentSharp.Native.so ── Filament ── Vulkan / Noop backend
```

- `Platform/` is the only code that talks to Xlib. It reports events
  through `IPlatformEventSink` (implemented by `App`), which keeps the
  window layer unit-testable with a fake platform window.
- `Text/` is pure CPU up to the last step: glyph rasterization and atlas
  placement are platform- and GPU-free; only the atlas texture upload and
  quad rendering touch Filament.
- `Internal/VulkanAvailability.cs` probes the Vulkan loader and creates a
  minimal real instance to decide whether a GPU backend is usable at all
  (see "Backend safety" below).

## The frame contract

`Graphics.Render(scene, camera)` maps to exactly one Filament frame:

```
BeginFrame(swapChain)
    ├── Render(view)               the 3D view
    ├── RenderOverlay(renderer)    the text view, translucent
    ├── (optional) ReadPixels      readback of this frame
EndFrame
```

- **`Render` returns `false` when the frame was skipped** — the swap
  chain reported backpressure, or the window is not presentable yet
  (unexposed). The correct response is to skip the frame and try again
  later. PAL **never** sleeps, spins or busy-waits on its own: pacing is
  the application's decision.
- Text queued by `DrawText` since the last *presented* frame stays queued
  across a skipped frame and is consumed by the next presented one;
  nothing accumulates beyond that.
- The clear color is applied once before the first frame (Filament's
  clear options are persistent), not per frame.
- Resize: the platform window reports the new size, `Graphics` destroys
  and recreates the native swap chain on the same window handle —
  strictly outside any begin/end frame pair.

<a id="frame-pacing"></a>
### Frame pacing and backpressure on software drivers

Under llvmpipe (and generally multi-threaded software drivers), present
retirement happens on the driver's helper threads. On a starved CPU
(e.g. a 2-core host running a flat-out loop), `BeginFrame` keeps
returning `false` indefinitely: the loop never yields, the driver
threads never run, presents never retire. This is not a PAL bug — it is
why backpressure is surfaced rather than hidden. Real applications pace
(`Thread.Sleep(1)`, vsync, or work between frames); the samples do, and
`CaptureFrame` yields 1 ms between its retry frames for the same reason
(bounded by its `maxFrames` argument, default 8).

## App lifecycle and rollback

`AppBuilder.Build()` performs a strictly ordered bring-up:

1. Validate the requested combination (e.g. headless graphics with a
   window, zero-size headless targets, or a second `Build` call, throw
   `PALException` before anything is created).
2. Create the platform window (if any).
3. Create the engine, renderer and view (if graphics were requested),
   after the backend-safety probe.
4. Create `Input` and `Time`.

If any step fails, everything already created in that `Build` call is
disposed before the exception propagates — a failed build leaves no
orphaned native resources. `App.Dispose` is idempotent and disposes in
reverse order: text batches and user resources first (via
`Graphics.Dispose`), then view/renderer/engine, swap chain **before**
window, window last.

## Ownership and disposal

- Managed wrappers (`Scene`, `Camera`, `Model`, `Light`, `Font`) own
  their native counterparts and dispose them deterministically. Users
  dispose what they create; PAL disposes what it created.
- Disposal order matters and PAL enforces the safe order internally:
  renderables and materials are removed/disposed before the engine,
  the swap chain is destroyed before the window it presents to.
- `Dispose` never throws on double calls; finalizers are not relied on
  for correctness (leaked wrappers are reclaimed only by engine
  teardown, which is the documented safety net — but the contract is
  deterministic disposal).

## Threading

PAL is single-threaded by contract: **create the `App` and call every
PAL API from the same thread**. Filament adopts the engine-creating
thread internally; calling into the engine (including readback
completion, which flushes and waits) from another thread panics with
`Precondition ... This thread has not been adopted` — observed and
reproduced, which is why this rule is documented rather than guessed.
Pumping, rendering, drawing text and disposing from one thread is
thread-safe by construction against PAL's own state (no shared mutable
state beyond what Filament itself manages).

## Coordinates and conventions

- World space: right-handed, `System.Numerics` matrices
  (row-vector convention: `v * M`). `Camera.LookAt` and
  `Model.Transform` follow it; verified against the FilamentSharp
  native bridge in the integration tests.
- Text: window pixels, origin top-left, y down; see
  [text-rendering.md](text-rendering.md) for the line-box contract.
- Colors: authored in sRGB, converted to linear where a pass requires
  it (`Color.ToLinear`), including text vertices and the clear color.

## Backend safety

At the pinned Filament release, `Engine.Create(Backend.Vulkan)` with no
usable ICD **aborts the process** (Filament panics before returning).
PAL therefore probes first: `VulkanAvailability.IsUsable` loads
`libvulkan`, creates a minimal `VkInstance` and enumerates physical
devices; if any step fails the reason is reported and PAL does not
attempt engine creation (tests self-skip; applications can check via
the probe's public surface and fall back to `Backend.Noop` — the
integration tests demonstrate the pattern).

`Backend.Noop` exercises the entire object model, frame loop and
readback plumbing without a GPU — used by the always-run integration
tests and by `samples/MinimalWindow --backend noop`.
