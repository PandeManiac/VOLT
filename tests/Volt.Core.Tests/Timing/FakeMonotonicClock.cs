using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// A monotonic clock whose timestamp only changes when a test moves
/// it.
/// </summary>
/// <remarks>
/// One tick is one nanosecond by default, so
/// <see cref="AdvanceNanoseconds"/> and <see cref="TimeSpan"/> deltas
/// are exact. Nothing here reads the wall clock, sleeps, or depends on
/// scheduler accuracy.
/// </remarks>
internal sealed class FakeMonotonicClock : IMonotonicClock
{
	public const long NanosecondsPerSecond = 1_000_000_000;

	public FakeMonotonicClock(long ticksPerSecond = NanosecondsPerSecond)
	{
		if (ticksPerSecond <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ticksPerSecond));
		}

		TicksPerSecond = ticksPerSecond;
	}

	public long Timestamp { get; private set; }

	public long TicksPerSecond { get; }

	public void AdvanceNanoseconds(long nanoseconds)
	{
		Timestamp += nanoseconds;
	}

	public void Advance(TimeSpan delta)
	{
		Timestamp += delta.Ticks *
			(TicksPerSecond / TimeSpan.TicksPerSecond);
	}

	public void AdvanceTicks(long ticks)
	{
		Timestamp += ticks;
	}

	public void SetTimestamp(long timestamp)
	{
		Timestamp = timestamp;
	}
}
