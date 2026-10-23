using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Simulation;

/// <summary>
/// Covers the authored features under the automatic fixed-step
/// integration: composite waits, custom tasks, branching, and ordering
/// across the effects and task phases of one step.
/// </summary>
public sealed class GameplaySimulationAuthoringTests
{
	private static readonly GameplayAbilityDefinition WaitAnyBuff =
		GameplayAbilityDefinition
			.Create("Test.Simulation.WaitAnyBuff")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitAny(
						GameplayWaitAny
							.Create()
							.WithTimeout(1.5f / 60f))
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.End()
					.Build())
			.Build();

	private static readonly GameplayAbilityDefinition GuardWait =
		GameplayAbilityDefinition
			.Create("Test.Simulation.GuardWait")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.ApplyEffect(EffectSchema.Stun)
					.WaitAny(
						GameplayWaitAny
							.Create()
							.WithTimeout(5f)
							.OnTag(
								TagSchema.StatusStunned,
								GameplayTagWaitMode.Present))
					.End()
					.Build())
			.Build();

	[Fact]
	public void WaitAnyTimeout_AdvancesWithTheFixedDelta()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, WaitAnyBuff);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Step();

		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Step();

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void TagSource_ArmedByAnEffectExpiry_CompletesInTheSameStep()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<GameplayAbilityTaskChange> changes = [];

		runtime.Tasks.Changed += changes.Add;

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, GuardWait);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.StatusStunned));

		FrameTiming frame = runtime.Step();

		Assert.Equal(1, frame.FixedSteps);

		// The effect phase removed the tag, and the task phase
		// completed the race that watched it, all in one step. The
		// five second timeout did not win.
		GameplayAbilityTaskChange completed = changes.Single(
			change => change.Kind == GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(
			GameplayAbilityTaskCompletionReason.ConditionMet,
			completed.Reason);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void CustomTask_CompletedInAFixedUpdate_ContinuesThePlanInThatStep()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("SimulationSwing");

		GameplayAbilityDefinition plan = GameplayAbilityDefinition
			.Create("Test.Simulation.CustomTask")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitCustom(definition)
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, plan);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		// The Started notification already ran during activation, so
		// capture the current task from the owner list instead.
		GameplayAbilityTaskHandle[] handles =
			new GameplayAbilityTaskHandle[runtime.Tasks.GetPendingTaskCount(actor)];

		Assert.Equal(1, runtime.Tasks.CopyTasks(actor, handles));

		runtime.Loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (runtime.Tasks.Complete(handles[0]))
			{
				handles[0] = GameplayAbilityTaskHandle.None;
			}
		});

		runtime.Step();

		// The fixed update completed the task, the plan continued
		// immediately, and the buff was applied in the same step.
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void PausedLoop_DoesNotAdvanceCompositeWaits()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, WaitAnyBuff);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Loop.Pause();

		for (int index = 0; index < 10; index++)
		{
			runtime.Step();
		}

		Assert.Equal(0d, runtime.Tasks.Time, 9);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.IsActive(handle));

		Assert.True(runtime.Loop.StepOnce());
		runtime.Step();

		Assert.Equal(
			runtime.Loop.FixedDeltaSeconds,
			runtime.Tasks.Time,
			9);
		Assert.True(runtime.Abilities.IsActive(handle));

		runtime.Loop.Resume();
		runtime.Step();

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void TimeScaledFrame_RunsMultipleStepsAndCompletesTheWait()
	{
		using GameplaySimulationRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, WaitAnyBuff);

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Loop.TimeScale = TimeScale.FromSingle(2f);

		FrameTiming frame = runtime.Step();

		// The wait's 1.5-step timeout is reached by the second fixed
		// step of the same rendered frame.
		Assert.Equal(2, frame.FixedSteps);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}
}
