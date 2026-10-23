using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Abilities;

namespace Volt.Gameplay.Tests.Lifecycle.Stress;

/// <summary>
/// Pins the rollback behavior exercised randomly by the stress harness.
/// </summary>
public sealed class LifecycleStressCooldownTests
{
	[Fact]
	public void FailedCostAfterApplyingANewCooldown_RemovesThatCooldown()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			owner,
			LifecycleStressSchema.Score,
			25f);

		GameplayAbilityHandle dash = runtime.Abilities.GiveAbility(
			owner,
			LifecycleStressSchema.Dash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(dash);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCost,
			result.Status);
		Assert.False(runtime.Abilities.IsActive(dash));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(
			0,
			runtime.Tags.GetTagCount(
				owner,
				LifecycleStressSchema.Cooldown));
		Assert.Equal(
			25f,
			runtime.Attributes.GetCurrentValue(
				owner,
				LifecycleStressSchema.Score));
		Assert.False(runtime.Abilities.IsOnCooldown(
			owner,
			LifecycleStressSchema.Dash));
	}

	[Fact]
	public void FailedCostAfterStackingAnExistingCooldown_RestoresThePreviousStack()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();

		runtime.Attributes.SetBaseValue(
			owner,
			LifecycleStressSchema.Score,
			45f);

		GameplayEffectApplyResult seeded = runtime.Effects.Apply(
			owner,
			LifecycleStressSchema.CooldownTwo);

		Assert.Equal(GameplayEffectApplyStatus.Applied, seeded.Status);
		Assert.Equal(1, seeded.StackCount);
		Assert.Equal(
			25f,
			runtime.Attributes.GetCurrentValue(
				owner,
				LifecycleStressSchema.Score));

		// The cooldown effect stays active but no longer grants its tag.
		// That is the inhibited/tag-stripped shape that exposes rollback
		// bugs: activation passes the cooldown precheck, stacks the
		// existing instance, and then fails the cost re-check.
		Assert.True(runtime.Tags.RemoveTag(
			owner,
			LifecycleStressSchema.Cooldown));

		GameplayAbilityHandle dash = runtime.Abilities.GiveAbility(
			owner,
			LifecycleStressSchema.Dash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(dash);

		Assert.Equal(
			GameplayAbilityActivationStatus.RejectedByCost,
			result.Status);
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
		Assert.True(runtime.Effects.TryFindEffect(
			owner,
			LifecycleStressSchema.CooldownTwo,
			out ActiveGameplayEffect cooldown));
		Assert.Equal(1, cooldown.StackCount);
		Assert.Equal(
			25f,
			runtime.Attributes.GetCurrentValue(
				owner,
				LifecycleStressSchema.Score));
		Assert.Equal(
			0,
			runtime.Tags.GetTagCount(
				owner,
				LifecycleStressSchema.Cooldown));
	}
}
