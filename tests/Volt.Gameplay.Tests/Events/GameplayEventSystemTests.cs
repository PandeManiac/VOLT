using Volt.Ecs;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Events;

/// <summary>
/// Covers event routing, exact and hierarchical matching, scoped
/// subscription lifetime, and generational handles.
/// </summary>
public sealed class GameplayEventSystemTests
{
	[Fact]
	public void ExactSubscription_MatchesOnlyItsOwnTag()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int exact = 0;
		int ancestor = 0;

		events.Subscribe(EventSchema.DamageReceived, _ => exact++);
		events.Subscribe(
			EventSchema.Damage,
			_ => ancestor++,
			GameplayEventMatch.Exact);

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.DamageReceived)));

		Assert.Equal(1, exact);
		Assert.Equal(0, ancestor);

		Assert.Equal(0, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		// Publishing the ancestor itself reaches the exact listener on
		// that tag; it reaches the descendant listener only if the
		// subscriber opted into hierarchical matching.
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.Damage)));
		Assert.Equal(1, exact);
		Assert.Equal(1, ancestor);
	}

	[Fact]
	public void HierarchicalSubscription_MatchesDescendants()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int damage = 0;
		int eventRoot = 0;

		events.Subscribe(
			EventSchema.Damage,
			_ => damage++,
			GameplayEventMatch.Hierarchical);

		events.Subscribe(
			EventSchema.Event,
			_ => eventRoot++,
			GameplayEventMatch.Hierarchical);

		Assert.Equal(2, events.Publish(new GameplayEvent(
			EventSchema.DamageReceived)));

		Assert.Equal(1, damage);
		Assert.Equal(1, eventRoot);

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.Equal(1, damage);
		Assert.Equal(2, eventRoot);
	}

	[Fact]
	public void DispatchOrder_IsExactThenAncestorsThenWildcard()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		List<string> order = [];

		events.Subscribe(
			EventSchema.Event,
			_ => order.Add("event"),
			GameplayEventMatch.Hierarchical);

		events.Subscribe(
			EventSchema.Damage,
			_ => order.Add("damage"),
			GameplayEventMatch.Hierarchical);

		events.Subscribe(
			EventSchema.DamageReceived,
			_ => order.Add("exact"));

		events.Subscribe(
			GameplayTag.None,
			_ => order.Add("wildcard"));

		events.Publish(new GameplayEvent(EventSchema.DamageReceived));

		Assert.Equal(
			["exact", "damage", "event", "wildcard"],
			order);
	}

	[Fact]
	public void DispatchOrder_WithinABucketIsNewestFirst()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		List<int> order = [];

		events.Subscribe(EventSchema.AttackHit, _ => order.Add(1));
		events.Subscribe(EventSchema.AttackHit, _ => order.Add(2));
		events.Subscribe(EventSchema.AttackHit, _ => order.Add(3));

		events.Publish(new GameplayEvent(EventSchema.AttackHit));

		Assert.Equal([3, 2, 1], order);
	}

	[Fact]
	public void SourceAndTargetFilters_SkipIrrelevantEvents()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity attacker = world.CreateEntity();
		Entity victim = world.CreateEntity();
		Entity other = world.CreateEntity();

		int sourceMatches = 0;
		int targetMatches = 0;

		events.Subscribe(
			EventSchema.AttackHit,
			_ => sourceMatches++,
			sourceFilter: attacker);

		events.Subscribe(
			EventSchema.AttackHit,
			_ => targetMatches++,
			targetFilter: victim);

		Assert.Equal(2, events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			attacker,
			victim)));

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			attacker,
			other)));

		// The source filter rejects this one; the target filter accepts
		// it because the victim is the target.
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit,
			other,
			victim)));

		Assert.Equal(2, sourceMatches);
		Assert.Equal(2, targetMatches);
	}

	[Fact]
	public void Unsubscribe_RetiresTheHandle()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		GameplayEventSubscription subscription = events.Subscribe(
			EventSchema.AttackHit,
			_ => { });

		Assert.Equal(1, events.SubscriptionCount);
		Assert.True(events.Unsubscribe(subscription));
		Assert.False(events.Unsubscribe(subscription));
		Assert.Equal(0, events.SubscriptionCount);
		Assert.Equal(0, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
	}

	[Fact]
	public void StaleHandle_DoesNotRemoveAReusedSubscription()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		GameplayEventSubscription stale = events.Subscribe(
			EventSchema.AttackHit,
			_ => { });

		Assert.True(events.Unsubscribe(stale));

		int delivered = 0;
		GameplayEventSubscription replacement = events.Subscribe(
			EventSchema.AttackHit,
			_ => delivered++);

		Assert.Equal(stale.Slot, replacement.Slot);
		Assert.NotEqual(stale, replacement);
		Assert.False(events.Unsubscribe(stale));
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, delivered);
	}

	[Fact]
	public void HandleFromAnotherSystem_IsRejected()
	{
		using World world = new();
		using GameplayEventSystem mine = new(world);
		using GameplayEventSystem other = new(world);

		GameplayEventSubscription subscription = mine.Subscribe(
			EventSchema.AttackHit,
			_ => { });

		Assert.Equal(subscription.Slot, other.Subscribe(
			EventSchema.AttackHit,
			_ => { }).Slot);
		Assert.False(other.Unsubscribe(subscription));
		Assert.True(mine.Unsubscribe(subscription));
	}

	[Fact]
	public void DestroyingTheScopedOwner_RemovesItsSubscriptions()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity owner = world.CreateEntity();
		Entity other = world.CreateEntity();

		int ownerCalls = 0;
		int otherCalls = 0;

		events.Subscribe(
			EventSchema.AttackHit,
			_ => ownerCalls++,
			owner: owner);

		events.Subscribe(
			EventSchema.AttackHit,
			_ => otherCalls++,
			owner: other);

		Assert.Equal(2, events.SubscriptionCount);
		Assert.True(world.DestroyEntity(owner));
		Assert.Equal(1, events.SubscriptionCount);

		events.Publish(new GameplayEvent(EventSchema.AttackHit));

		Assert.Equal(0, ownerCalls);
		Assert.Equal(1, otherCalls);
	}

	[Fact]
	public void ScopedSubscriptionToADeadEntity_Throws()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity dead = world.CreateEntity();

		Assert.True(world.DestroyEntity(dead));

		Assert.Throws<ArgumentException>(() => events.Subscribe(
			EventSchema.AttackHit,
			_ => { },
			owner: dead));
	}

	[Fact]
	public void UnsubscribeAll_RemovesOnlyThatOwnersScopedSubscriptions()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		for (int index = 0; index < 8; index++)
		{
			events.Subscribe(EventSchema.AttackHit, _ => { }, owner: first);
			events.Subscribe(EventSchema.AttackHit, _ => { }, owner: second);
		}

		events.Subscribe(EventSchema.AttackHit, _ => { });

		Assert.Equal(8, events.UnsubscribeAll(first));
		Assert.Equal(0, events.UnsubscribeAll(first));
		Assert.Equal(9, events.SubscriptionCount);
		Assert.Equal(8, events.UnsubscribeAll(second));
		Assert.Equal(1, events.SubscriptionCount);
	}

	[Fact]
	public void RecycledOwnerBeforeDestructionNotification_OnlyTheReplacementRuns()
	{
		using World world = new();

		Entity doomed = world.CreateEntity();
		Entity replacement = default;
		GameplayEventSystem events = null!;
		int oldCalls = 0;
		int newCalls = 0;

		// This handler runs before the event system observes the
		// destruction, and recycles the index.
		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != doomed)
			{
				return;
			}

			replacement = world.CreateEntity();

			Assert.Equal(doomed.Index, replacement.Index);

			events.Subscribe(
				EventSchema.AttackHit,
				_ => newCalls++,
				owner: replacement);
		};

		events = new GameplayEventSystem(world);

		events.Subscribe(
			EventSchema.AttackHit,
			_ => oldCalls++,
			owner: doomed);

		Assert.True(world.DestroyEntity(doomed));
		Assert.Equal(1, events.SubscriptionCount);
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(0, oldCalls);
		Assert.Equal(1, newCalls);
	}

	[Fact]
	public void Publish_WithoutSubscriptions_DoesNothing()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Assert.Equal(0, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
	}

	[Fact]
	public void Publish_WithInvalidTag_Throws()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		Assert.Throws<ArgumentException>(
			() => events.Publish(default));
	}

	[Fact]
	public void ExplicitNoneTag_IsAWildcard()
	{
		using World world = new();
		using GameplayEventSystem events = new(world);

		int calls = 0;

		events.Subscribe(GameplayTag.None, _ => calls++);

		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));
		Assert.Equal(1, events.Publish(new GameplayEvent(
			EventSchema.InteractionCompleted)));
		Assert.Equal(2, calls);
	}
}
