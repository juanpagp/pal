# Getting started

This guide takes you from a fresh checkout to a running PAL application on
Linux. Everything here is verified against the exact repository layout PAL
expects: PAL and FilamentSharp checked out side by side.

## 1. Prerequisites

- **.NET 10 SDK.** FilamentSharp's `global.json` pins the SDK version;
  `dotnet --version` must print 10.x.
- **X11 development runtime** for windows: `libX11` (usually present; under
  Debian/Ubuntu `libx11-6` is enough at runtime, `libx11-dev` if you build
  native code against it).
- **Vulkan**: a hardware driver, or the Mesa software rasterizer
  (`mesa-vulkan-drivers`, which provides llvmpipe/lavapipe). PAL's test
  suite runs on llvmpipe — a GPU is not required.
- **FilamentSharp** — the bindings repository, checked out as a sibling:

```
~/code/FilamentSharp/     https://github.com/juanpagp/FilamentSharp
~/code/pal/               this repository
```

PAL's `nuget.config` clears the default feeds and adds
`../FilamentSharp/artifacts/package/release` as a local source, plus
nuget.org. The sibling layout is therefore part of the contract (you can
override the location by editing `nuget.config`).

## 2. Build FilamentSharp's native bridge and pack the NuGet

Follow the FilamentSharp repository's own instructions; in short:

```
cd ../FilamentSharp
scripts/setup-headless-vulkan.sh    # downloads the lavapipe ICD for headless CI
scripts/build-native.sh             # builds libFilamentSharp.Native.so
scripts/pack.sh                     # packs FilamentSharp.1.0.0.nupkg locally
```

After this, `../FilamentSharp/external/native-install/lib/` contains the
native bridge and the local feed exists. PAL's `scripts/env.sh` resolves
both automatically.

## 3. Build PAL

```
cd ~/code/pal
dotnet build PAL.sln
```

This compiles the library, the five samples and both test suites. If the
build cannot restore FilamentSharp, the sibling checkout or the local feed
is missing (step 2).

## 4. Run the samples

From the repository root:

```
# no window, no graphics — pure timing loop
dotnet run --project samples/MinimalApp

# a real window; works under a virtual display too
dotnet run --project samples/MinimalWindow -- --backend vk --frames 120

# full 3D scene
dotnet run --project samples/First3DScene -- --frames 300

# text over 3D
dotnet run --project samples/TextRendering -- --frames 300

# offscreen render to a PPM image
dotnet run --project samples/HeadlessRendering
```

On a machine without a display (CI, container), start a virtual one first:

```
Xvfb :99 -screen 0 1024x768x24 &
DISPLAY=:99 dotnet run --project samples/First3DScene -- --frames 300
```

The samples all accept `--frames N` to exit after N presented frames and
`--backend vk|noop`; `--help` prints usage.

## 5. The shape of a PAL application

A PAL program is an ordinary `Main` with a `using` block. There is no
inversion of control: PAL never calls your code, you call PAL.

```csharp
using var app = App.CreateBuilder()
    .WithWindow("Title", 1280, 720)      // or: nothing, or WithHeadlessGraphics
    .WithGraphics(Backend.Vulkan)
    .WithMaxDeltaTime(0.25f)             // clamp frame deltas (default 1/15 s)
    .Build();

while (app.Window!.IsOpen)               // you own the loop
{
    app.PumpEvents();                    // drains X11, updates Input and Time

    float dt = app.Time.DeltaTime;       // seconds since the previous pump
    // ...your update...

    bool presented = app.Graphics!.Render(scene, camera);
    if (!presented)
    {
        // Backpressure: the swap chain is busy. Skipping is correct;
        // PAL does not sleep or busy-wait for you — you decide pacing.
    }
}                                        // Dispose: deterministic teardown
```

Three modes come out of the builder:

| Mode | Builder calls | What exists |
|------|---------------|-------------|
| `AppMode.Core` | (none) | `Time` only. Filament is never initialized. |
| `AppMode.Windowed` | `WithWindow` (+ optional `WithGraphics`) | Window, input, timing; renderer when graphics are requested. |
| `AppMode.HeadlessRendering` | `WithHeadlessGraphics(w, h, backend)` | Offscreen renderer and readback; no window (a display is not required). |

## 6. Run the tests

```
scripts/run-tests.sh                       # unit + integration (GPU auto-gated)
PAL_WINDOWED_TESTS=1 DISPLAY=:99 scripts/run-tests.sh
                                           # + real-window tests under Xvfb
```

See [testing.md](testing.md) for the full gating rules and what each suite
covers.

## Troubleshooting

- **`Asset not found: assets/...`** — run samples from the repository root
  (the sample asset walker starts at the binary directory and walks up).
- **`libFilamentSharp.Native.so` not found** — `source scripts/env.sh`
  (or run through `scripts/run-tests.sh`), which points `LD_LIBRARY_PATH`
  at the sibling build output.
- **Engine creation aborts / no Vulkan ICD** — at the pinned Filament
  release, creating a `Vulkan` engine without a usable ICD terminates the
  process. PAL probes the ICD first (`PAL.Internal.VulkanAvailability`) and
  the integration tests skip themselves with a printed reason. For
  headless machines: `scripts/setup-headless-vulkan.sh` in FilamentSharp,
  then source `scripts/env.sh` to set `VK_ICD_FILENAMES`.
- **Windowed rendering presents ~2 frames then reports backpressure in a
  tight loop** — expected on software drivers: presents retire on backend
  threads that a flat-out spin loop starves. Pace your loop (a 1 ms sleep
  is plenty); see [architecture.md](architecture.md#frame-pacing).
