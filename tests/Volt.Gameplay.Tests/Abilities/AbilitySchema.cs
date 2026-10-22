using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// The ability definitions shared by the ability tests.
/// </summary>
/// <remarks>
/// Definitions are immutable, so they are built once and reused. Tags
/// and effects live here rather than in <see cref="TagSchema"/> and
/// <see cref="EffectSchema"/> because only ability tests need them.
/// </remarks>
internal static class AbilitySchema
{
	private const string Root = "VoltGameplayTests.Ability";
	private const string CooldownRoot = "VoltGameplayTests.Cooldown";

	internal static readonly GameplayTag AbilityJump =
		Register($"{Root}.Jump");

	internal static readonly GameplayTag AbilityChannel =
		Register($"{Root}.Channel");

	internal static readonly GameplayTag AbilityGuard =
		Register($"{Root}.Guard");

	internal static readonly GameplayTag AbilityShove =
		Register($"{Root}.Shove");

	internal static readonly GameplayTag AbilityStrike =
		Register($"{Root}.Strike");

	internal static readonly GameplayTag AbilityRetrigger =
		Register($"{Root}.Retrigger");

	internal static readonly GameplayTag AbilityFree =
		Register($"{Root}.Free");

	internal static readonly GameplayTag Cooldown =
		Register(CooldownRoot);

	internal static readonly GameplayTag CooldownDash =
		Register($"{CooldownRoot}.Dash");

	internal static readonly GameplayTag CooldownJump =
		Register($"{CooldownRoot}.Jump");

	internal static readonly GameplayTag CooldownInfinite =
		Register($"{CooldownRoot}.Infinite");

	/// <summary>
	/// An instant cost of ten stamina.
	/// </summary>
	internal static readonly GameplayEffectDefinition CostTen =
		GameplayEffectDefinition
			.Create("Test.Ability.CostTen")
			.Instant()
			.Modifier(
				AttributeSchema.Stamina,
				GameplayModifierOperation.AddBase,
				-10f)
			.Build();

	/// <summary>
	/// An instant cost of thirty stamina.
	/// </summary>
	internal static readonly GameplayEffectDefinition CostThirty =
		GameplayEffectDefinition
			.Create("Test.Ability.CostThirty")
			.Instant()
			.Modifier(
				AttributeSchema.Stamina,
				GameplayModifierOperation.AddBase,
				-30f)
			.Build();

	/// <summary>
	/// An instant cost the affordability check must ignore, because it
	/// is not additive.
	/// </summary>
	internal static readonly GameplayEffectDefinition CostHalf =
		GameplayEffectDefinition
			.Create("Test.Ability.CostHalf")
			.Instant()
			.Modifier(
				AttributeSchema.Stamina,
				GameplayModifierOperation.MultiplyAdditive,
				0.5f)
			.Build();

	/// <summary>
	/// An instant cost that refuses to apply while stunned.
	/// </summary>
	internal static readonly GameplayEffectDefinition CostIgnoringStun =
		GameplayEffectDefinition
			.Create("Test.Ability.CostIgnoringStun")
			.Instant()
			.Modifier(
				AttributeSchema.Stamina,
				GameplayModifierOperation.AddBase,
				-5f)
			.RequireToApply(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

	/// <summary>
	/// A two second cooldown that grants its tag.
	/// </summary>
	internal static readonly GameplayEffectDefinition DashCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.DashCooldown")
			.HasDuration(2f)
			.GrantTags(CooldownDash)
			.Build();

	/// <summary>
	/// A cooldown that never ends.
	/// </summary>
	internal static readonly GameplayEffectDefinition InfiniteCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.InfiniteCooldown")
			.Infinite()
			.GrantTags(CooldownInfinite)
			.Build();

	/// <summary>
	/// A cooldown that expires without being removed.
	/// </summary>
	internal static readonly GameplayEffectDefinition JumpCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.JumpCooldown")
			.HasDuration(5f)
			.GrantTags(CooldownJump)
			.Build();

	/// <summary>
	/// An external effect that grants the same tag as the dash
	/// cooldown, to show that the cooldown check reads owner tags.
	/// </summary>
	internal static readonly GameplayEffectDefinition ExternalDashCooldown =
		GameplayEffectDefinition
			.Create("Test.Ability.ExternalDashCooldown")
			.Infinite()
			.GrantTags(CooldownDash)
			.Build();

	/// <summary>
	/// An instant ability with a cost, a cooldown, and a stun
	/// requirement.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Dash =
		GameplayAbilityDefinition
			.Create("Test.Ability.Dash")
			.AssetTags(TagSchema.AbilityDash)
			.Cost(CostTen)
			.Cooldown(DashCooldown)
			.RequireToActivate(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(TagSchema.StatusStunned)))
			.Build();

	/// <summary>
	/// An instant ability with a cost that most actors cannot pay.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Jump =
		GameplayAbilityDefinition
			.Create("Test.Ability.Jump")
			.AssetTags(AbilityJump)
			.Cost(CostThirty)
			.Cooldown(JumpCooldown)
			.Build();

	/// <summary>
	/// An ability that stays active until it ends or is cancelled.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Channel =
		GameplayAbilityDefinition
			.Create("Test.Ability.Channel")
			.AssetTags(AbilityChannel)
			.EndOnActivate(false)
			.Build();

	/// <summary>
	/// A lasting ability that a stun cancels.
	/// </summary>
	internal static readonly GameplayAbilityDefinition ChannelCancelOnStun =
		GameplayAbilityDefinition
			.Create("Test.Ability.ChannelCancelOnStun")
			.AssetTags(AbilityChannel)
			.CancelOnTags(TagSchema.StatusStunned)
			.EndOnActivate(false)
			.Build();

	/// <summary>
	/// A lasting ability that blocks every other ability by their
	/// shared asset tag.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Guard =
		GameplayAbilityDefinition
			.Create("Test.Ability.Guard")
			.AssetTags(AbilityGuard)
			.BlockAbilitiesWithTags(TagSchema.Ability)
			.EndOnActivate(false)
			.Build();

	/// <summary>
	/// An instant ability that cancels the dash and the channel.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Shove =
		GameplayAbilityDefinition
			.Create("Test.Ability.Shove")
			.AssetTags(AbilityShove)
			.CancelAbilitiesWithTags(TagSchema.AbilityDash, AbilityChannel)
			.Build();

	/// <summary>
	/// A lasting ability that may be retriggered while active.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Retriggerable =
		GameplayAbilityDefinition
			.Create("Test.Ability.Retriggerable")
			.AssetTags(AbilityRetrigger)
			.Retrigger(true)
			.EndOnActivate(false)
			.Build();

	/// <summary>
	/// An instant ability that requires a burning target.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Strike =
		GameplayAbilityDefinition
			.Create("Test.Ability.Strike")
			.AssetTags(AbilityStrike)
			.RequireOnTarget(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.StatusBurning)))
			.Build();

	/// <summary>
	/// An instant ability that requires the owner to have any effect
	/// tag.
	/// </summary>
	internal static readonly GameplayAbilityDefinition RequiresBuff =
		GameplayAbilityDefinition
			.Create("Test.Ability.RequiresBuff")
			.AssetTags(AbilityFree)
			.RequireToActivate(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.EffectBuff)))
			.Build();

	/// <summary>
	/// An instant ability whose requirement names the parent of the
	/// tag the owner has.
	/// </summary>
	internal static readonly GameplayAbilityDefinition RequiresStatus =
		GameplayAbilityDefinition
			.Create("Test.Ability.RequiresStatus")
			.AssetTags(AbilityFree)
			.RequireToActivate(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.Status)))
			.Build();

	/// <summary>
	/// An instant ability with an infinite cooldown.
	/// </summary>
	internal static readonly GameplayAbilityDefinition InfiniteCooldownAbility =
		GameplayAbilityDefinition
			.Create("Test.Ability.InfiniteCooldownAbility")
			.AssetTags(AbilityJump)
			.Cooldown(InfiniteCooldown)
			.Build();

	/// <summary>
	/// An instant ability whose cost effect refuses to apply while
	/// stunned.
	/// </summary>
	internal static readonly GameplayAbilityDefinition BlockedCostAbility =
		GameplayAbilityDefinition
			.Create("Test.Ability.BlockedCostAbility")
			.AssetTags(AbilityFree)
			.Cost(CostIgnoringStun)
			.Build();

	/// <summary>
	/// An instant ability whose only cost is a multiplier.
	/// </summary>
	internal static readonly GameplayAbilityDefinition HalfCostAbility =
		GameplayAbilityDefinition
			.Create("Test.Ability.HalfCostAbility")
			.AssetTags(AbilityFree)
			.Cost(CostHalf)
			.Build();

	/// <summary>
	/// An instant ability with a cost, a cooldown, and a requirement
	/// that fails, used to show which check reports first.
	/// </summary>
	internal static readonly GameplayAbilityDefinition DashRequiringBuff =
		GameplayAbilityDefinition
			.Create("Test.Ability.DashRequiringBuff")
			.AssetTags(TagSchema.AbilityDash)
			.Cost(CostTen)
			.Cooldown(DashCooldown)
			.RequireToActivate(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(TagSchema.EffectBuff)))
			.Build();

	private static GameplayTag Register(string name)
	{
		return GameplayTagRegistry.Shared.Register(name);
	}
}
