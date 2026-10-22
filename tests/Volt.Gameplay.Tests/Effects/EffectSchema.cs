using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// The gameplay effect definitions shared by the effect tests.
/// </summary>
/// <remarks>
/// Definitions are immutable, so they are built once and reused. The
/// unbounded attribute isolates arithmetic from clamping; the health
/// attribute exercises a real range.
/// </remarks>
internal static class EffectSchema
{
	/// <summary>
	/// An instant effect that subtracts ten from an unbounded value.
	/// </summary>
	internal static readonly GameplayEffectDefinition InstantMinusTen =
		GameplayEffectDefinition
			.Create("Test.InstantMinusTen")
			.Instant()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-10f)
			.Build();

	/// <summary>
	/// An instant effect that multiplies before adding, so the
	/// execution order inside one application is observable.
	/// </summary>
	internal static readonly GameplayEffectDefinition InstantScaleThenAdd =
		GameplayEffectDefinition
			.Create("Test.InstantScaleThenAdd")
			.Instant()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.MultiplyAdditive,
				2f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				5f)
			.Build();

	/// <summary>
	/// An instant effect that adds health, which a full health bar
	/// clamps away.
	/// </summary>
	internal static readonly GameplayEffectDefinition InstantHealTen =
		GameplayEffectDefinition
			.Create("Test.InstantHealTen")
			.Instant()
			.Modifier(
				AttributeSchema.Health,
				GameplayModifierOperation.AddBase,
				10f)
			.Build();

	/// <summary>
	/// A five second buff to the unbounded attribute.
	/// </summary>
	internal static readonly GameplayEffectDefinition BuffFiveSeconds =
		GameplayEffectDefinition
			.Create("Test.BuffFiveSeconds")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				20f)
			.GrantTags(TagSchema.EffectBuff)
			.Build();

	/// <summary>
	/// A one second tick of two damage, over ten seconds.
	/// </summary>
	internal static readonly GameplayEffectDefinition BurnTenSeconds =
		GameplayEffectDefinition
			.Create("Test.BurnTenSeconds")
			.HasDuration(10f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-2f)
			.GrantTags(TagSchema.StatusBurning)
			.Build();

	/// <summary>
	/// The same burning effect without an execution on application.
	/// </summary>
	internal static readonly GameplayEffectDefinition BurnWaitingFirstTick =
		GameplayEffectDefinition
			.Create("Test.BurnWaitingFirstTick")
			.HasDuration(3f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-2f)
			.ExecuteOnApplication(false)
			.Build();

	/// <summary>
	/// An infinite stun that grants a status tag and changes nothing.
	/// </summary>
	internal static readonly GameplayEffectDefinition Stun =
		GameplayEffectDefinition
			.Create("Test.Stun")
			.Infinite()
			.GrantTags(TagSchema.StatusStunned)
			.Build();

	/// <summary>
	/// An infinite buff that only works while the target burns.
	/// </summary>
	internal static readonly GameplayEffectDefinition OnlyWhileBurning =
		GameplayEffectDefinition
			.Create("Test.OnlyWhileBurning")
			.Infinite()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				7f)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

	/// <summary>
	/// An infinite buff that a stun removes.
	/// </summary>
	internal static readonly GameplayEffectDefinition RemovedByStun =
		GameplayEffectDefinition
			.Create("Test.RemovedByStun")
			.Infinite()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				3f)
			.RemoveWhen(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

	/// <summary>
	/// An instant effect that a stun blocks.
	/// </summary>
	internal static readonly GameplayEffectDefinition BlockedByStun =
		GameplayEffectDefinition
			.Create("Test.BlockedByStun")
			.Instant()
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				1f)
			.RequireToApply(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

	/// <summary>
	/// A bleeding effect that stacks per source, up to five.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedPerSource =
		GameplayEffectDefinition
			.Create("Test.BleedPerSource")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(GameplayEffectStackingType.AggregateBySource, 5)
			.Build();

	/// <summary>
	/// A bleeding effect that stacks per source without refreshing.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedNeverRefresh =
		GameplayEffectDefinition
			.Create("Test.BleedNeverRefresh")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(
				GameplayEffectStackingType.AggregateBySource,
				5,
				GameplayEffectStackingDurationPolicy.NeverRefresh)
			.Build();

	/// <summary>
	/// A bleeding effect whose stacks each add a full duration.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedExtend =
		GameplayEffectDefinition
			.Create("Test.BleedExtend")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(
				GameplayEffectStackingType.AggregateBySource,
				5,
				GameplayEffectStackingDurationPolicy.ExtendDuration)
			.Build();

	/// <summary>
	/// A stacked effect that loses one stack per expiry.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedPerStackExpiry =
		GameplayEffectDefinition
			.Create("Test.BleedPerStackExpiry")
			.HasDuration(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(
				GameplayEffectStackingType.AggregateByTarget,
				5,
				GameplayEffectStackingDurationPolicy.NeverRefresh,
				GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication,
				GameplayEffectStackingExpirationPolicy
					.RemoveSingleStackAndRefreshDuration)
			.Build();

	/// <summary>
	/// A stacked effect that keeps its stacks when its duration ends.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedKeepOnExpiry =
		GameplayEffectDefinition
			.Create("Test.BleedKeepOnExpiry")
			.HasDuration(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(
				GameplayEffectStackingType.AggregateByTarget,
				5,
				GameplayEffectStackingDurationPolicy.NeverRefresh,
				GameplayEffectStackingPeriodPolicy.ResetOnSuccessfulApplication,
				GameplayEffectStackingExpirationPolicy.RefreshDuration)
			.Build();

	/// <summary>
	/// A stacked effect whose magnitude ignores the stack count.
	/// </summary>
	internal static readonly GameplayEffectDefinition BleedFlatMagnitude =
		GameplayEffectDefinition
			.Create("Test.BleedFlatMagnitude")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 5)
			.FactorInStackCount(false)
			.Build();

	/// <summary>
	/// A stacked effect that grants an add multiplier, whose stacked
	/// magnitude is additive rather than exponential.
	/// </summary>
	internal static readonly GameplayEffectDefinition StackedMultiplier =
		GameplayEffectDefinition
			.Create("Test.StackedMultiplier")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.MultiplyAdditive,
				1.5f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	/// <summary>
	/// A stacked effect that grants a compound multiplier, whose
	/// stacked magnitude is a power.
	/// </summary>
	internal static readonly GameplayEffectDefinition StackedCompound =
		GameplayEffectDefinition
			.Create("Test.StackedCompound")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.MultiplyCompound,
				1.5f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	/// <summary>
	/// A periodic effect that stacks per target and resets its period.
	/// </summary>
	internal static readonly GameplayEffectDefinition BurnStacking =
		GameplayEffectDefinition
			.Create("Test.BurnStacking")
			.HasDuration(10f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	/// <summary>
	/// A periodic effect that only ticks while the target is stunned.
	/// </summary>
	internal static readonly GameplayEffectDefinition BurnWhileStunned =
		GameplayEffectDefinition
			.Create("Test.BurnWhileStunned")
			.HasDuration(10f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.PeriodicInhibition(
				GameplayEffectPeriodInhibitionPolicy.ResetPeriod)
			.Build();

	/// <summary>
	/// A periodic effect that executes once as inhibition ends.
	/// </summary>
	internal static readonly GameplayEffectDefinition BurnExecuteOnResume =
		GameplayEffectDefinition
			.Create("Test.BurnExecuteOnResume")
			.HasDuration(10f)
			.Period(1f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				-1f)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.PeriodicInhibition(
				GameplayEffectPeriodInhibitionPolicy
					.ExecuteAndResetPeriod)
			.Build();
}
