using BenchmarkDotNet.Attributes;
using Volt.Core.Timing;

namespace Volt.Benchmarks;

/// <summary>
/// Compares the loop's own frame cost for empty, fixed-step, idle
/// scheduled, and due-scheduled work.
/// </summary>
/// <remarks>
/// <para>
/// User callbacks are deliberately empty. A benchmark measures only
/// the loop's bookkeeping, not the work a game puts inside a
/// callback; invoking user code is real CPU work even when the
/// scheduler allocates zero bytes.
/// </para>
/// <para>
/// The injected frame is slightly longer than 1/60 second, so the
/// fixed-step cases execute exactly one step per iteration and do not
/// depend on wall-clock scheduling accuracy.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class GameLoopBenchmarks
{
	private static readonly TimeSpan FrameDelta =
		TimeSpan.FromTicks(166_667);

	private readonly BenchmarkClock _clock = new();

	private GameLoop _empty = null!;
	private GameLoop _fixed = null!;
	private GameLoop _idleSchedule = null!;
	private GameLoop _dueSchedule = null!;

	[GlobalSetup]
	public void Setup()
	{
		_empty = CreateLoop();

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

		_dueSchedule = CreateLoop();

		for (int index = 0; index < 16; index++)
		{
			_dueSchedule.Schedule.EveryHz(
				60,
				static (in ScheduledUpdateContext _) => { });
		}
	}

	[Benchmark(Baseline = true)]
	public FrameTiming EmptyFrame()
	{
		return Advance(_empty);
	}

	[Benchmark]
	public FrameTiming FixedStepFrame()
	{
		return Advance(_fixed);
	}

	[Benchmark]
	public FrameTiming IdleScheduleWith64Tasks()
	{
		return Advance(_idleSchedule);
	}

	[Benchmark]
	public FrameTiming SixteenDueCallbacks()
	{
		return Advance(_dueSchedule);
	}

	private FrameTiming Advance(GameLoop loop)
	{
		_clock.Advance(FrameDelta);

		return loop.Advance();
	}

	private GameLoop CreateLoop()
	{
		return new GameLoop(GameLoopSettings.Default, _clock);
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
