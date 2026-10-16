using Xunit;
namespace PAL.Tests;

public class TimeTests
{
    private static (Time Time, Action Advance) Create(double start, float maxDelta = 0.25f)
    {
        double now = start;
        var time = new Time(() => now, maxDelta);
        return (time, () => now += 1.0 / 60.0);
    }

    [Fact]
    public void FirstPumpReportsTimeSinceCreationClamped()
    {
        double now = 0.0;
        var time = new Time(() => now, 0.25f);
        now = 0.016; // 16ms after construction
        time.Tick();

        Assert.Equal(0.016f, time.DeltaTime, 5);
        Assert.Equal(1, time.FrameCount);
    }

    [Fact]
    public void DeltaTimeMeasuresBetweenPumps()
    {
        double now = 0.0;
        var time = new Time(() => now, 10.0f);
        time.Tick(); // first pump

        now += 0.5;
        time.Tick();
        Assert.Equal(0.5f, time.DeltaTime, 5);

        now += 0.25;
        time.Tick();
        Assert.Equal(0.25f, time.DeltaTime, 5);
    }

    [Fact]
    public void LongPausesAreClampedToMaxDelta()
    {
        double now = 0.0;
        var time = new Time(() => now, 0.25f);
        time.Tick();

        now += 60.0; // a full minute (suspend / debugger)
        time.Tick();
        Assert.Equal(0.25f, time.DeltaTime, 5);

        // Total time is unclamped.
        Assert.Equal(60.0, time.TotalTime, 5);
    }

    [Fact]
    public void ClockRunningBackwardsIsTreatedAsZero()
    {
        double now = 10.0;
        var time = new Time(() => now, 1.0f);
        time.Tick();
        now = 5.0;
        time.Tick();
        Assert.Equal(0.0f, time.DeltaTime, 5);
    }

    [Fact]
    public void FpsStartsAtZeroAndConverges()
    {
        double now = 0.0;
        var time = new Time(() => now, 100.0f);
        Assert.Equal(0.0f, time.Fps);

        time.Tick(); // first frame: no fps yet
        Assert.Equal(0.0f, time.Fps);

        now += 1.0 / 60.0; // second frame: one interval, still warming up
        time.Tick();

        for (int i = 0; i < 5; i++)
        {
            now += 1.0 / 60.0; // 60 fps cadence
            time.Tick();
        }

        // The fps window counts intervals between pumps: 5 exact 1/60s
        // intervals report ~60 fps.
        Assert.InRange(time.Fps, 58.0f, 62.0f);

        for (int i = 0; i < Time.FpsSampleCount * 2; i++)
        {
            now += 1.0 / 100.0; // 100 fps cadence
            time.Tick();
        }
        Assert.InRange(time.Fps, 95.0f, 105.0f);
    }

    [Fact]
    public void MaxDeltaIsValidatedAtBuilder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithMaxDeltaTime(0.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithMaxDeltaTime(-1.0f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            App.CreateBuilder().WithMaxDeltaTime(float.NaN));
    }
}
