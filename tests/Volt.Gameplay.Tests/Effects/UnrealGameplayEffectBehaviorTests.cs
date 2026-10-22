using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Pins the behavior that was read from Unreal 5.8.4, so a future
/// refactor cannot quietly change a documented rule.
/// </summary>
/// <remarks>
/// Each test names the Unreal symbol it mirrors in a comment. The
/// specification in <c>src/Volt.Gameplay/GameplaySpec.md</c> is the
/// authority; these tests are its executable form.
/// </remarks>
public sealed class UnrealGameplayEffectBehaviorTests
{
	[Fact]
	public void InstantEffect_ExecutesAgainstTheBaseValue()
	{
		// FActiveGameplayEffectsContainer::ApplyModToAttribute calls
		// FAggregator::StaticExecModOnBaseValue and writes the base
		// value back, so temporary modifiers are untouched.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Attributes.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			50f);

		runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);

		Assert.Equal(90f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(140f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void InstantEffect_IsNeverStoredAsAnActiveEffect()
	{
		// UAbilitySystemComponent::ApplyGameplayEffectSpecToSelf only
		// calls ApplyGameplayEffectSpec for non-instant durations, and
		// ExecuteGameplayEffect never adds the spec to the container.
		using EffectRuntime runtime = new();
		EffectRecorder recorder = new(runtime.Effects);
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectApplyResult result =
			runtime.Effects.Apply(entity, EffectSchema.InstantMinusTen);

		Assert.True(result.Handle.IsNone);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Empty(recorder.Changes);
	}

	[Fact]
	public void DurationEffect_HoldsAModifierWithoutTouchingTheBase()
	{
		// AddActiveGameplayEffectGrantedTagsAndModifiers registers
		// aggregator mods for a spec without a period.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BuffFiveSeconds);

		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(120f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(1, runtime.Attributes.GetModifierCount(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void PeriodicEffect_SkipsTheAggregator()
	{
		// The period branch of
		// AddActiveGameplayEffectGrantedTagsAndModifiers adds no mods;
		// InternalExecutePeriodicGameplayEffect calls
		// ExecuteActiveEffectsFrom once per tick instead.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds);

		Assert.Equal(0, runtime.Attributes.GetModifierCount(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(98f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void GrantedTags_AreCountedPerInstanceNotPerStack()
	{
		// UpdateTagMap(Effect.Spec.Def->GetGrantedTags(), 1) runs once
		// per active instance, whatever its stack count.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition stacked = GameplayEffectDefinition
			.Create("Test.StackedGrant")
			.HasDuration(5f)
			.GrantTags(TagSchema.EffectShield)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

		runtime.Effects.Apply(entity, stacked);
		runtime.Effects.Apply(entity, stacked);
		runtime.Effects.Apply(entity, stacked);

		Assert.Equal(
			1,
			runtime.Tags.GetTagCount(entity, TagSchema.EffectShield));

		Assert.Equal(
			1,
			runtime.Effects.GetEffectCount(entity));
	}

	[Fact]
	public void ApplyingAtTheStackLimit_RefreshesInsteadOfFailing()
	{
		// bDenyOverflowApplication defaults to false, so
		// HandleActiveGameplayEffectStackOverflow reports success and
		// the count is clamped to the limit while the duration is
		// refreshed.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectHandle handle = runtime.Effects.Apply(
			entity,
			EffectSchema.BleedPerSource).Handle;

		for (int index = 0; index < 8; index++)
		{
			runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);
		}

		runtime.Effects.Advance(2f);

		Assert.True(runtime.Effects.TryGetEffect(
			handle,
			out ActiveGameplayEffect effect));

		Assert.Equal(5, effect.StackCount);
		Assert.Equal(3d, effect.RemainingDuration, 3);
	}

	[Fact]
	public void StackedAdditiveMagnitude_ScalesLinearly()
	{
		// GameplayEffectUtilities::ComputeStackedModifierMagnitude
		// multiplies a non-compound magnitude by the stack count.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);
		runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);
		runtime.Effects.Apply(entity, EffectSchema.BleedPerSource);

		// Three stacks of -1.
		Assert.Equal(97f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void StackedAdditiveMultiplier_UsesTheUnitBias()
	{
		// A bias of one means n copies of 1.5 add to 1.0 + n * 0.5.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.StackedMultiplier);
		runtime.Effects.Apply(entity, EffectSchema.StackedMultiplier);
		runtime.Effects.Apply(entity, EffectSchema.StackedMultiplier);

		// 1.0 + 3 * 0.5 = 2.5.
		Assert.Equal(250f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void StackedCompoundMultiplier_UsesAPower()
	{
		// A compound multiplier has a bias of zero and is raised to
		// the stack count instead of being multiplied by it.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.StackedCompound);
		runtime.Effects.Apply(entity, EffectSchema.StackedCompound);
		runtime.Effects.Apply(entity, EffectSchema.StackedCompound);

		// 1.5^3 = 3.375.
		Assert.Equal(337.5f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void StackingBySource_RequiresTheSameSource()
	{
		// FindStackableActiveGameplayEffect compares the spec
		// definition and, for AggregateBySource, the instigator.
		using EffectRuntime runtime = new();

		Entity target = runtime.CreateAffectedEntity();
		Entity source = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(target, EffectSchema.BleedPerSource, source);

		Assert.Equal(
			GameplayEffectApplyStatus.Stacked,
			runtime.Effects.Apply(
				target,
				EffectSchema.BleedPerSource,
				source).Status);

		Assert.Equal(
			GameplayEffectApplyStatus.Applied,
			runtime.Effects.Apply(
				target,
				EffectSchema.BleedPerSource,
				target).Status);

		Assert.Equal(2, runtime.Effects.GetEffectCount(target));
	}

	[Fact]
	public void StackingByTarget_IgnoresTheSource()
	{
		using EffectRuntime runtime = new();

		Entity target = runtime.CreateAffectedEntity();
		Entity firstSource = runtime.CreateAffectedEntity();
		Entity secondSource = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(
			target,
			EffectSchema.BleedFlatMagnitude,
			firstSource);

		Assert.Equal(
			GameplayEffectApplyStatus.Stacked,
			runtime.Effects.Apply(
				target,
				EffectSchema.BleedFlatMagnitude,
				secondSource).Status);

		Assert.Equal(1, runtime.Effects.GetEffectCount(target));
	}

	[Fact]
	public void InhibitedEffect_StillExpiresOnSchedule()
	{
		// CheckDuration runs regardless of bIsInhibited; only the
		// periodic execution checks inhibition.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Effects.Apply(entity, EffectSchema.BurnWhileStunned);

		Assert.Equal(1, runtime.Effects.ActiveEffectCount);

		runtime.Effects.Advance(10f);

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(100f, runtime.Attributes.GetBaseValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void Inhibition_WithdrawsModifiersAndTagsTogether()
	{
		// SetActiveGameplayEffectInhibit removes or re-adds both the
		// granted tags and the aggregator mods.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition gated = GameplayEffectDefinition
			.Create("Test.GatedBuff")
			.Infinite()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				10f)
			.GrantTags(TagSchema.EffectShield)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

		GameplayEffectHandle handle =
			runtime.Effects.Apply(entity, gated).Handle;

		Assert.Equal(0, runtime.Attributes.GetModifierCount(
			entity,
			AttributeSchema.Unbounded));
		Assert.False(runtime.Tags.HasTag(entity, TagSchema.EffectShield));

		GameplayEffectHandle burn =
			runtime.Effects.Apply(entity, EffectSchema.BurnTenSeconds).Handle;

		Assert.Equal(1, runtime.Attributes.GetModifierCount(
			entity,
			AttributeSchema.Unbounded));
		Assert.True(runtime.Tags.HasTag(entity, TagSchema.EffectShield));

		runtime.Effects.Remove(burn);

		Assert.Equal(0, runtime.Attributes.GetModifierCount(
			entity,
			AttributeSchema.Unbounded));
		Assert.False(runtime.Tags.HasTag(entity, TagSchema.EffectShield));
		Assert.True(runtime.Effects.TryGetEffect(handle, out _));
	}

	[Fact]
	public void StackingExpiration_ClearEntireStackRemovesEverything()
	{
		// A stack count of three with ClearEntireStack is removed by a
		// single expiry.
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		GameplayEffectDefinition stacked = GameplayEffectDefinition
			.Create("Test.ClearAll")
			.HasDuration(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 5)
			.Build();

		runtime.Effects.Apply(entity, stacked);
		runtime.Effects.Apply(entity, stacked);
		runtime.Effects.Apply(entity, stacked);

		runtime.Effects.Advance(1f);

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(100f, runtime.Attributes.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}
}
