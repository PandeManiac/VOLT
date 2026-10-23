using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Lifecycle;

/// <summary>
/// Verifies that all gameplay stores agree when a destruction handler
/// recycles an entity index before the stores observe the destruction.
/// </summary>
public sealed class CrossStoreLifecycleTests
{
	[Fact]
	public void ReplacementGivenEveryKindOfState_KeepsOnlyItsOwnState()
	{
		using World world = new();

		Entity first = world.CreateEntity();

		GameplayAttributeStore? attributes = null;
		GameplayTagStore? tags = null;
		GameplayEffectSystem? effects = null;
		GameplayAbilitySystem? abilities = null;

		Entity? replacement = null;
		GameplayEffectHandle replacementEffect = GameplayEffectHandle.None;
		GameplayAbilityHandle replacementAbility = GameplayAbilityHandle.None;

		// Subscribed before every store, so the replacement is fully
		// populated while the old generation still owns all its state.
		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != first)
			{
				return;
			}

			replacement = world.CreateEntity();

			tags!.AddTag(replacement.Value, TagSchema.StatusStunned);

			attributes!.SetBaseValue(
				replacement.Value,
				AttributeSchema.Unbounded,
				42f);

			replacementEffect = effects!.Apply(
				replacement.Value,
				EffectSchema.BuffFiveSeconds).Handle;

			replacementAbility = abilities!.GiveAbility(
				replacement.Value,
				AbilitySchema.Dash);
		};

		using GameplayAttributeStore actualAttributes = new(world);
		using GameplayTagStore actualTags = new(world);
		using GameplayEffectSystem actualEffects =
			new(world, actualAttributes, actualTags);
		using GameplayAbilitySystem actualAbilities =
			new(world, actualTags, actualEffects, actualAttributes);

		attributes = actualAttributes;
		tags = actualTags;
		effects = actualEffects;
		abilities = actualAbilities;

		tags.AddTag(first, TagSchema.StatusBurning);
		attributes.SetBaseValue(first, AttributeSchema.Unbounded, 10f);

		GameplayEffectHandle oldEffect = effects.Apply(
			first,
			EffectSchema.Stun).Handle;

		GameplayAbilityHandle oldAbility = abilities.GiveAbility(
			first,
			AbilitySchema.Channel);

		Assert.True(abilities.TryActivate(oldAbility).Succeeded);
		Assert.Equal(1, effects.ActiveEffectCount);
		Assert.Equal(1, abilities.ActiveAbilityCount);

		Assert.True(world.DestroyEntity(first));

		Assert.NotNull(replacement);
		Assert.Equal(first.Index, replacement.Value.Index);
		Assert.NotEqual(first.Generation, replacement.Value.Generation);
		Assert.False(replacementEffect.IsNone);
		Assert.False(replacementAbility.IsNone);

		// Tags: only the replacement's explicit tag and its effect's
		// tag remain, on one entity.
		Assert.Equal(1, tags.TaggedEntityCount);
		Assert.True(tags.HasTag(replacement.Value, TagSchema.StatusStunned));
		Assert.True(tags.HasTag(replacement.Value, TagSchema.EffectBuff));
		Assert.False(tags.HasTag(replacement.Value, TagSchema.StatusBurning));
		Assert.Equal(0, tags.GetTagCount(first, TagSchema.StatusBurning));

		// Attributes: the old base value and state are gone, while the
		// replacement keeps its base value and the new effect's modifier.
		Assert.Equal(42f, attributes.GetBaseValue(
			replacement.Value,
			AttributeSchema.Unbounded));
		Assert.Equal(62f, attributes.GetCurrentValue(
			replacement.Value,
			AttributeSchema.Unbounded));
		Assert.False(attributes.HasAttribute(
			first,
			AttributeSchema.Unbounded));
		Assert.Equal(1, attributes.AttributedEntityCount);

		// Effects: the infinite old instance is retired and the new
		// instance remains addressable.
		Assert.Equal(1, effects.ActiveEffectCount);
		Assert.False(effects.TryGetEffect(oldEffect, out _));
		Assert.True(effects.TryGetEffect(replacementEffect, out _));
		Assert.Equal(1, effects.GetEffectCount(replacement.Value));
		Assert.Equal(0, effects.GetEffectCount(first));

		// Abilities: the active old grant is retired without leaving an
		// active count, and the replacement's grant survives.
		Assert.Equal(1, abilities.GrantedAbilityCount);
		Assert.Equal(0, abilities.ActiveAbilityCount);
		Assert.False(abilities.TryGetAbility(oldAbility, out _));
		Assert.True(abilities.TryGetAbility(replacementAbility, out _));
		Assert.Equal(1, abilities.GetAbilityCount(replacement.Value));
		Assert.Equal(0, abilities.GetAbilityCount(first));
	}

	[Fact]
	public void SuccessiveGenerationsBeforeStoresRun_LeaveOnlyTheNewestState()
	{
		using World world = new();

		Entity first = world.CreateEntity();

		GameplayAttributeStore? attributes = null;
		GameplayTagStore? tags = null;
		GameplayEffectSystem? effects = null;

		Entity? newest = null;
		GameplayEffectHandle newestEffect = GameplayEffectHandle.None;

		world.EntityDestroyed += destroyed =>
		{
			if (destroyed.Generation == first.Generation)
			{
				Entity second = world.CreateEntity();

				tags!.AddTag(second, TagSchema.StatusBurning);
				attributes!.SetBaseValue(
					second,
					AttributeSchema.Unbounded,
					20f);
				effects!.Apply(second, EffectSchema.Stun);

				// The nested destruction again recycles the index
				// before any store has seen either old generation.
				Assert.True(world.DestroyEntity(second));
			}
			else if (newest is null)
			{
				newest = world.CreateEntity();

				tags!.AddTag(newest.Value, TagSchema.StatusStunned);
				attributes!.SetBaseValue(
					newest.Value,
					AttributeSchema.Unbounded,
					70f);

				newestEffect = effects!.Apply(
					newest.Value,
					EffectSchema.BuffFiveSeconds).Handle;
			}
		};

		using GameplayAttributeStore actualAttributes = new(world);
		using GameplayTagStore actualTags = new(world);
		using GameplayEffectSystem actualEffects =
			new(world, actualAttributes, actualTags);

		attributes = actualAttributes;
		tags = actualTags;
		effects = actualEffects;

		tags.AddTag(first, TagSchema.StatusBurning);
		attributes.SetBaseValue(first, AttributeSchema.Unbounded, 5f);
		effects.Apply(first, EffectSchema.Stun);

		Assert.True(world.DestroyEntity(first));

		Assert.NotNull(newest);
		Assert.Equal(first.Index, newest.Value.Index);
		Assert.False(newestEffect.IsNone);

		Assert.Equal(1, tags.TaggedEntityCount);
		Assert.True(tags.HasTag(newest.Value, TagSchema.StatusStunned));
		Assert.True(tags.HasTag(newest.Value, TagSchema.EffectBuff));
		Assert.Equal(0, tags.GetTagCount(first, TagSchema.StatusBurning));

		Assert.Equal(70f, attributes.GetBaseValue(
			newest.Value,
			AttributeSchema.Unbounded));
		Assert.Equal(90f, attributes.GetCurrentValue(
			newest.Value,
			AttributeSchema.Unbounded));
		Assert.Equal(1, attributes.AttributedEntityCount);

		Assert.Equal(1, effects.ActiveEffectCount);
		Assert.True(effects.TryGetEffect(newestEffect, out _));
		Assert.Equal(1, effects.GetEffectCount(newest.Value));
	}
}
