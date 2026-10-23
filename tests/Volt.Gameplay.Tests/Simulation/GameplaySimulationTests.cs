using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Simulation;

/// <summary>
/// Covers automatic fixed-step advancement, update ordering, pause,
/// time scaling, catch-up, double-advance rejection, and teardown.
/// </summary>
public sealed class GameplaySimulationTests
{
	private const float FixedDelta = 1f / 60f;

	private static readonly GameplayEffectDefinition OneStepTag =
		GameplayEffectDefinition
			.Create("Test.Simulation.OneStepTag")
			.HasDuration(FixedDelta)
			.GrantTags(TagSchema.StatusBurning)
			.Build();

	private static readonly GameplayAbilityDefinition WaitForTagAbsent =
		GameplayAbilityDefinition
			.Create("Test.Simulation.WaitForTagAbsent")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitTag(
						TagSchema.StatusBurning,
						GameplayTagWaitMode.Absent)
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition WaitOneStep =
		GameplayAbilityDefinition
			.Create("Test.Simulation.WaitOneStep")
			.AssetTags(AbilitySchema.AbilityFree)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(FixedDelta)
					.PublishEvent(EventSchema.DamageReceived)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition WaitOneSecond =
		GameplayAbilityDefinition
			.Create("Test.Simulation.WaitOneSecond")
			.AssetTags(AbilitySchema.AbilityFree)
			.EndOnActivate(false)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.Wait(1f)
					.End()
					.Build())
			.Build();

	[Fact]
	public void FixedStep_AdvancesEffectsAndTasksExactlyOnce()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			WaitOneSecond);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		FrameTiming frame = runtime.Step();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, runtime.Simulation.FixedStepCount);
		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Effects.Time,
			9);
		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Tasks.Time,
			9);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Effects.Advance(1f));
		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Advance(1f));
	}

	[Fact]
	public void EffectExpiryAndTagWait_CompleteInTheSameFixedStep()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<string> order = [];
		bool eventReceived = false;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed)
			{
				order.Add("task completed");
			}
		};

		runtime.Effects.Changed += change =>
		{
			if (change.Kind == GameplayEffectChangeKind.Expired)
			{
				order.Add("effect expired");
			}
		};

		runtime.Events.Subscribe(EventSchema.DamageReceived, _ =>
		{
			eventReceived = true;
			order.Add("plan event");
		});

		runtime.Loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (context.StepIndex != 1)
			{
				return;
			}

			order.Add("fixed");

			Assert.True(runtime.Effects.Apply(actor, OneStepTag).Applied);

			GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
				actor,
				WaitForTagAbsent);

			Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		});

		runtime.Loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext _) => order.Add("post"));

		runtime.Step();

		Assert.True(eventReceived);
		Assert.Equal(
			["fixed", "effect expired", "plan event", "task completed", "post"],
			order);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void TaskStartedByGameFixedUpdate_CompletesInTheSameStep()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<string> order = [];

		runtime.Events.Subscribe(
			EventSchema.DamageReceived,
			_ => order.Add("plan event"));

		runtime.Loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (context.StepIndex != 1)
			{
				return;
			}

			order.Add("fixed");

			GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
				actor,
				WaitOneStep);

			Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		});

		runtime.Loop.RegisterPostFixedUpdate(
			(in PostFixedUpdateContext _) => order.Add("post"));

		runtime.Step();

		Assert.Equal(["fixed", "plan event", "post"], order);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void PausedLoop_DoesNotAdvanceGameplay()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			WaitOneSecond);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		runtime.Step();

		double effectsTime = runtime.Effects.Time;
		double tasksTime = runtime.Tasks.Time;

		runtime.Loop.Pause();

		runtime.Clock.AdvanceNanoseconds(5_000_000_000);

		FrameTiming paused = runtime.Loop.Advance();

		Assert.True(paused.IsPaused);
		Assert.Equal(0, paused.FixedSteps);
		Assert.Equal(effectsTime, runtime.Effects.Time);
		Assert.Equal(tasksTime, runtime.Tasks.Time);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Loop.StepOnce());

		runtime.Clock.AdvanceNanoseconds(1_000_000_000);

		FrameTiming stepped = runtime.Loop.Advance();

		Assert.Equal(1, stepped.FixedSteps);
		Assert.Equal(
			effectsTime + runtime.Loop.FixedDeltaSeconds,
			runtime.Effects.Time,
			9);
		Assert.Equal(
			tasksTime + runtime.Loop.FixedDeltaSeconds,
			runtime.Tasks.Time,
			9);
	}

	[Fact]
	public void RenderOnlyFrame_DoesNotAdvanceGameplay()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		double time = runtime.Effects.Time;

		runtime.Clock.AdvanceNanoseconds(5_000_000);

		FrameTiming frame = runtime.Loop.Advance();

		Assert.Equal(0, frame.FixedSteps);
		Assert.Equal(time, runtime.Effects.Time);
		Assert.Equal(0, runtime.Simulation.FixedStepCount);
	}

	[Fact]
	public void TimeScale_ChangesSimulationProgressNotTheFixedDelta()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		runtime.Loop.TimeScale = TimeScale.FromSingle(0.5f);

		runtime.Step(120);

		Assert.InRange(runtime.Loop.TotalFixedSteps, 59, 60);
		Assert.InRange(runtime.Effects.Time, 0.98, 1.02);
		Assert.Equal(
			1f / 60f,
			runtime.Loop.FixedDeltaSeconds,
			9);
	}

	[Fact]
	public void CatchUpSteps_AdvanceGameplayForEveryExecutedStep()
	{
		using GameplaySimulationRuntime runtime = new();

		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		runtime.Clock.AdvanceNanoseconds(
			3 * GameplaySimulationRuntime.FrameNanoseconds);

		FrameTiming frame = runtime.Loop.Advance();

		Assert.Equal(3, frame.FixedSteps);
		Assert.Equal(3, runtime.Simulation.FixedStepCount);
		Assert.Equal(
			3d * runtime.Loop.FixedDeltaSeconds,
			runtime.Effects.Time,
			9);
	}

	[Fact]
	public void OneStepEffectExpiresOnItsExactBoundary()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(actor, OneStepTag).Applied);
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.StatusBurning));
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);

		runtime.Step();

		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.StatusBurning));
	}

	[Fact]
	public void DisposingTheIntegration_ReturnsToManualAdvance()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			WaitOneSecond);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		double effectsTime = runtime.Effects.Time;
		double tasksTime = runtime.Tasks.Time;

		runtime.Simulation.Dispose();

		Assert.False(runtime.Simulation.IsAttached);

		// Disposal is idempotent.
		runtime.Simulation.Dispose();

		runtime.Effects.Advance(0.25f);
		runtime.Tasks.Advance(0.25f);

		Assert.Equal(effectsTime + 0.25, runtime.Effects.Time, 9);
		Assert.Equal(tasksTime + 0.25, runtime.Tasks.Time, 9);

		runtime.Step();

		Assert.Equal(effectsTime + 0.25, runtime.Effects.Time, 9);
		Assert.Equal(tasksTime + 0.25, runtime.Tasks.Time, 9);
	}

	[Fact]
	public void SecondIntegration_IsRejectedAndLeavesTheFirstWorking()
	{
		using GameplaySimulationRuntime runtime = new();

		Assert.Throws<InvalidOperationException>(
			() => new GameplaySimulation(
				runtime.Loop,
				runtime.Abilities,
				runtime.Effects));

		runtime.Step();

		Assert.Equal(1, runtime.Simulation.FixedStepCount);
		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Effects.Time,
			9);
	}

	[Fact]
	public void EntityDestructionDuringFixedStep_ReleasesGameplayState()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity victim = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			victim,
			EffectSchema.BuffFiveSeconds).Applied);

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			victim,
			WaitOneSecond);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (context.StepIndex == 1)
			{
				Assert.True(runtime.World.DestroyEntity(victim));
			}
		});

		runtime.Step();

		Assert.False(runtime.World.IsAlive(victim));
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Abilities.GrantedAbilityCount);
	}

	[Fact]
	public void ZeroTimeScale_FreezesGameplayWithoutPausingTheLoop()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.True(runtime.Effects.Apply(
			actor,
			EffectSchema.BuffFiveSeconds).Applied);

		runtime.Loop.TimeScale = TimeScale.Zero;
		runtime.Step(10);

		Assert.Equal(0, runtime.Loop.TotalFixedSteps);
		Assert.Equal(0, runtime.Effects.Time);
		Assert.Equal(0, runtime.Simulation.FixedStepCount);
	}
}
