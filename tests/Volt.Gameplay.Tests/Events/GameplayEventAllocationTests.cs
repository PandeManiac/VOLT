using Volt.Ecs;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Events;

/// <summary>
/// Guards the zero-allocation claim for warmed-up event dispatch.
/// </summary>
/// <remarks>
/// The guards measure managed allocations on the calling thread, so
/// they assert the implementation does not allocate, not that the
/// benchmark machine was quiet.
/// </remarks>
public sealed class GameplayEventAllocationTests
{
	[Fact]
	public void PublishWithoutSubscribers_AfterWarmup_AllocatesNothing()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		GameplayEvent gameplayEvent =
			new(EventSchema.AttackHit);

		for (int index = 0; index < 128; index++)
		{
			events.Publish(gameplayEvent);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			events.Publish(gameplayEvent);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void PublishWithOneListener_AfterWarmup_AllocatesNothing()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int calls = 0;

		events.Subscribe(EventSchema.AttackHit, _ => calls++);

		GameplayEvent gameplayEvent =
			new(EventSchema.AttackHit);

		for (int index = 0; index < 128; index++)
		{
			events.Publish(gameplayEvent);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			events.Publish(gameplayEvent);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(1_128, calls);
	}

	[Fact]
	public void HierarchicalAndWildcardDispatch_AfterWarmup_AllocatesNothing()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int calls = 0;

		events.Subscribe(
			EventSchema.Damage,
			_ => calls++,
			GameplayEventMatch.Hierarchical);

		events.Subscribe(GameplayTag.None, _ => calls++);

		GameplayEvent gameplayEvent =
			new(EventSchema.DamageReceived);

		for (int index = 0; index < 128; index++)
		{
			events.Publish(gameplayEvent);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			events.Publish(gameplayEvent);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(2_256, calls);
	}

	[Fact]
	public void NestedPublish_AfterWarmup_AllocatesNothing()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int innerCalls = 0;

		events.Subscribe(EventSchema.AttackHit, _ => innerCalls++);
		events.Subscribe(EventSchema.DamageReceived, _ =>
		{
			events.Publish(new GameplayEvent(EventSchema.AttackHit));
		});

		GameplayEvent gameplayEvent =
			new(EventSchema.DamageReceived);

		for (int index = 0; index < 128; index++)
		{
			events.Publish(gameplayEvent);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			events.Publish(gameplayEvent);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(1_128, innerCalls);
	}
}
