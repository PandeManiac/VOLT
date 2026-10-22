using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;

namespace Volt.Gameplay.Tests.Abilities;

public sealed class GameplayAbilityActivationTests
{
	[Fact]
	public void InstantAbility_PaysCostGrantsCooldownAndEnds()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.ActivatedAndEnded,
			result.Status);
		Assert.True(result.Succeeded);
		Assert.Equal(handle, result.Handle);
		Assert.Equal(actor, result.Owner);
		Assert.Equal(1, result.Level);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.True(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.True(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Dash));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);

		double remaining =
			runtime.Abilities.GetCooldownTimeRemaining(
				actor,
				AbilitySchema.Dash);

		Assert.InRange(remaining, 1.9, 2.0);
		Assert.Equal("Granted,Activated,Ended", recorder.Sequence());
	}

	[Fact]
	public void Cooldown_BlocksUntilTheEffectExpires()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCooldown,
			runtime.Abilities.TryActivate(handle).Status);

		runtime.Effects.Advance(2f);

		Assert.False(runtime.Tags.HasTag(actor, AbilitySchema.CooldownDash));
		Assert.False(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Dash));
		Assert.Equal(0d, runtime.Abilities.GetCooldownTimeRemaining(
			actor,
			AbilitySchema.Dash));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(
			30f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
	}

	[Fact]
	public void InsufficientCost_RejectsWithoutSideEffects()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			actor,
			AttributeSchema.Stamina,
			5f);

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCost,
			result.Status);

		Assert.False(result.Succeeded);
		Assert.Equal(
			5f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Dash));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal("Granted", recorder.Sequence());
	}

	[Fact]
	public void CostThatReachesZero_IsAffordable()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			actor,
			AttributeSchema.Stamina,
			10f);

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(
			0f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
	}

	[Fact]
	public void CostEffectRequirements_RejectTheActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			AbilitySchema.BlockedCostAbility);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCost,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
	}

	[Fact]
	public void ActivationRequirements_RejectWithoutSideEffects()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.False(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Dash));
		Assert.Equal("Granted", recorder.Sequence());
	}

	[Fact]
	public void RequiredTag_AllowsTheActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.RequiresBuff);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
	}

	[Fact]
	public void Requirements_MatchTheTagHierarchy()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.RequiresStatus);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.Stun).Applied);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
	}

	[Fact]
	public void TargetRequirements_NeedALivingMatchingTarget()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Strike);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle, target).Status);

		Assert.True(runtime.Effects.Apply(
			target,
			EffectSchema.BurnTenSeconds).Applied);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle, target);

		Assert.True(result.Succeeded);
		Assert.Equal(target, result.Target);

		runtime.World.DestroyEntity(target);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByRequirements,
			runtime.Abilities.TryActivate(handle, target).Status);
	}

	[Fact]
	public void ActiveBlocker_BlocksMatchingAssetTags()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle guard =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Guard);

		GameplayAbilityHandle dash =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash);

		Assert.True(runtime.Abilities.TryActivate(guard).Succeeded);

		Assert.Equal(
			GameplayAbilityActivationStatus.BlockedByActiveAbility,
			runtime.Abilities.TryActivate(dash).Status);

		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));

		Assert.True(runtime.Abilities.End(guard));
		Assert.True(runtime.Abilities.TryActivate(dash).Succeeded);
	}

	[Fact]
	public void ActiveAbility_RejectsOrRetriggers()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder recorder = new(runtime.Abilities);

		Entity actor = runtime.CreateActor();
		Entity other = runtime.CreateActor();

		GameplayAbilityHandle channel =
			runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel);

		GameplayAbilityHandle retrigger =
			runtime.Abilities.GiveAbility(other, AbilitySchema.Retriggerable);

		Assert.True(runtime.Abilities.TryActivate(channel).Succeeded);

		Assert.Equal(
			GameplayAbilityActivationStatus.AlreadyActive,
			runtime.Abilities.TryActivate(channel).Status);

		Assert.True(runtime.Abilities.TryActivate(retrigger).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(retrigger).Succeeded);
		Assert.True(runtime.Abilities.IsActive(retrigger));

		// Two activations of the retriggerable ability, plus the
		// channel's own activation.
		Assert.Equal(3, recorder.Count(GameplayAbilityChangeKind.Activated));
		Assert.Equal(1, recorder.Count(GameplayAbilityChangeKind.Ended));
	}

	[Fact]
	public void TryActivateByInput_AttemptsEveryMatchingGrant()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Abilities.GiveAbility(actor, AbilitySchema.Dash, inputId: 1);
		runtime.Abilities.GiveAbility(actor, AbilitySchema.Channel, inputId: 1);
		runtime.Abilities.GiveAbility(actor, AbilitySchema.Jump, inputId: 2);

		Assert.Equal(2, runtime.Abilities.TryActivateByInput(actor, 1));
		Assert.Equal(0, runtime.Abilities.TryActivateByInput(actor, 1));
		Assert.Equal(1, runtime.Abilities.TryActivateByInput(actor, 2));
		Assert.Equal(0, runtime.Abilities.TryActivateByInput(actor, 9));
	}

	[Fact]
	public void ActivationWithoutACooldown_ReportsZero()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.False(runtime.Abilities.IsOnCooldown(actor, AbilitySchema.Channel));
		Assert.Equal(0d, runtime.Abilities.GetCooldownTimeRemaining(
			actor,
			AbilitySchema.Channel));
	}
}
