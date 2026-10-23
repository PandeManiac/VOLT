using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers registered custom tasks: completion, cancellation, reuse,
/// slot recycling, exceptions, and definition scoping.
/// </summary>
public sealed class GameplayAbilityCustomTaskTests
{
	private static GameplayAbilityDefinition Plan(
		string name,
		GameplayAbilityTaskDefinition definition)
	{
		return GameplayAbilityDefinition
			.Create(name)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitCustom(definition)
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.End()
					.Build())
			.Build();
	}

	private static GameplayAbilityTaskHandle Pending(
		GameplayAbilityTaskSystem tasks,
		Entity owner)
	{
		GameplayAbilityTaskHandle[] handles =
			new GameplayAbilityTaskHandle[tasks.GetPendingTaskCount(owner)];

		int count = tasks.CopyTasks(owner, handles);

		Assert.Equal(1, count);

		return handles[0];
	}

	[Fact]
	public void CustomTask_CompletesThePlanWhenApplicationCodeSignalsIt()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		AbilityRecorder recorder = new(runtime.Abilities);
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Interaction");

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Complete", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));

		GameplayAbilityTaskHandle task = Pending(runtime.Tasks, actor);

		Assert.True(runtime.Tasks.TryGetTask(task, out GameplayAbilityTaskInfo info));
		Assert.Equal(GameplayAbilityTaskKind.Custom, info.Kind);
		Assert.Same(definition, info.CustomDefinition);
		Assert.Equal(0, info.PlanStep);

		Assert.True(runtime.Tasks.Complete(task));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Tasks.TryGetTask(task, out _));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
		Assert.Equal(1, recorder.Count(GameplayAbilityChangeKind.Ended));
	}

	[Fact]
	public void OneDefinition_CanOwnSeveralTasksInOneActivation()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("SeveralInstances");
		GameplayAbilityActivation activation = TaskTestSupport.ActivateChannel(
			runtime,
			actor,
			out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitCustom(activation, definition);
		GameplayAbilityTaskHandle second =
			runtime.Tasks.WaitCustom(activation, definition);

		Assert.NotEqual(first, second);
		Assert.Equal(2, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(first, out GameplayAbilityTaskInfo firstInfo));
		Assert.True(runtime.Tasks.TryGetTask(second, out GameplayAbilityTaskInfo secondInfo));
		Assert.Same(definition, firstInfo.CustomDefinition);
		Assert.Same(definition, secondInfo.CustomDefinition);

		Assert.True(runtime.Tasks.Complete(first));
		Assert.False(runtime.Tasks.TryGetTask(first, out _));
		Assert.True(runtime.Tasks.TryGetTask(second, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.Complete(second));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void CustomTask_StartedHandlerExceptionLeavesAReleasableTask()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("StartHandlerThrows");
		GameplayAbilityActivation activation = TaskTestSupport.ActivateChannel(
			runtime,
			actor,
			out _);
		GameplayAbilityTaskHandle started = GameplayAbilityTaskHandle.None;

		void ThrowAfterCapturingStartedTask(GameplayAbilityTaskChange change)
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Started)
			{
				started = change.Handle;
				throw new InvalidOperationException("start handler failed");
			}
		}

		runtime.Tasks.Changed += ThrowAfterCapturingStartedTask;

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.WaitCustom(activation, definition));

		runtime.Tasks.Changed -= ThrowAfterCapturingStartedTask;

		Assert.False(started.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.TryGetTask(started, out GameplayAbilityTaskInfo info));
		Assert.Equal(GameplayAbilityTaskKind.Custom, info.Kind);
		Assert.Same(definition, info.CustomDefinition);
		Assert.True(runtime.Tasks.Cancel(started));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void CustomTask_OwnerIndexRecyclingCannotCompleteTheReplacement()
	{
		using AbilityRuntime runtime = new();
		Entity owner = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("RecycledOwner");
		GameplayAbilityHandle firstAbility = runtime.Abilities.GiveAbility(
			owner,
			Plan("Test.Custom.FirstOwner", definition));

		Assert.True(runtime.Abilities.TryActivate(firstAbility).Succeeded);
		GameplayAbilityTaskHandle stale = Pending(runtime.Tasks, owner);

		runtime.World.DestroyEntity(owner);
		Entity replacement = runtime.CreateActor();

		Assert.Equal(owner.Index, replacement.Index);
		Assert.NotEqual(owner.Generation, replacement.Generation);

		GameplayAbilityHandle replacementAbility = runtime.Abilities.GiveAbility(
			replacement,
			Plan("Test.Custom.ReplacementOwner", definition));

		Assert.True(runtime.Abilities.TryActivate(replacementAbility).Succeeded);
		GameplayAbilityTaskHandle current = Pending(runtime.Tasks, replacement);

		Assert.NotEqual(stale, current);
		Assert.False(runtime.Tasks.Complete(stale));
		Assert.True(runtime.Tasks.TryGetTask(current, out _));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.Complete(current));
		Assert.True(runtime.Tags.HasTag(replacement, TagSchema.EffectBuff));
	}

	[Fact]
	public void RevokingTheGrant_ReleasesTheCustomTask()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Revoked");
		GameplayAbilityHandle ability = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Revoke", definition));
		List<GameplayAbilityTaskChange> changes = [];
		runtime.Tasks.Changed += changes.Add;

		Assert.True(runtime.Abilities.TryActivate(ability).Succeeded);
		GameplayAbilityTaskHandle task = Pending(runtime.Tasks, actor);

		Assert.True(runtime.Abilities.RevokeAbility(ability));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.TryGetAbility(ability, out _));
		Assert.False(runtime.Tasks.Complete(task));
		Assert.Contains(
			changes,
			change => change.Handle == task
				&& change.Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& change.Reason == GameplayAbilityTaskCompletionReason.AbilityEnded);
	}

	[Fact]
	public void CustomTask_DoesNotCompleteWithoutASignal()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Dormant");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Dormant", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Tasks.Advance(60f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void CustomTask_CanCompleteFromItsStartedNotification()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Immediate");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Immediate", definition));

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Started)
			{
				Assert.True(runtime.Tasks.Complete(change.Handle));
			}
		};

		// The plan finished inside the Started notification, while the
		// activation call was still running, which the activation
		// status reports explicitly.
		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void CancellingTheAbility_ReleasesTheCustomTask()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		AbilityRecorder recorder = new(runtime.Abilities);
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Interrupted");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Cancel", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.True(runtime.Abilities.Cancel(handle));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.False(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
		Assert.Equal(1, recorder.Count(GameplayAbilityChangeKind.Cancelled));
	}

	[Fact]
	public void CancellationFromTheStartedNotification_IsSafe()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("CancelOnStart");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.CancelOnStart", definition));

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Started)
			{
				Assert.True(runtime.Abilities.Cancel(handle));
			}
		};

		Assert.Equal(
			GameplayAbilityActivationStatus.EndedDuringActivation,
			runtime.Abilities.TryActivate(handle).Status);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void DestroyingTheOwner_ReleasesTheCustomTaskSilently()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("DestroyedOwner");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.DestroyedOwner", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.World.DestroyEntity(actor);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void CustomTask_ReleasesStateWhenACompletionHandlerThrows()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("ThrowingHandler");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.ThrowingHandler", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		GameplayAbilityTaskHandle task = Pending(runtime.Tasks, actor);

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed)
			{
				throw new InvalidOperationException("handler failed");
			}
		};

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Complete(task));

		// The task and the plan were released before the notification
		// propagated, so the failure left no pending state behind.
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void CustomTask_RetriggersAfterThePlanEnds()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Retriggerable");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Retrigger", definition));

		for (int index = 0; index < 3; index++)
		{
			Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

			GameplayAbilityTaskHandle task = Pending(runtime.Tasks, actor);

			Assert.True(runtime.Tasks.Complete(task));
			Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		}
	}

	[Fact]
	public void OneDefinition_ServesIndependentActivations()
	{
		using AbilityRuntime runtime = new();
		Entity first = runtime.CreateActor();
		Entity second = runtime.CreateActor();
		GameplayAbilityDefinition definition = Plan(
			"Test.Custom.Shared",
			runtime.Abilities.RegisterTaskDefinition("SharedInteraction"));

		GameplayAbilityHandle firstHandle =
			runtime.Abilities.GiveAbility(first, definition);
		GameplayAbilityHandle secondHandle =
			runtime.Abilities.GiveAbility(second, definition);

		Assert.True(runtime.Abilities.TryActivate(firstHandle).Succeeded);
		Assert.True(runtime.Abilities.TryActivate(secondHandle).Succeeded);
		Assert.Equal(2, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Tasks.Complete(Pending(runtime.Tasks, first)));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.IsActive(secondHandle));
		Assert.True(runtime.Tags.HasTag(first, TagSchema.EffectBuff));
		Assert.False(runtime.Tags.HasTag(second, TagSchema.EffectBuff));
	}

	[Fact]
	public void CustomTask_RejectsARecycledSlotHandle()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Recycled");
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Recycled", definition));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		GameplayAbilityTaskHandle stale = Pending(runtime.Tasks, actor);

		Assert.True(runtime.Tasks.Complete(stale));
		Assert.False(runtime.Tasks.Complete(stale));
		Assert.False(runtime.Tasks.Cancel(stale));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		GameplayAbilityTaskHandle current = Pending(runtime.Tasks, actor);

		Assert.NotEqual(stale, current);
		Assert.False(runtime.Tasks.Complete(stale));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Tasks.Complete(current));
	}

	[Fact]
	public void CustomTask_RejectsADefinitionFromAnotherAbilitySystem()
	{
		using AbilityRuntime first = new();
		using AbilityRuntime second = new();
		Entity actor = first.CreateActor();
		GameplayAbilityTaskDefinition foreign =
			second.Abilities.RegisterTaskDefinition("Foreign");

		GameplayAbilityTaskDefinition local =
			first.Abilities.RegisterTaskDefinition("Local");
		GameplayAbilityHandle handle = first.Abilities.GiveAbility(
			actor,
			Plan("Test.Custom.Foreign", local));

		GameplayAbilityActivation activation =
			first.Abilities.TryActivate(handle).Activation;

		Assert.False(activation.IsNone);

		Assert.Throws<ArgumentException>(
			() => first.Tasks.WaitCustom(activation, foreign));
	}

	[Fact]
	public void Registration_RequiresUniqueNames()
	{
		using AbilityRuntime runtime = new();
		GameplayAbilityTaskDefinition definition =
			runtime.Abilities.RegisterTaskDefinition("Unique");

		Assert.Equal("Unique", definition.Name);
		Assert.Equal(1, definition.Id);
		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.RegisterTaskDefinition("Unique"));
		Assert.Throws<ArgumentException>(
			() => runtime.Abilities.RegisterTaskDefinition(" "));
		Assert.Equal(
			2,
			runtime.Abilities.RegisterTaskDefinition("Second").Id);
	}

	[Fact]
	public void Registration_FailsAfterDisposal()
	{
		AbilityRuntime runtime = new();

		runtime.Abilities.RegisterTaskDefinition("BeforeDispose");
		runtime.Abilities.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => runtime.Abilities.RegisterTaskDefinition("AfterDispose"));

		runtime.Dispose();
	}
}
