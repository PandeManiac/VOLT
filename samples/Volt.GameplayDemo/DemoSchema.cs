using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.GameplayDemo;

/// <summary>
/// The small content set used by the gameplay demonstration.
/// </summary>
/// <remarks>
/// Definitions are immutable and shared, exactly as they would be in a
/// game's content pipeline. Static field order registers the tags and
/// attributes before the effects and abilities that reference them.
/// </remarks>
internal static class DemoSchema
{
	private const string Root = "VoltDemo";

	internal static readonly GameplayTag AbilityDash =
		Register($"{Root}.Ability.Dash");

	internal static readonly GameplayTag AbilityFocus =
		Register($"{Root}.Ability.Focus");

	internal static readonly GameplayTag AbilityOvercharge =
		Register($"{Root}.Ability.Overcharge");

	internal static readonly GameplayTag CooldownDash =
		Register($"{Root}.Cooldown.Dash");

	internal static readonly GameplayTag EventDashReleased =
		Register($"{Root}.Event.Dash.Released");

	internal static readonly GameplayTag StatusDashing =
		Register($"{Root}.Status.Dashing");

	internal static readonly GameplayTag StatusStunned =
		Register($"{Root}.Status.Stunned");

	internal static readonly GameplayAttribute Stamina =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Attributes.Stamina",
			defaultBaseValue: 50f,
			minimum: 0f,
			maximum: 50f);

	internal static readonly GameplayAttribute Speed =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Attributes.Speed",
			defaultBaseValue: 10f,
			minimum: 0f,
			maximum: 100f);

	internal static readonly GameplayEffectDefinition DashCost =
		GameplayEffectDefinition
			.Create("Demo.Effect.DashCost")
			.Instant()
			.Modifier(
				Stamina,
				GameplayModifierOperation.AddBase,
				-10f)
			.Build();

	internal static readonly GameplayEffectDefinition DashCooldown =
		GameplayEffectDefinition
			.Create("Demo.Effect.DashCooldown")
			.HasDuration(1f)
			.GrantTags(CooldownDash)
			.Build();

	internal static readonly GameplayEffectDefinition DashBuff =
		GameplayEffectDefinition
			.Create("Demo.Effect.DashBuff")
			.HasDuration(0.5f)
			.Modifier(
				Speed,
				GameplayModifierOperation.AddBase,
				5f)
			.GrantTags(StatusDashing)
			.Build();

	internal static readonly GameplayEffectDefinition FocusBuff =
		GameplayEffectDefinition
			.Create("Demo.Effect.FocusBuff")
			.HasDuration(1f)
			.Modifier(
				Speed,
				GameplayModifierOperation.AddBase,
				2f)
			.Build();

	internal static readonly GameplayEffectDefinition Stun =
		GameplayEffectDefinition
			.Create("Demo.Effect.Stun")
			.Infinite()
			.GrantTags(StatusStunned)
			.Build();

	/// <summary>
	/// The charged dash: pay, wind up, wait for release, apply the
	/// dash buff, then wait until the buff's tag disappears.
	/// </summary>
	internal static readonly GameplayAbilityDefinition ChargedDash =
		GameplayAbilityDefinition
			.Create("Demo.Ability.ChargedDash")
			.AssetTags(AbilityDash)
			.Cost(DashCost)
			.Cooldown(DashCooldown)
			.CancelOnTags(StatusStunned)
			.RequireToActivate(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(StatusStunned)))
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.25f)
					.WaitEvent(EventDashReleased)
					.ApplyEffect(DashBuff)
					.WaitTag(StatusDashing, GameplayTagWaitMode.Absent)
					.End()
					.Build())
			.Build();

	/// <summary>
	/// A second ability with its own independent task lifetime.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Focus =
		GameplayAbilityDefinition
			.Create("Demo.Ability.Focus")
			.AssetTags(AbilityFocus)
			.CancelOnTags(StatusStunned)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.5f)
					.ApplyEffect(FocusBuff)
					.End()
					.Build())
			.Build();

	/// <summary>
	/// An ability without a plan, used to show the application-facing
	/// task API.
	/// </summary>
	internal static readonly GameplayAbilityDefinition Overcharge =
		GameplayAbilityDefinition
			.Create("Demo.Ability.Overcharge")
			.AssetTags(AbilityOvercharge)
			.EndOnActivate(false)
			.Build();

	private static GameplayTag Register(string name)
	{
		return GameplayTagRegistry.Shared.Register(name);
	}
}
