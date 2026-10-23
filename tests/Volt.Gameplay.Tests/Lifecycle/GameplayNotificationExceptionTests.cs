using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Lifecycle;

/// <summary>
/// Notifications are post-commit. An exception propagates to the
/// caller, while the committed mutation remains observable and no
/// internal loop or system flag stays stuck.
/// </summary>
public sealed class GameplayNotificationExceptionTests
{
	[Fact]
	public void ThrowingTagNotification_LeavesTheCommittedGrantRemovable()
	{
		using World world = new();
		using GameplayTagStore tags = new(world);

		Entity entity = world.CreateEntity();

		void ThrowOnChange(Entity _)
		{
			throw new InvalidOperationException("tag notification failed");
		}

		tags.TagsChanged += ThrowOnChange;

		Assert.Throws<InvalidOperationException>(
			() => tags.AddTag(entity, TagSchema.StatusStunned));

		Assert.True(tags.HasTag(entity, TagSchema.StatusStunned));
		Assert.Equal(1, tags.TaggedEntityCount);

		tags.TagsChanged -= ThrowOnChange;

		Assert.True(tags.RemoveTag(entity, TagSchema.StatusStunned));
		Assert.Equal(0, tags.TaggedEntityCount);
	}

	[Fact]
	public void ThrowingAttributeNotification_LeavesTheCommittedValueClearable()
	{
		using World world = new();
		using GameplayAttributeStore attributes = new(world);

		Entity entity = world.CreateEntity();

		void ThrowOnChange(AttributeChange _)
		{
			throw new InvalidOperationException("attribute notification failed");
		}

		attributes.AttributeChanged += ThrowOnChange;

		Assert.Throws<InvalidOperationException>(
			() => attributes.SetBaseValue(
				entity,
				AttributeSchema.Unbounded,
				40f));

		Assert.Equal(40f, attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(1, attributes.AttributedEntityCount);

		attributes.AttributeChanged -= ThrowOnChange;

		Assert.True(attributes.Clear(entity));
		Assert.Equal(0, attributes.AttributedEntityCount);
	}

	[Fact]
	public void ThrowingExpiryNotification_DoesNotLeaveAdvanceReentrancyBlocked()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.BuffFiveSeconds);

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.Expired)
			{
				throw new InvalidOperationException("expiry notification failed");
			}
		};

		Assert.Throws<InvalidOperationException>(
			() => runtime.Effects.Advance(5f));

		// Removal is committed before the notification, and the advance
		// guard is reset even though the handler failed.
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Effects.GetEffectCount(target));
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));

		runtime.Effects.Advance(1f);
		runtime.Effects.Apply(target, EffectSchema.Stun);

		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
	}

	[Fact]
	public void ThrowingRefreshNotification_DoesNotLeaveRefreshingStuck()
	{
		using EffectRuntime runtime = new();
		Entity target = runtime.CreateAffectedEntity();

		GameplayEffectHandle removed = runtime.Effects.Apply(
			target,
			EffectSchema.RemovedByStun).Handle;

		bool thrown = false;

		void ThrowOnRemoval(GameplayEffectChange change)
		{
			if (change.Kind == GameplayEffectChangeKind.Removed
				&& change.Handle == removed)
			{
				thrown = true;

				throw new InvalidOperationException("removal notification failed");
			}
		}

		runtime.Effects.Changed += ThrowOnRemoval;

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tags.AddTag(target, TagSchema.StatusStunned));

		Assert.True(thrown);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);

		// The tag commit survives the failed notification.
		Assert.True(runtime.Tags.HasTag(target, TagSchema.StatusStunned));

		runtime.Effects.Changed -= ThrowOnRemoval;
		runtime.Tags.RemoveTag(target, TagSchema.StatusStunned);

		// A later refresh must still evaluate a new effect; the failed
		// pass must not leave the entity's Refreshing flag set.
		runtime.Effects.Apply(target, EffectSchema.OnlyWhileBurning);

		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));

		runtime.Tags.AddTag(target, TagSchema.StatusBurning);

		Assert.Equal(107f, runtime.Attributes.GetCurrentValue(
			target,
			AttributeSchema.Unbounded));
	}
}
