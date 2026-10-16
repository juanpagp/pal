using Xunit;
using PAL.Text;

namespace PAL.Tests;

/// <summary>
/// Glyph rasterization, metrics, atlas packing and text layout — pure CPU
/// paths, no GPU or window required. Uses the repository's Inter-Regular
/// test asset.
/// </summary>
public class TextPipelineTests
{
    private static string FontPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "assets", "Fonts", "Inter-Regular.ttf");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar))!;
            }
            throw new FileNotFoundException(
                "assets/Fonts/Inter-Regular.ttf not found relative to the test binary.");
        }
    }

    private static Font LoadFont() => new(null, File.ReadAllBytes(FontPath));

    [Fact]
    public void InvalidFontDataFailsWithActionableError()
    {
        Assert.Throws<PALException>(() => new Font(null, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }));
        var garbage = new byte[1024];
        new Random(42).NextBytes(garbage);
        Assert.Throws<PALException>(() => new Font(null, garbage));
    }

    [Fact]
    public void MetricsAreSaneForARegularFont()
    {
        var font = LoadFont();
        var cache = font.GetCache(32);
        cache.LoadMetrics();

        // Ascent above baseline, descent below (negative), line height
        // covers both plus the gap.
        Assert.InRange(cache.Ascent, 10.0f, 60.0f);
        Assert.InRange(cache.Descent, -40.0f, -1.0f);
        Assert.Equal(cache.Ascent - cache.Descent + cache.LineGap, cache.LineHeight, 4);
        Assert.True(cache.LineHeight > 30.0f, "32px line height should exceed 30px");
    }

    [Fact]
    public void GlyphRasterizationProducesNonEmptyBitmaps()
    {
        var font = LoadFont();
        var cache = font.GetCache(24);
        cache.LoadMetrics();

        var glyph = cache.GetGlyph('A');
        Assert.Equal('A', glyph.Codepoint);
        Assert.True(glyph.Rect.Width > 0, "'A' should have ink");
        Assert.True(glyph.Rect.Height > 0, "'A' should have ink");
        Assert.InRange(glyph.Advance, 5.0f, 40.0f);

        // 'A' sits above the baseline: negative y offset.
        Assert.True(glyph.YOffset < 0, "'A' top should be above the baseline");
    }

    [Fact]
    public void SpaceHasAdvanceButNoInk()
    {
        var font = LoadFont();
        var cache = font.GetCache(24);
        cache.LoadMetrics();

        var space = cache.GetGlyph(' ');
        Assert.True(space.Advance > 0.0f);
        Assert.Equal(default, space.Rect); // no bitmap
    }

    [Fact]
    public void GlyphsAreCachedPerSizeAndCodepoint()
    {
        var font = LoadFont();
        var cache = font.GetCache(24);
        cache.LoadMetrics();

        var first = cache.GetGlyph('X');
        var second = cache.GetGlyph('X');
        Assert.Equal(first, second); // same record: cached, not re-rasterized

        var other = cache.GetGlyph('Y');
        Assert.NotEqual(first, other);

        var bigger = font.GetCache(48).GetGlyph('X');
        Assert.NotEqual(first.Rect, bigger.Rect); // separate cache per size
    }

    [Fact]
    public void KerningIsAppliedBetweenPairs()
    {
        var font = LoadFont();
        var cache = font.GetCache(24);
        cache.LoadMetrics();

        // "AV" kerns tighter than "AA" in most proportional fonts; even if
        // the font has no kerning for a pair, the call must return 0.
        float kern = cache.GetKerning('A', 'V');
        Assert.True(kern <= 0.0f || Math.Abs(kern) < 1e-6f,
            $"kerning should be zero or negative (tightening), got {kern}");
        Assert.Equal(0.0f, cache.GetKerning('A', 'A'));
    }

    [Fact]
    public void MeasureTextAdvancesByGlyphAdvances()
    {
        var font = LoadFont();
        var size = Text.TextRenderer.MeasureText("AAAA", font, 24);
        var single = Text.TextRenderer.MeasureText("A", font, 24);

        Assert.Equal(single.X * 4.0f, size.X, 2.0f); // monospace advance chain
        Assert.Equal(single.Y, size.Y, 4);           // same single line
        Assert.True(size.Y >= 24.0f, "height covers the full line");
    }

    [Fact]
    public void MeasureTextCountsLinesAndNewlines()
    {
        var font = LoadFont();
        var one = Text.TextRenderer.MeasureText("Hello", font, 20);
        var two = Text.TextRenderer.MeasureText("Hello\nWorld", font, 20);
        var three = Text.TextRenderer.MeasureText("A\n\nB", font, 20);

        Assert.Equal(one.Y, two.Y / 2.0f, 4);
        Assert.Equal(one.Y, three.Y / 3.0f, 4);
        Assert.InRange(two.X, 5.0f, 400.0f);
    }

    [Fact]
    public void MeasureTextHandlesTabsAndCarriageReturns()
    {
        var font = LoadFont();
        var withTab = Text.TextRenderer.MeasureText("A\tA", font, 20);
        var plain = Text.TextRenderer.MeasureText("AA", font, 20);
        Assert.True(withTab.X > plain.X, "tab advances more than a plain glyph");

        var crlf = Text.TextRenderer.MeasureText("A\r\nB", font, 20);
        var lf = Text.TextRenderer.MeasureText("A\nB", font, 20);
        Assert.Equal(lf, crlf);
    }

    [Fact]
    public void MeasureTextSupportsNonLatinAndAstralCodepoints()
    {
        var font = LoadFont();
        // Greek, Cyrillic and an emoji (astral plane — Inter has no emoji
        // glyph, which exercises the missing-glyph path).
        var greek = Text.TextRenderer.MeasureText("Ωμέγα", font, 24);
        Assert.True(greek.X > 0.0f);

        var mixed = Text.TextRenderer.MeasureText("A😀B", font, 24);
        Assert.True(mixed.X > 0.0f);
    }

    [Fact]
    public void EmptyAndNullInputsAreHandled()
    {
        var font = LoadFont();
        Assert.Throws<ArgumentNullException>(() =>
            Text.TextRenderer.MeasureText(null!, font, 24));
        var empty = Text.TextRenderer.MeasureText("", font, 24);
        Assert.Equal(0.0f, empty.X);
        Assert.True(empty.Y > 0.0f); // one (empty) line still has height
    }

    [Fact]
    public void FontDisposeIsIdempotentAndGuardsUse()
    {
        var font = LoadFont();
        font.Dispose();
        font.Dispose();

        Assert.True(font.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() =>
            Text.TextRenderer.MeasureText("x", font, 24));
    }

    [Fact]
    public void FontRejectsNullData()
    {
        Assert.Throws<ArgumentNullException>(() => new Font(null, null!));
    }
}

public class GlyphAtlasTests
{
    private static GlyphAtlas CreateAtlas() => new(null);

    private static byte[] Rgba(int w, int h, byte fill)
    {
        var data = new byte[w * h * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = fill;
            data[i + 1] = fill;
            data[i + 2] = fill;
            data[i + 3] = 255;
        }
        return data;
    }

    [Fact]
    public void ShelfPackingPlacesRectsWithoutOverlap()
    {
        var atlas = CreateAtlas();
        var placed = new List<AtlasRect>();

        for (int i = 0; i < 100; i++)
        {
            var rect = atlas.Place(10, 12, Rgba(10, 12, (byte)i));
            placed.Add(rect);
        }

        foreach (var a in placed)
        {
            Assert.InRange(a.X, 0, atlas.Size - a.Width);
            Assert.InRange(a.Y, 0, atlas.Size - a.Height);
            foreach (var b in placed)
            {
                if (a == b)
                {
                    continue;
                }
                bool disjoint = a.X + a.Width <= b.X || b.X + b.Width <= a.X ||
                                a.Y + a.Height <= b.Y || b.Y + b.Height <= a.Y;
                Assert.True(disjoint, $"overlapping placements {a} and {b}");
            }
        }
    }

    [Fact]
    public void AtlasGrowsWhenFull()
    {
        var atlas = CreateAtlas();
        int initial = atlas.Size;

        // Exhaust the initial atlas with tall glyphs.
        int count = 0;
        while (atlas.Size == initial && count < 100_000)
        {
            atlas.Place(30, 40, Rgba(30, 40, 1));
            count++;
        }

        Assert.True(atlas.Size > initial, "atlas should have grown");
        Assert.True(count < 100_000, "growth should happen before 100k glyphs");
    }

    [Fact]
    public void RepeatedPlaceOfSameSizeAdvancesCursor()
    {
        var atlas = CreateAtlas();
        var r1 = atlas.Place(10, 10, Rgba(10, 10, 1));
        var r2 = atlas.Place(10, 10, Rgba(10, 10, 2));
        Assert.NotEqual(r1, r2);
        Assert.True(r2.X > r1.X || r2.Y > r1.Y);
    }

    [Fact]
    public void PlacementsStayInsideTheAtlasAfterManyGrowthCycles()
    {
        var atlas = CreateAtlas();
        var random = new Random(7);

        for (int i = 0; i < 2000; i++)
        {
            int w = random.Next(4, 60);
            int h = random.Next(4, 60);
            var rect = atlas.Place(w, h, Rgba(w, h, (byte)i));
            Assert.InRange(rect.X, 0, atlas.Size - w);
            Assert.InRange(rect.Y, 0, atlas.Size - h);
        }
    }

    [Fact]
    public void AtlasRejectsInvalidArguments()
    {
        var atlas = CreateAtlas();
        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Place(0, 10, Rgba(1, 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Place(10, 0, Rgba(1, 1, 0)));
        Assert.Throws<ArgumentException>(() => atlas.Place(10, 10, new byte[10]));
    }

    [Fact]
    public void TextureWithoutEngineThrowsActionably()
    {
        var atlas = CreateAtlas();
        atlas.Place(10, 10, Rgba(10, 10, 1));
        var ex = Assert.Throws<PALException>(() => atlas.EnsureTexture());
        Assert.Contains("engine", ex.Message);
    }
}
