using Volt.Ecs;
using Volt.Gameplay.Events;

namespace Volt.Gameplay.Tests.Events;

/// <summary>
/// Covers subscriptions and publishes that mutate the routing table
/// while an event is being delivered.
/// </summary>
public sealed class GameplayEventReentrancyTests
{
	[Fact]
	public void Handler_CanUnsubscribeItself()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		GameplayEventSubscription subscription = default;
		int calls = 0;

		subscription = events.Subscribe(EventSchema.AttackHit, _ =>
		{
			calls++;
			Assert.True(events.Unsubscribe(subscription));
		});

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(0, events.SubscriptionCount);
		Assert.Equal(0, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, calls);
	}

	[Fact]
	public void Handler_CanUnsubscribeAnotherListenerOfTheSameEvent()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int victimCalls = 0;
		GameplayEventSubscription victim = GameplayEventSubscription.None;

		victim = events.Subscribe(
			EventSchema.AttackHit,
			_ => victimCalls++);

		// Subscribed last, so it runs first in its bucket.
		events.Subscribe(EventSchema.AttackHit, _ =>
		{
			Assert.True(events.Unsubscribe(victim));
		});

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(0, victimCalls);
	}

	[Fact]
	public void Handler_CanSubscribeAndTheNewListenerWaitsForTheNextEvent()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int calls = 0;

		events.Subscribe(EventSchema.AttackHit, _ =>
		{
			events.Subscribe(EventSchema.AttackHit, _ => calls++);
		});

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(0, calls);

		Assert.Equal(2, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, calls);
	}

	[Fact]
	public void NestedPublish_CompletesBeforeTheOuterDispatchContinues()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		List<string> order = [];

		events.Subscribe(EventSchema.DamageReceived, _ =>
		{
			order.Add("outer-start");
			events.Publish(new GameplayEvent(EventSchema.AttackHit));
			order.Add("outer-end");
		});

		events.Subscribe(EventSchema.AttackHit, _ => order.Add("inner"));

		// The return value counts this dispatch's listeners; the nested
		// inner event is a separate dispatch.
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.DamageReceived)));

		Assert.Equal(
			["outer-start", "inner", "outer-end"],
			order);
	}

	[Fact]
	public void RecursivePublish_IsBounded()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		GameplayEventSubscription subscription = default;

		subscription = events.Subscribe(EventSchema.AttackHit, _ =>
		{
			events.Publish(new GameplayEvent(EventSchema.AttackHit));
		});

		Assert.Throws<InvalidOperationException>(
			() => events.Publish(new GameplayEvent(EventSchema.AttackHit)));

		Assert.True(events.Unsubscribe(subscription));

		// The depth guard is released, so the system still publishes.
		Assert.Equal(0, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
	}

	[Fact]
	public void ThrowingHandler_PropagatesAndLeavesRoutingUsable()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		bool thrown = false;
		int calls = 0;

		void ThrowOnce(GameplayEvent _)
		{
			if (!thrown)
			{
				thrown = true;

				throw new InvalidOperationException("listener failed");
			}

			calls++;
		}

		events.Subscribe(EventSchema.AttackHit, ThrowOnce);

		Assert.Throws<InvalidOperationException>(
			() => events.Publish(new GameplayEvent(EventSchema.AttackHit)));

		Assert.Equal(1, events.SubscriptionCount);
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, calls);
	}

	[Fact]
	public void Handler_CanDestroyAnEntityScopedToALaterSubscription()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity owner = world.CreateEntity();
		int scopedCalls = 0;

		events.Subscribe(
			EventSchema.AttackHit,
			_ => scopedCalls++,
			owner: owner);

		events.Subscribe(EventSchema.AttackHit, _ =>
		{
			world.DestroyEntity(owner);
		});

		// The unscoped handler was subscribed last, so it runs first
		// and destroys the owner of the scoped subscription.
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(0, scopedCalls);

		// Only the unscoped listener remains.
		Assert.Equal(1, events.SubscriptionCount);
	}

	[Fact]
	public void Handler_CanDestroyAnUnrelatedEntityWithoutAffectingOthers()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity target = world.CreateEntity();
		int calls = 0;

		events.Subscribe(EventSchema.AttackHit, _ => calls++);
		events.Subscribe(EventSchema.AttackHit, _ =>
		{
			world.DestroyEntity(target);
		});

		Assert.Equal(2, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, calls);
		Assert.False(world.IsAlive(target));
	}
}
