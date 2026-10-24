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
/// Covers the ergonomic plan layer, which reuses the task runtime
/// rather than duplicating execution.
/// </summary>
public sealed class GameplayAbilityPlanTests
{
	private static readonly GameplayEffectDefinition PlanEffect =
		GameplayEffectDefinition
			.Create("Test.Plan.Apply")
			.Instant()
			.Modifier(
				AttributeSchema.Stamina,
				GameplayModifierOperation.AddBase,
				-7f)
			.Build();

	private static readonly GameplayAbilityDefinition PlanDash =
		GameplayAbilityDefinition
			.Create("Test.Plan.Dash")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.25f)
					.ApplyEffect(PlanEffect)
					.WaitEvent(EventSchema.AttackHit)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition PlanInterrupted =
		GameplayAbilityDefinition
			.Create("Test.Plan.Interrupted")
			.AssetTags(AbilitySchema.AbilityFree)
			.Cost(AbilitySchema.CostTen)
			.Cooldown(AbilitySchema.DashCooldown)
			.CancelOnTags(TagSchema.StatusStunned)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.25f)
					.ApplyEffect(PlanEffect)
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition PlanPublish =
		GameplayAbilityDefinition
			.Create("Test.Plan.Publish")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.PublishEvent(EventSchema.AttackHit)
					.Wait(0.1f)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition PlanTag =
		GameplayAbilityDefinition
			.Create("Test.Plan.Tag")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitTag(TagSchema.StatusBurning, GameplayTagWaitMode.Present)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition PlanEmpty =
		GameplayAbilityDefinition
			.Create("Test.Plan.Empty")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(GameplayAbilityPlan.Create().Build())
			.Build();

	private static readonly GameplayAbilityDefinition PlanRetrigger =
		GameplayAbilityDefinition
			.Create("Test.Plan.Retrigger")
			.AssetTags(AbilitySchema.AbilityFree)
			.Retrigger(true)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(0.5f)
					.ApplyEffect(PlanEffect)
					.End()
					.Build())
			.Build();

	[Fact]
	public void Plan_WaitsAppliesWaitsForAnEventAndEnds()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanDash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.Activated,
			result.Status);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(0.24f);

		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));

		runtime.Tasks.Advance(0.02f);

		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(
			33f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.Equal(1, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal("Granted,Activated,Ended", abilities.Sequence());
		Assert.Equal(
			2,
			tasks.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(
			0,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));
	}

	[Fact]
	public void Plan_InterruptionCancelsPendingTasksAndKeepsCommittedCost()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanInterrupted);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(result.Succeeded);
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.True(runtime.Abilities.IsOnCooldown(
			actor,
			PlanInterrupted));

		runtime.Tasks.Advance(0.1f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Effects.Apply(actor, EffectSchema.Stun).Applied);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.AbilityEnded,
			tasks.Last(GameplayAbilityTaskChangeKind.Cancelled).Reason);

		runtime.Tasks.Advance(10f);

		// The wind-up effect never ran, and the already committed cost
		// and cooldown were not rolled back by the cancellation.
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.True(runtime.Abilities.IsOnCooldown(
			actor,
			PlanInterrupted));
		Assert.Equal("Granted,Activated,Cancelled", abilities.Sequence());
	}

	[Fact]
	public void Plan_PublishStepRunsWhileTheAbilityIsActive()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		Entity target = runtime.CreateActor();

		GameplayEvent received = default;
		int calls = 0;

		runtime.Events.Subscribe(EventSchema.AttackHit, gameplayEvent =>
		{
			calls++;
			received = gameplayEvent;

			Assert.True(runtime.Abilities.ActiveAbilityCount == 1);
		});

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanPublish);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle, target);

		Assert.Equal(
			GameplayAbilityActivationStatus.Activated,
			result.Status);
		Assert.Equal(1, calls);
		Assert.Equal(EventSchema.AttackHit, received.Tag);
		Assert.Equal(actor, received.Source);
		Assert.Equal(target, received.Target);
		Assert.True(runtime.Abilities.IsActive(handle));

		runtime.Tasks.Advance(0.1f);

		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void Plan_WaitsForATagCondition()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BurnTenSeconds).Applied);

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanTag);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.IsActive(handle));

		runtime.Tasks.Advance(0.016f);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Completed));
	}

	[Fact]
	public void EmptyPlan_EndsAsSoonAsItIsActivated()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanEmpty);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.Equal(
			GameplayAbilityActivationStatus.ActivatedAndEnded,
			result.Status);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Empty(tasks.Changes);
	}

	[Fact]
	public void Retrigger_RestartsThePlanFromItsFirstStep()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanRetrigger);

		GameplayAbilityActivationResult first =
			runtime.Abilities.TryActivate(handle);

		Assert.True(first.Succeeded);

		runtime.Tasks.Advance(0.4f);

		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));

		GameplayAbilityActivationResult second =
			runtime.Abilities.TryActivate(handle);

		Assert.True(second.Succeeded);
		Assert.NotEqual(first.Activation, second.Activation);
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));

		runtime.Tasks.Advance(0.4f);

		// The new plan started at the retrigger, so its 0.5 second wait
		// has not elapsed yet.
		Assert.Equal(
			50f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.True(runtime.Abilities.IsActive(handle));

		runtime.Tasks.Advance(0.11f);

		Assert.Equal(
			43f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void CancellingAPlanTask_EndsTheAbilityInsteadOfStalling()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityTaskHandle planTask =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Started
				&& change.PlanStep == 0)
			{
				planTask = change.Handle;
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanDash);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.False(planTask.IsNone);

		Assert.True(runtime.Tasks.Cancel(planTask));

		// A plan cannot continue after its step was cancelled, so the
		// ability ends instead of waiting forever without a task.
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));
	}

	[Fact]
	public void PlanStartedHandler_CancellingAllTasksDoesNotLeaveAPlanStuck()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		bool cancelledFromStart = false;

		runtime.Tasks.Changed += change =>
		{
			if (!cancelledFromStart
				&& change.Kind == GameplayAbilityTaskChangeKind.Started
				&& change.PlanStep == 0)
			{
				cancelledFromStart = true;
				runtime.Tasks.CancelAll(actor);
			}
		};

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, PlanDash);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(cancelledFromStart);
		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			result.Status);

		// The activation itself was committed, but the plan step was
		// cancelled during its Started notification. The ability ends
		// with the activation rather than staying active without work.
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			40f,
			runtime.Attributes.GetCurrentValue(
				actor,
				AttributeSchema.Stamina));
	}

	[Fact]
	public void PlanBuilder_RejectsInvalidSteps()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayAbilityPlan.Create().Wait(-1f));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayAbilityPlan.Create().Wait(float.NaN));
		Assert.Throws<ArgumentNullException>(
			() => GameplayAbilityPlan.Create().ApplyEffect(null!));
		Assert.Throws<ArgumentException>(
			() => GameplayAbilityPlan.Create().WaitTag(
				GameplayTag.None,
				GameplayTagWaitMode.Present));

		GameplayAbilityPlanBuilder tooLong = GameplayAbilityPlan.Create();

		for (int index = 0; index <= GameplayAbilityPlan.MaximumStepCount; index++)
		{
			tooLong.Wait(0.1f);
		}

		Assert.Throws<InvalidOperationException>(() => tooLong.Build());
	}

	[Fact]
	public void PlanDefinition_RejectsEndingOnActivate()
	{
		GameplayAbilityPlan plan = GameplayAbilityPlan
			.Create()
			.Wait(0.1f)
			.Build();

		Assert.Throws<InvalidOperationException>(
			() => GameplayAbilityDefinition
				.Create("Test.Plan.Conflict")
				.Plan(plan)
				.EndOnActivate(true)
				.Build());
	}
}
