# Testing

PAL ships two test suites with different environmental needs, plus
explicit gates for the parts that require real windows or a real GPU.
The philosophy: **every claim the documentation makes about behavior is
asserted somewhere, or the docs say it is not verified.**

## Running

```
# Unit + integration (GPU tests auto-gate on the Vulkan probe):
scripts/run-tests.sh

# Everything, including real-window tests (Xvfb works):
Xvfb :99 -screen 0 1024x768x24 &
PAL_WINDOWED_TESTS=1 DISPLAY=:99 scripts/run-tests.sh
```

`scripts/env.sh` (sourced by the runner) resolves the native library
location, and points `VK_ICD_FILENAMES` at the lavapipe ICD staged by
FilamentSharp's `scripts/setup-headless-vulkan.sh`. Without a usable
Vulkan ICD, the Vulkan-dependent tests print an
`ENVIRONMENTAL SKIP: <reason>` line and pass through — nothing fails
mysteriously, nothing claims coverage it did not get.

## `tests/PAL.Tests` — unit tests (no GPU, no window, no display)

91 tests over the pure-logic parts of PAL:

- **App/builder semantics**: the three modes, configuration validation
  (headless+window rejected, dimensions validated, unsupported
  backends rejected *before* anything is created, OpenGL without a
  display rejected), deterministic double-dispose safety,
  use-after-dispose throws, core mode initializes no graphics.
- **Input state machine** (via the fake platform window feeding real
  `App` events): press/hold/release edges, auto-repeat suppression,
  release-without-press, pump-with-no-events keeps down state but
  clears edges, focus loss clears keys without release edges, mouse
  edges/position/wheel, keysym→Key mapping.
- **Time**: monotonic deltas, clamping via `WithMaxDeltaTime`, FPS
  window sampling (including the first-frame exclusion), frame count.
- **Text pipeline (CPU-only fonts)**: metrics ranges and line-height
  identity, advance sums, kerning, measurement matches layout (newlines,
  tabs, surrogate pairs), atlas packing/growth/occupancy, PPM writer
  byte format.
- **Color**: presets, byte packing, sRGB→linear transfer function.

## `tests/PAL.IntegrationTests` — real backend, real windows

### Always running (Noop backend, no GPU)

Object-model plumbing: scene/camera creation and defaults, view and
viewport wiring, the escape hatch (`Graphics.Engine`,
`Scene.Native`), frame loop behavior under Noop, and the
capture-readback machinery (valid-sized zero buffer, PPM validity).

### Gated on a usable Vulkan ICD (the probe decides)

Pixel-verified rendering under llvmpipe:

- clear color verified by reading pixels back,
- a lit glTF triangle renders (content, not just no-error),
- text composites over 3D at the requested top-left position with
  upright glyphs, does not accumulate across frames, and its drawn
  extent matches `MeasureText` (the line-box contract),
- `CaptureFrame` writes a valid PPM.

### Gated on `PAL_WINDOWED_TESTS=1` plus a reachable `DISPLAY`

Real X11 windows under Xvfb (verified in this repository's environment):

- a window opens, pumps real events, renders frames (Noop) and closes
  cleanly;
- **real key delivery**: a second X connection injects genuine
  KeyPress/KeyRelease events into the app's window via `XSendEvent`
  (round-tripped with `XSync`), and the test asserts the full
  pipeline — server queue → `XPending`/`XNextEvent` → keysym mapping
  → `Input` edges, held-key-down-across-pumps semantics, and edge
  clearing;
- **windowed Vulkan end-to-end**: a real 192×144 window with a real
  native swap chain, frames presented (paced, per the backpressure
  contract), then `CaptureFrame` readback with pixel-content
  assertions.

## Why the gates exist

- Filament at the pinned release **aborts the process** when a Vulkan
  engine is created with no usable ICD — so tests must never "try and
  see"; `PAL.Internal.VulkanAvailability` probes (loader, real
  `VkInstance`, physical-device enumeration) before anything launches,
  and `PAL_TEST_FORCE_NO_VULKAN=1` forces the skip path for testing
  the gates themselves.
- Real-window tests need a display; headless CI provides Xvfb.
  Skipping is always loud: the reason is printed in the test output.

## Coverage honesty

The suites assert behavior, including **pixel content** of rendered
frames (not merely "no exception thrown") for the paths above. What is
*not* covered anywhere — hardware-GPU output, real window-manager
interaction quirks, multi-window use — is listed in
[implementation-report.md](implementation-report.md).
