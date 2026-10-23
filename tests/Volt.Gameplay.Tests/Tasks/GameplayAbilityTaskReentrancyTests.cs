using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Events;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers callbacks that end, reactivate, revoke, or start work from
/// inside task delivery.
/// </summary>
public sealed class GameplayAbilityTaskReentrancyTests
{
	[Fact]
	public void TaskCompletion_CanEndTheOwningAbility()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 0.5f);

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == task)
			{
				Assert.True(runtime.Abilities.End(handle));
			}
		};

		runtime.Tasks.Advance(0.5f);

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(1, tasks.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal("Granted,Activated,Ended", abilities.Sequence());
	}

	[Fact]
	public void TaskCompletion_CanReactivateTheAbility()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation first =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(first, 0.5f);

		GameplayAbilityActivationResult reactivated = default;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == task)
			{
				Assert.True(runtime.Abilities.End(handle));
				reactivated = runtime.Abilities.TryActivate(handle);
			}
		};

		runtime.Tasks.Advance(0.5f);

		Assert.True(reactivated.Succeeded);
		Assert.NotEqual(first, reactivated.Activation);
		Assert.True(runtime.Abilities.IsActive(handle));
		Assert.False(runtime.Tasks.TryGetTask(task, out _));

		GameplayAbilityTaskHandle replacement = runtime.Tasks.WaitDuration(
			reactivated.Activation,
			1f);

		Assert.False(replacement.IsNone);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void TaskCancellation_CannotReactivateDuringTeardown()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 10f);

		GameplayAbilityActivationResult nested = default;
		bool attempted = false;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& change.Handle == task)
			{
				attempted = true;
				nested = runtime.Abilities.TryActivate(handle);
			}
		};

		Assert.True(runtime.Abilities.End(handle));

		Assert.True(attempted);
		Assert.Equal(
			GameplayAbilityActivationStatus.AlreadyActive,
			nested.Status);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void TaskCancellation_CannotRevokeDuringTeardown()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle task =
			runtime.Tasks.WaitDuration(activation, 10f);

		bool revokeResult = true;
		bool attempted = false;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& change.Handle == task)
			{
				attempted = true;
				revokeResult = runtime.Abilities.RevokeAbility(handle);
			}
		};

		Assert.True(runtime.Abilities.End(handle));

		Assert.True(attempted);
		Assert.False(revokeResult);
		Assert.True(runtime.Abilities.TryGetAbility(handle, out _));
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal("Granted,Activated,Ended", abilities.Sequence());

		Assert.True(runtime.Abilities.RevokeAbility(handle));
	}

	[Fact]
	public void TaskCompletion_ThrowingHandlerLeavesNoLeak()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitDuration(activation, 0.5f);

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed)
			{
				throw new InvalidOperationException("task handler failed");
			}
		};

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.Advance(0.5f));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.True(runtime.Abilities.IsActive(handle));

		// The advance guard is released, so the runtime keeps working.
		runtime.Tasks.Advance(1f);
		Assert.True(runtime.Abilities.End(handle));
	}

	[Fact]
	public void TaskCancellation_ThrowingHandlerStillEndsTheAbility()
	{
		using AbilityRuntime runtime = new();
		AbilityRecorder abilities = new(runtime.Abilities);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskSystem tasks = runtime.Tasks;

		tasks.WaitDuration(activation, 10f);

		tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Cancelled)
			{
				throw new InvalidOperationException(
					"cancellation handler failed");
			}
		};

		Assert.Throws<InvalidOperationException>(
			() => runtime.Abilities.End(handle));

		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal("Granted,Activated,Ended", abilities.Sequence());

		// The ending guard was released even though the handler failed.
		Assert.False(runtime.Abilities.End(handle));
		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
	}

	[Fact]
	public void EventHandler_CanCancelTheAbilityBeforeTheTaskRuns()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		runtime.Tasks.WaitEvent(activation, EventSchema.AttackHit);

		runtime.Events.Subscribe(EventSchema.AttackHit, _ =>
		{
			Assert.True(runtime.Abilities.Cancel(handle));
		});

		Assert.Equal(1, runtime.Events.Publish(new GameplayEvent(
			EventSchema.AttackHit)));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			0,
			tasks.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(
			1,
			tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void CompletingOneTask_CanCancelItsSiblings()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out GameplayAbilityHandle handle);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 0.5f);

		runtime.Tasks.WaitDuration(activation, 5f);

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				Assert.True(runtime.Abilities.End(handle));
			}
		};

		runtime.Tasks.Advance(0.5f);

		Assert.Equal(1, tasks.Count(GameplayAbilityTaskChangeKind.Completed));
		Assert.Equal(1, tasks.Count(GameplayAbilityTaskChangeKind.Cancelled));
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void CompletionHandler_CanRecycleTaskSlotsSafely()
	{
		using AbilityRuntime runtime = new();
		TaskRecorder tasks = new(runtime.Tasks);
		Entity actor = runtime.CreateActor();

		GameplayAbilityActivation activation =
			TaskTestSupport.ActivateChannel(
				runtime,
				actor,
				out _);

		GameplayAbilityTaskHandle first =
			runtime.Tasks.WaitDuration(activation, 0.5f);
		GameplayAbilityTaskHandle second =
			runtime.Tasks.WaitDuration(activation, 5f);

		GameplayAbilityTaskHandle replacement =
			GameplayAbilityTaskHandle.None;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed
				&& change.Handle == first)
			{
				Assert.True(runtime.Tasks.Cancel(second));

				replacement = runtime.Tasks.WaitDuration(
					activation,
					2f);
			}
		};

		runtime.Tasks.Advance(0.5f);

		Assert.False(replacement.IsNone);
		Assert.NotEqual(second, replacement);
		Assert.False(runtime.Tasks.Cancel(second));
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(2f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(
			replacement,
			tasks.Last(GameplayAbilityTaskChangeKind.Completed).Handle);
	}
}
