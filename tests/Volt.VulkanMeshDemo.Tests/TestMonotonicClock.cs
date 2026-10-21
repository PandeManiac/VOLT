using Volt.Core.Timing;

namespace Volt.VulkanMeshDemo.Tests;

/// <summary>
/// A nanosecond monotonic clock that only moves when a test moves it.
/// </summary>
internal sealed class TestMonotonicClock : IMonotonicClock
{
	public long Timestamp { get; private set; }

	public long TicksPerSecond => 1_000_000_000;

	public void AdvanceNanoseconds(long nanoseconds)
	{
		Timestamp += nanoseconds;
	}
}
