using System.Diagnostics;
namespace PAL;

/// <summary>
/// Frame timing: delta time, total elapsed time and a smoothed FPS
/// measurement, updated by <see cref="App.PumpEvents"/>.
/// </summary>
/// <remarks>
/// <para>
/// PAL never paces, limits or schedules frames. <see cref="DeltaTime"/> is
/// the wall-clock time between two consecutive <see cref="App.PumpEvents"/>
/// calls, measured with a monotonic clock and clamped to the builder's
/// maximum delta (default 0.25 s) so that a long pause (window drag, system
/// suspend, debugger breakpoint) does not produce an unbounded simulation
/// step. <see cref="TotalTime"/> is unclamped.
/// </para>
/// <para>
/// The first pump after application construction reports the time elapsed
/// since construction (clamped), so a frame loop that pumps before its first
/// update sees a small, finite delta rather than zero or garbage.
/// </para>
/// <para>
/// <see cref="Fps"/> is a rolling average over the last
/// <see cref="FpsSampleCount"/> frames; it reports 0 until two frames have
/// been sampled.
/// </para>
/// </remarks>
public sealed class Time
{
    /// <summary>Number of frames averaged for <see cref="Fps"/>.</summary>
    public const int FpsSampleCount = 30;

    private readonly Func<double> _timestampSeconds;
    private readonly double _baseTimestamp;
    private readonly float _maxDelta;
    private readonly float[] _recentDeltas = new float[FpsSampleCount];
    private double _lastTimestamp;

    internal Time(Func<double> timestampSeconds, float maxDelta)
    {
        _timestampSeconds = timestampSeconds;
        _maxDelta = maxDelta;
        _baseTimestamp = timestampSeconds();
        _lastTimestamp = _baseTimestamp;
    }

    /// <summary>Creates a time source driven by the system monotonic clock.</summary>
    internal static Time CreateDefault(float maxDelta) =>
        new(ReadMonotonicSeconds, maxDelta);

    private static double ReadMonotonicSeconds() =>
        Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>
    /// Seconds elapsed since the previous pump, clamped to the configured
    /// maximum (see <c>AppBuilder.WithMaxDeltaTime</c>).
    /// </summary>
    public float DeltaTime { get; private set; }

    /// <summary>Unclamped seconds elapsed since application construction.</summary>
    public double TotalTime => _timestampSeconds() - _baseTimestamp;

    /// <summary>
    /// Smoothed frames per second over the last <see cref="FpsSampleCount"/>
    /// pumps; 0 until at least two frames have been sampled.
    /// </summary>
    public float Fps { get; private set; }

    /// <summary>Number of pumps since construction.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Called once per <see cref="App.PumpEvents"/>.</summary>
    internal void Tick()
    {
        double now = _timestampSeconds();
        double raw = now - _lastTimestamp;
        _lastTimestamp = now;

        float delta = (float)raw;
        if (delta < 0.0f)
        {
            // The injected clock went backwards; treat as no time passing.
            delta = 0.0f;
        }
        else if (delta > _maxDelta)
        {
            delta = _maxDelta;
        }

        DeltaTime = delta;
        FrameCount++;

        // The FPS window measures intervals *between* pumps; the first
        // frame's delta (construction -> first pump) is excluded so a
        // near-zero construction delta cannot inflate the average.
        if (FrameCount >= 2)
        {
            _recentDeltas[(FrameCount - 2) % FpsSampleCount] = delta;
        }

        if (FrameCount >= 3)
        {
            int count = Math.Min(FrameCount - 1, FpsSampleCount);
            float sum = 0.0f;
            for (int i = 0; i < count; i++)
            {
                sum += _recentDeltas[i];
            }
            if (sum > 0.0f)
            {
                Fps = count / sum;
            }
        }
    }
}
