using BenchmarkDotNet.Attributes;
using Volt.Core.Timing;

namespace Volt.Benchmarks;

/// <summary>
/// Measures the loop's own frame cost while simulation time is
/// scaled.
/// </summary>
/// <remarks>
/// <para>
/// The injected frame is slightly longer than 1/60 second, so at
/// <c>1.0x</c> every iteration executes exactly one fixed step. At
/// <c>0.5x</c> half of the iterations execute one step and half
/// execute none, which is the real cadence of a half-speed 60 Hz
/// simulation. At <c>2.0x</c> every iteration executes two steps. The
/// callbacks are empty, so the numbers describe scaling arithmetic and
/// step bookkeeping only.
/// </para>
/// <para>
/// Every loop is created in <see cref="GlobalSetup"/>, so dictionary
/// growth and first-touch costs are outside the measured methods.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TimeScaleBenchmarks
{
	private static readonly TimeSpan FrameDelta =
		TimeSpan.FromTicks(166_667);

	private readonly BenchmarkClock _clock = new();

	private GameLoop _fixed = null!;
	private GameLoop _idleSchedule = null!;

	/// <summary>
	/// Gets or sets the simulation time scale under test.
	/// </summary>
	[Params(1.0f, 0.5f, 2.0f)]
	public float Scale { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_fixed = CreateLoop();
		_fixed.RegisterFixedUpdate(
			static (in FixedUpdateContext _) => { });

		_idleSchedule = CreateLoop();

		for (int index = 0; index < 64; index++)
		{
			_idleSchedule.Schedule.Every(
				TimeSpan.FromHours(1),
				static (in ScheduledUpdateContext _) => { });
		}
	}

	/// <summary>
	/// Advances one frame with a registered fixed update.
	/// </summary>
	[Benchmark(Baseline = true)]
	public FrameTiming FixedStepFrame()
	{
		return Advance(_fixed);
	}

	/// <summary>
	/// Advances one frame while 64 simulation-scheduled tasks are
	/// polled and none is due.
	/// </summary>
	[Benchmark]
	public FrameTiming IdleScheduleWith64Tasks()
	{
		return Advance(_idleSchedule);
	}

	private FrameTiming Advance(GameLoop loop)
	{
		_clock.Advance(FrameDelta);

		return loop.Advance();
	}

	private GameLoop CreateLoop()
	{
		GameLoop loop = new(GameLoopSettings.Default, _clock);
		loop.TimeScale = Scale;

		return loop;
	}

	private sealed class BenchmarkClock : IMonotonicClock
	{
		public long Timestamp { get; private set; } = 1;

		public long TicksPerSecond => 1_000_000_000;

		public void Advance(TimeSpan delta)
		{
			Timestamp += delta.Ticks * 100;
		}
	}
}
