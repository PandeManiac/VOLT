using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Reproduces rules read from Unreal 5.8.4's
/// <c>UGameplayAbility::CanActivateAbility</c>, <c>CheckCooldown</c>,
/// <c>CheckCost</c>, and <c>UAbilitySystemComponent::InternalTryActivateAbility</c>.
/// </summary>
public sealed class UnrealGameplayAbilityBehaviorTests
{
	[Fact]
	public void CooldownCheck_RunsBeforeCostAndRequirements()
	{
		// CanActivateAbility checks the cooldown, then the cost, then
		// the tag requirements, so a cooling-down ability with no
		// resources and a missing tag reports the cooldown.
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			AbilitySchema.ExternalDashCooldown).Applied);

		runtime.Attributes.SetBaseValue(
			actor,
			AttributeSchema.Stamina,
			0f);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.DashRequiringBuff);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			runtime.Abilities.TryActivate(handle).Status);
	}

	[Fact]
	public void CostCheck_RunsBeforeTagRequirements()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			actor,
			AttributeSchema.Stamina,
			5f);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.DashRequiringBuff);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCost,
			runtime.Abilities.TryActivate(handle).Status);
	}

	[Fact]
	public void Cooldown_IsReadFromTheOwnersTags()
	{
		// CheckCooldown calls HasAnyMatchingGameplayTags on the
		// cooldown effect's granted tags, so an unrelated effect that
		// grants the same tag also blocks the ability.
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Effects.Apply(
			actor,
			AbilitySchema.ExternalDashCooldown).Applied);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			runtime.Abilities.TryActivate(handle).Status);
	}

	[Fact]
	public void InfiniteCooldown_NeverExpires()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.InfiniteCooldownAbility);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.True(runtime.Abilities.IsOnCooldown(
			actor,
			AbilitySchema.InfiniteCooldownAbility));

		Assert.True(double.IsPositiveInfinity(
			runtime.Abilities.GetCooldownTimeRemaining(
				actor,
				AbilitySchema.InfiniteCooldownAbility)));

		runtime.Effects.Advance(1_000_000f);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			runtime.Abilities.TryActivate(handle).Status);
	}

	[Fact]
	public void BlockTags_ApplyOnlyWhileTheBlockerIsActive()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle guard =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Guard);

		GameplayAbilityHandle jump =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Jump);

		Assert.True(runtime.Abilities.TryActivate(guard).Succeeded);

		Assert.Equal(
			GameplayAbilityActivationStatus.BlockedByActiveAbility,
			runtime.Abilities.TryActivate(jump).Status);

		Assert.True(runtime.Abilities.End(guard));
		Assert.True(runtime.Abilities.TryActivate(jump).Succeeded);
	}

	[Fact]
	public void Ability_DoesNotBlockItself()
	{
		// Unreal has no notion of "other abilities" in the block
		// check, but VOLT excludes the requesting grant, so a
		// re-activation reports AlreadyActive rather than Blocked.
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle guard =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Guard);

		Assert.True(runtime.Abilities.TryActivate(guard).Succeeded);

		Assert.Equal(
			GameplayAbilityActivationStatus.AlreadyActive,
			runtime.Abilities.TryActivate(guard).Status);
	}

	[Fact]
	public void CostCheck_IgnoresNonAdditiveModifiers()
	{
		// CanApplyAttributeModifiers skips every operation except
		// Additive, so a multiplier cost never rejects an activation.
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			actor,
			AttributeSchema.Stamina,
			0f);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.HalfCostAbility);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
	}

	[Fact]
	public void Retrigger_EndsTheRunningInstanceFirst()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.Retriggerable);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.Equal(
			"Granted,Activated,Ended,Activated",
			recorder.Sequence());

		Assert.True(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void Requirements_MatchNonExactTags()
	{
		using AbilityRuntime runtime = new();
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.RequiresStatus);

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.Stun).Applied);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
	}

	[Fact]
	public void RevokingAnActiveAbility_CancelsItFirst()
	{
		// ClearAbility cancels the active instance before removing the
		// spec, so listeners see Cancelled before Revoked.
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		var actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.RevokeAbility(handle));

		Assert.Equal(
			"Granted,Activated,Cancelled,Revoked",
			recorder.Sequence());
	}
}
