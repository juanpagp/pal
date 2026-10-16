# Text rendering

PAL renders text as a first-class feature of `Graphics`: CPU-rasterized
glyphs, a shared atlas texture, batched screen-space quads and a
precompiled material — no runtime shader compilation, no per-character
draw calls.

## Public API

```csharp
Font font   = graphics.LoadFont("Inter-Regular.ttf");   // path, bytes or stream
var size    = graphics.MeasureText("Hello", font, 24);  // layout box, no drawing
graphics.DrawText("Hello", font, new Vector2(20, 20), 24, Color.White);
```

- `DrawText` **queues** text for the current frame. The next
  `Render`/`CaptureFrame` uploads the batch, renders the overlay and
  clears it. Text drawn during a frame appears in that frame and
  nothing accumulates across frames (verified by
  `TextDoesNotAccumulateAcrossFrames`).
- Multiple fonts per frame are supported; each font has its own atlas
  and material instance, all batched into one overlay scene.
- `\n` starts a new line, `\t` advances four space-widths, surrogate
  pairs are decoded to full code points, kerning is applied between
  adjacent code points.

## Coordinate system and the line-box contract

Draw positions are window pixels with the origin at the **top-left**,
x growing right and y growing down — the 2D UI convention, not the 3D
camera convention.

The position is the top-left corner of the first line's **line box**
(the CSS/text-layout convention):

```
 (x, y) ┌─────────────────────────────┐
        │  ascent (baseline − caps)   │  ← caps ink starts a few px down
        │ ── baseline: y + ascent ──  │
        │  descent                    │
        └─────────────────────────────┘  ← y + MeasureText().Y
```

- The baseline sits `ascent` pixels below `position.Y`.
- `MeasureText` returns exactly the box the text occupies from that
  corner: `X` is the advance width, `Y` is `lines * lineHeight`
  (ascent + descent + line gap), including the last line.
- Ink never escapes `[position, position + measured]`. For caps-only
  text the ink starts `ascent − capHeight` pixels below the box top
  (for Inter at 32 px: ascent 25.6, cap height 19.2, so ~6.4 px) and
  stops at the baseline; descenders reach into the descent area. This
  makes a fixed position stable regardless of *which* glyphs are drawn
  — "aaa" and "AAA" align to the same baseline — at the cost of the
  ink top not being the box top.

This contract is what `MeasureTextMatchesDrawnExtent` asserts against
the actual GPU output: ink within the measured box, cap ink in the
upper half, no-descender ink stopping at the baseline region.

## Pipeline

```
DrawText ──► GlyphCache (per font, per pixel size)
                 │ stb_truetype: metrics, advances, kerning, rasterization
                 ▼
             GlyphAtlas (shared per font, grows 256→512→1024 px)
                 │ RGBA8, glyphs packed in rows, CPU-side only
                 ▼
             TextRenderer per-frame batch
                 │ interleaved verts: pos(3) uv(2) color(4)
                 ▼
             one VertexBuffer + IndexBuffer + Renderable per font
                 │ quads: 0,1,2  2,1,3  (two triangles)
                 ▼
             overlay View (translucent) rendered after the 3D view
                 │ material: text_overlay.filamat (precompiled, embedded)
                 ▼
             blended over the 3D scene in the same swap chain
```

Details worth knowing:

- **Glyph rasterization is lazy and cached**: a glyph is rasterized the
  first time it is drawn or measured at a given pixel size, then kept.
  Repeated `DrawText` of the same string reuses the atlas, not new
  GPU resources.
- **Atlas growth**: the atlas starts at 256 px and doubles when a glyph
  does not fit, up to 2048 px. Growth recreates the texture and
  rebinds it to the font's material instance before the next frame;
  glyph rects are remapped on the CPU.
- **Colors** are authored in sRGB and converted to linear for the
  overlay pass (the renderer encodes back to sRGB on output), so
  `Color.White` text over a dark scene reads correctly without gamma
  tricks.
- **Capacity**: at most 16384 glyph quads per font per frame; beyond
  that `DrawText` throws instead of degrading silently.

<a id="atlas-orientation"></a>
## Atlas orientation

Two vertical flips meet in this pipeline, and getting them wrong
produces upside-down or vertically mirrored text — so both are pinned
here, with the reasoning:

1. **Quad positions.** Draw positions are y-down (top-left origin), but
   the overlay camera uses a y-up orthographic projection
   (`SetOrthographicProjection(0, w, 0, h, …)` — bottom-left origin).
   The conversion happens when quads are baked: the baseline is flipped
   around the viewport's bottom edge
   (`baselineUp = viewportHeight − baselineY`) and the glyph extends
   `−YOffset` (stb_truetype bearings are negative above the baseline)
   upward from it. `EmitQuad` in `TextRenderer.cs` is the single place
   this happens.

2. **Texture V coordinates.** Filament's texture upload maps the
   buffer's row 0 to `v = 1` (the top in UV space) — the opposite of
   the naive `v = 0` assumption. The atlas coordinates are therefore
   emitted flipped: a glyph's top atlas row uses `v = 1 − rect.Y /
   size`, its bottom row `v = 1 − (rect.Y + height) / size`. Verified
   empirically on the Vulkan backend (llvmpipe): with the flip,
   "P A L" rasterize upright and in order; without it, glyphs render
   vertically mirrored.

The combination was validated end-to-end by pixel-reading rendered
frames (`TextOverlayCompositesOver3DOnVulkan` checks placement *and*
upright orientation in the top half of the frame; the PPM dumps from
`CaptureFrameWritesValidPpm` show readable text).

## The material

`src/PAL/Assets/text_overlay.filamat` is a **precompiled Filament
material** (source in `assets/Materials/text_overlay.mat`) embedded as
a resource in the PAL assembly: an unlit, blended, double-sided
material sampling the atlas with a vertex color modulating the glyph's
alpha. No runtime shader compilation exists anywhere in PAL — the
material is loaded through `Engine.CreateMaterial(bytes)`, the same
path FilamentSharp's own samples use.

## CPU-only use

`Font` can exist without an engine (used by the unit tests): metrics,
measurement, kerning and rasterization all work; only the step that
creates the atlas *texture* requires the engine and throws a
`PALException` with a clear message if attempted CPU-only.
