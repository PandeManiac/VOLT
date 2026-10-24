using Volt.Ecs;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Cues;

public sealed class GameplayCueAllocationTests
{
	[Fact]
	public void ExecuteWithoutListeners_AllocatesNothingAfterWarmup()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		for (int index = 0; index < 16; index++)
		{
			cues.Execute(TagSchema.CueBurning);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < 1000; index++)
		{
			cues.Execute(TagSchema.CueBurning);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0, allocated);
	}

	[Fact]
	public void BufferedExecuteAndDrain_AllocateNothingAfterCapacityWarmup()
	{
		using World world = new();
		using GameplayCueSystem cues = new(world);
		using GameplayCueBuffer buffer = new(1024, 1024);
		cues.PresentationBuffer = buffer;
		Span<GameplayCueEvent> events = stackalloc GameplayCueEvent[1024];
		for (int index = 0; index < 1024; index++)
		{
			cues.Execute(TagSchema.CueBurning);
		}

		buffer.Drain(events);
		long before = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < 1024; index++)
		{
			cues.Execute(TagSchema.CueBurning);
		}

		buffer.Drain(events);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.Equal(0, allocated);
	}
}
