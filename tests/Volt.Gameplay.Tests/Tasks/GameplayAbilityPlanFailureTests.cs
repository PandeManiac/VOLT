using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Defines the terminal policy for exceptions raised by plan steps,
/// activation commits, and task-completion continuations.
/// </summary>
/// <remarks>
/// A failed plan cancels its activation, releases every task and
/// subscription, and rethrows the original exception. Costs and
/// cooldowns that were already committed are intentionally kept,
/// exactly like any other cancellation. The ability never remains
/// active with no plan cursor and nothing that could resume it.
/// </remarks>
public sealed class GameplayAbilityPlanFailureTests
{
	private static readonly GameplayEffectDefinition PlanEffect =
		GameplayEffectDefinition
			.Create("Test.Plan.Failure.Effect")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				5f)
			.Build();

	private static readonly GameplayAbilityDefinition PublishFailure =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Publish")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition CooldownFailure =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Cooldown")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(AbilitySchema.DashCooldown)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition EffectFailure =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Effect")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.ApplyEffect(PlanEffect)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition ResumeAfterDuration =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Duration")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.1f)
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition ResumeAfterEvent =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Event")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitEvent(EventSchema.AttackHit)
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition ResumeAfterTag =
		GameplayAbilityDefinition
			.Create("Test.Plan.Failure.Tag")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitTag(
						TagSchema.StatusBurning,
						GameplayTagWaitMode.Present)
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	[Fact]
	public void ThrowingPublishStep_CancelsTheActivationAndKeepsTheCost()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		ThrowOnDamage(runtime);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			PublishFailure);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.TryActivate(handle));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Abilities.IsOnCooldown(
			actor,
			PublishFailure));
		Assert.Equal("Granted,Activated,Cancelled", abilities.Sequence());
	}

	[Fact]
	public void ThrowingEffectStep_CancelsTheActivationAndKeepsTheCost()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.Applied
				&& change.Definition == PlanEffect)
			{
				throw new InvalidOperationException(
					"plan effect notification failed");
			}
		};

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			EffectFailure);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.TryActivate(handle));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));

		// The effect itself committed before its notification threw.
		Assert.Equal(
			105f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Unbounded));
	}

	[Fact]
	public void ThrowingCooldownNotification_CancelsTheCommittedActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.Applied
				&& change.Definition == AbilitySchema.DashCooldown)
			{
				throw new InvalidOperationException(
					"cooldown notification failed");
			}
		};

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			CooldownFailure);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.TryActivate(handle));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		// The cooldown effect committed before its notification threw,
		// and the activation was cancelled rather than left active
		// without a plan. The cost was not paid, because the cooldown
		// is applied before the cost transaction.
		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.True(runtime.Abilities.IsOnCooldown(
			actor,
			CooldownFailure));
	}

	[Fact]
	public void ThrowingResumeAfterDuration_CancelsAndReleasesTheTask()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		ThrowOnDamage(runtime);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			ResumeAfterDuration);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Advance(0.1f));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		// The advance guard was released, and the canceled activation
		// left no task that could fire later.
		runtime.Tasks.Advance(10f);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void ThrowingResumeAfterEvent_CancelsAndRemovesTheSubscription()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			ResumeAfterEvent);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.Equal(1, runtime.Events.SubscriptionCount);

		GameplayEventSubscription throwing = ThrowOnDamage(runtime);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Events.Publish(new GameplayEvent(
				EventSchema.AttackHit)));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Events.Unsubscribe(throwing));
		Assert.Equal(0, runtime.Events.SubscriptionCount);
	}

	[Fact]
	public void ThrowingResumeAfterTag_CancelsAndReleasesTheWait()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			ResumeAfterTag);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		ThrowOnDamage(runtime);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusBurning));

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Advance(0.016f));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void CancellationCleanupFailure_IsAggregatedWithThePlanFailure()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		ThrowOnDamage(runtime);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			ResumeAfterDuration);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.TryGetActivation(
			handle,
			out GameplayAbilityActivation activation));

		GameplayAbilityTaskHandle externalTask =
			runtime.Tasks.WaitDuration(activation, 100f);

		Assert.True(runtime.Tasks.TryGetTask(externalTask, out _));

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled)
			{
				throw new InvalidOperationException(
					"cancellation notification failed");
			}
		};

		AggregateException exception = Assert.Throws<AggregateException>(
			() => runtime.Tasks.Advance(0.1f));

		Assert.Equal(2, exception.InnerExceptions.Count);
		Assert.IsType<InvalidOperationException>(
			exception.InnerExceptions[0]);
		Assert.IsType<InvalidOperationException>(
			exception.InnerExceptions[1]);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void FailedPlan_CanBeActivatedAgainAfterTheCauseIsRemoved()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			PublishFailure);

		GameplayEventSubscription throwing = runtime.Events.Subscribe(
			EventSchema.DamageReceived,
			_ => throw new InvalidOperationException(
				"plan publish failed"));

		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.TryActivate(handle));

		Assert.True(runtime.Events.Unsubscribe(throwing));

		// The failed activation did not revoke the grant or leave a
		// stuck in-progress flag. A fresh activation, after the
		// throwing listener is gone, starts from step zero.
		GameplayAbilityActivationResult retry =
			runtime.Abilities.TryActivate(handle);

		Assert.True(retry.Succeeded);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			30f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));

		Assert.Contains(
			GameplayAbilityChangeKind.Cancelled,
			abilities.Changes.Select(change => change.Kind));

		Assert.Contains(
			GameplayAbilityChangeKind.Activated,
			abilities.Changes.Select(change => change.Kind));
	}

	private static GameplayEventSubscription ThrowOnDamage(
		AbilityRuntime runtime)
	{
		return runtime.Events.Subscribe(
			EventSchema.DamageReceived,
			_ => throw new InvalidOperationException(
				"plan publish failed"));
	}
}
