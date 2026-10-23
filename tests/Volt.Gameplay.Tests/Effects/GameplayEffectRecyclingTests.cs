using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Covers effect cleanup when a destruction callback recycles the
/// target's index before the effect system receives the destruction.
/// </summary>
public sealed class GameplayEffectRecyclingTests
{
	[Fact]
	public void ReplacementAffectedBeforeDestructionCallback_RetiresOldInstances()
	{
		using World world = new();

		Entity first = world.CreateEntity();

		GameplayEffectSystem? system = null;
		Entity? replacement = null;
		GameplayEffectHandle replacementHandle = GameplayEffectHandle.None;

		// Subscribed before the stores, so the replacement is fully
		// affected while the old generation still owns its effects.
		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != first)
			{
				return;
			}

			replacement = world.CreateEntity();
			replacementHandle = system!.Apply(
				replacement.Value,
				EffectSchema.BuffFiveSeconds).Handle;
		};

		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);

		system = effects;

		EffectRecorder recorder = new(effects);

		GameplayEffectHandle infinite = effects.Apply(
			first,
			EffectSchema.Stun).Handle;

		GameplayEffectHandle periodic = effects.Apply(
			first,
			EffectSchema.BurnTenSeconds).Handle;

		Assert.Equal(2, effects.ActiveEffectCount);

		Assert.True(world.DestroyEntity(first));

		Assert.NotNull(replacement);
		Assert.Equal(first.Index, replacement.Value.Index);
		Assert.NotEqual(first.Generation, replacement.Value.Generation);
		Assert.False(replacementHandle.IsNone);

		// The old instances, including the infinite one that would
		// otherwise leak forever, are gone.
		Assert.Equal(1, effects.ActiveEffectCount);
		Assert.False(effects.TryGetEffect(infinite, out _));
		Assert.False(effects.TryGetEffect(periodic, out _));
		Assert.True(effects.TryGetEffect(replacementHandle, out _));
		Assert.Equal(1, effects.GetEffectCount(replacement.Value));
		Assert.Equal(0, effects.GetEffectCount(first));

		// The replacement keeps only its own effect's tags and
		// modifiers.
		Assert.True(tags.HasTag(replacement.Value, TagSchema.EffectBuff));
		Assert.False(tags.HasTag(replacement.Value, TagSchema.StatusStunned));
		Assert.False(tags.HasTag(replacement.Value, TagSchema.StatusBurning));
		Assert.Equal(1, tags.TaggedEntityCount);
		Assert.Equal(120f, attributes.GetCurrentValue(
			replacement.Value,
			AttributeSchema.Unbounded));

		// The retired periodic instance must not tick after its
		// deadline passes, even though the old handle is still held.
		int ticksBeforeAdvance = recorder.Count(
			GameplayEffectChangeKind.PeriodExecuted);

		effects.Advance(1f);

		Assert.Equal(
			ticksBeforeAdvance,
			recorder.Count(GameplayEffectChangeKind.PeriodExecuted));
		Assert.Equal(1, effects.ActiveEffectCount);
	}

	[Fact]
	public void RemoveAll_TagCallbackReusesTheNextSlot_KeepsTheNewEffect()
	{
		using World world = new();

		Entity target = world.CreateEntity();

		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);

		GameplayEffectHandle stun = effects.Apply(
			target,
			EffectSchema.Stun).Handle;

		effects.Apply(target, EffectSchema.BurnTenSeconds);

		GameplayEffectHandle replacement = GameplayEffectHandle.None;
		bool reentered = false;

		tags.TagsChanged += entity =>
		{
			// Removing the burning tag happens while RemoveAll still
			// holds the stale next-slot index. Removing the stun and
			// applying a new effect reuses that slot.
			if (reentered || entity != target)
			{
				return;
			}

			reentered = true;

			Assert.True(effects.Remove(stun));

			replacement = effects.Apply(
				target,
				EffectSchema.BuffFiveSeconds).Handle;
		};

		Assert.Equal(1, effects.RemoveAll(target));

		Assert.True(reentered);
		Assert.False(replacement.IsNone);
		Assert.Equal(1, effects.ActiveEffectCount);
		Assert.True(effects.TryGetEffect(replacement, out _));
		Assert.Equal(1, effects.GetEffectCount(target));
	}

	[Fact]
	public void RepeatedRecycling_DoesNotAccumulateEffectInstances()
	{
		using World world = new();

		Entity current = world.CreateEntity();

		GameplayEffectSystem? system = null;
		Entity? replacement = null;

		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != current)
			{
				return;
			}

			replacement = world.CreateEntity();
			system!.Apply(replacement.Value, EffectSchema.Stun);
		};

		using GameplayAttributeStore attributes = new(world);
		using GameplayTagStore tags = new(world);
		using GameplayEffectSystem effects = new(world, attributes, tags);

		system = effects;

		uint index = current.Index;

		for (int generation = 0; generation < 32; generation++)
		{
			Entity destroyed = current;

			Assert.True(world.DestroyEntity(destroyed));

			Assert.NotNull(replacement);
			Assert.Equal(index, replacement.Value.Index);
			Assert.NotEqual(destroyed.Generation, replacement.Value.Generation);
			Assert.Equal(1, effects.ActiveEffectCount);
			Assert.Equal(1, effects.GetEffectCount(replacement.Value));
			Assert.Equal(1, tags.GetTagCount(
				replacement.Value,
				TagSchema.StatusStunned));

			current = replacement.Value;
			replacement = null;
		}

		Assert.Equal(1, tags.TaggedEntityCount);
	}
}
