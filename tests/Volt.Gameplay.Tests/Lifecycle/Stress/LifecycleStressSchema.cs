using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Lifecycle.Stress;

/// <summary>
/// The small, deliberately understood schema used by the randomized
/// cross-store lifecycle harness.
/// </summary>
/// <remarks>
/// <para>
/// The reference model cannot mirror arbitrary gameplay definitions,
/// so the harness restricts itself to one unbounded attribute, one
/// additive modifier per effect, and a handful of fixed durations and
/// stacking policies. That is enough to exercise every ownership
/// transition while keeping an independent oracle tractable.
/// </para>
/// <para>
/// The cooldown both grants a tag and subtracts from the resource. That
/// makes a rejected activation able to fail after the cooldown was
/// applied, which is the path the rollback logic must restore.
/// </para>
/// </remarks>
internal static class LifecycleStressSchema
{
	private const string TagRoot = "VoltGameplayTests.Stress";
	private const string EffectRoot = "Test.Stress";
	private const string AbilityRoot = "Test.Stress.Ability";

	internal static readonly GameplayTag Alpha =
		GameplayTagRegistry.Shared.Register($"{TagRoot}.Alpha");

	internal static readonly GameplayTag Beta =
		GameplayTagRegistry.Shared.Register($"{TagRoot}.Beta");

	internal static readonly GameplayTag Cooldown =
		GameplayTagRegistry.Shared.Register($"{TagRoot}.Cooldown");

	internal static readonly GameplayTag Ability =
		GameplayTagRegistry.Shared.Register($"{TagRoot}.Ability");

	internal static readonly GameplayTag[] Tags =
	[
		Alpha,
		Beta,
		Cooldown,
		Ability
	];

	internal static readonly GameplayAttribute Score =
		GameplayAttributeRegistry.Shared.Register(
			$"{TagRoot}.Score",
			defaultBaseValue: 100f);

	/// <summary>
	/// An effect with no tags and no modifiers. Its only notification is
	/// Applied, which makes it the clean probe for exception delivery at
	/// the effect boundary.
	/// </summary>
	internal static readonly GameplayEffectDefinition InertInfinite =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.InertInfinite")
			.Infinite()
			.Build();

	internal static readonly GameplayEffectDefinition InfiniteFive =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.InfiniteFive")
			.Infinite()
			.Modifier(Score, GameplayModifierOperation.AddBase, 5f)
			.GrantTags(Alpha)
			.Build();

	internal static readonly GameplayEffectDefinition DurationTen =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.DurationTen")
			.HasDuration(3f)
			.Modifier(Score, GameplayModifierOperation.AddBase, 10f)
			.GrantTags(Beta)
			.Build();

	internal static readonly GameplayEffectDefinition StackThree =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.StackThree")
			.HasDuration(6f)
			.Modifier(Score, GameplayModifierOperation.AddBase, 2f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	internal static readonly GameplayEffectDefinition InstantMinusSeven =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.InstantMinusSeven")
			.Instant()
			.Modifier(Score, GameplayModifierOperation.AddBase, -7f)
			.Build();

	internal static readonly GameplayEffectDefinition OnlyWhileAlpha =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.OnlyWhileAlpha")
			.Infinite()
			.Modifier(Score, GameplayModifierOperation.AddBase, 3f)
			.RequireWhileActive(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(Alpha)))
			.Build();

	internal static readonly GameplayEffectDefinition CooldownTwo =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.CooldownTwo")
			.HasDuration(2f)
			.Modifier(Score, GameplayModifierOperation.AddBase, -20f)
			.GrantTags(Cooldown)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	internal static readonly GameplayEffectDefinition CostTen =
		GameplayEffectDefinition
			.Create($"{EffectRoot}.CostTen")
			.Instant()
			.Modifier(Score, GameplayModifierOperation.AddBase, -10f)
			.Build();

	internal static readonly GameplayEffectDefinition[] Effects =
	[
		InfiniteFive,
		DurationTen,
		StackThree,
		InstantMinusSeven,
		OnlyWhileAlpha,
		CooldownTwo
	];

	internal static readonly GameplayAbilityDefinition Dash =
		GameplayAbilityDefinition
			.Create($"{AbilityRoot}.Dash")
			.AssetTags(Ability)
			.Cost(CostTen)
			.Cooldown(CooldownTwo)
			.Build();

	internal static readonly GameplayAbilityDefinition Channel =
		GameplayAbilityDefinition
			.Create($"{AbilityRoot}.Channel")
			.AssetTags(Ability)
			.EndOnActivate(false)
			.Build();

	internal static readonly GameplayAbilityDefinition ChannelCancelOnAlpha =
		GameplayAbilityDefinition
			.Create($"{AbilityRoot}.ChannelCancelOnAlpha")
			.AssetTags(Ability)
			.CancelOnTags(Alpha)
			.EndOnActivate(false)
			.Build();

	internal static readonly GameplayAbilityDefinition[] Abilities =
	[
		Dash,
		Channel,
		ChannelCancelOnAlpha
	];
}
