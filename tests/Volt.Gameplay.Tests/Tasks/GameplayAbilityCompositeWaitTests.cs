using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;
using Volt.Gameplay.Tests.Effects;
using Volt.Gameplay.Tests.Events;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Tasks;

/// <summary>
/// Covers composite Any waits: timeouts, event sets, tag conditions,
/// race winners, cleanup, and filtering.
/// </summary>
public sealed class GameplayAbilityCompositeWaitTests
{
	private static GameplayAbilityDefinition Plan(
		string name,
		GameplayWaitAny wait)
	{
		return GameplayAbilityDefinition
			.Create(name)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitAny(wait)
					.ApplyEffect(EffectSchema.BuffFiveSeconds)
					.End()
					.Build())
			.Build();
	}

	private static List<GameplayAbilityTaskChange> Record(
		GameplayAbilityTaskSystem tasks)
	{
		List<GameplayAbilityTaskChange> changes = [];

		tasks.Changed += changes.Add;

		return changes;
	}

	[Fact]
	public void TimeoutOnly_CompletesWhenTheTimeoutElapses()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<GameplayAbilityTaskChange> changes = Record(runtime.Tasks);
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Timeout",
				GameplayWaitAny.Create().WithTimeout(1f)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Tasks.Advance(0.5f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(0.5f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));

		GameplayAbilityTaskChange completed = changes.Single(
			change => change.Kind == GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(GameplayAbilityTaskKind.Any, completed.TaskKind);
		Assert.Equal(
			GameplayAbilityTaskCompletionReason.Elapsed,
			completed.Reason);
	}

	[Fact]
	public void ZeroTimeout_CompletesOnTheNextAdvance()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.ZeroTimeout",
				GameplayWaitAny.Create().WithTimeout(0f)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(0.1f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void EventOnly_CompletesOnTheWatchedEvent()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<GameplayAbilityTaskChange> changes = Record(runtime.Tasks);
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Event",
				GameplayWaitAny
					.Create()
					.OnEvent(EventSchema.DamageReceived)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Events.SubscriptionCount);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.Unrelated,
			actor,
			actor));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived,
			actor,
			actor));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);
		Assert.False(runtime.Abilities.IsActive(handle));

		GameplayAbilityTaskChange completed = changes.Single(
			change => change.Kind == GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(
			GameplayAbilityTaskCompletionReason.ConditionMet,
			completed.Reason);
		Assert.Equal(EventSchema.DamageReceived, completed.Event.Tag);
	}

	[Fact]
	public void MultipleEvents_CompleteOnAnyWatchedTag()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.MultipleEvents",
				GameplayWaitAny
					.Create()
					.OnEvent(EventSchema.Damage)
					.OnEvent(EventSchema.Unrelated)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		// Attack shares the common ancestor but is not watched.
		runtime.Events.Publish(new GameplayEvent(
			EventSchema.Attack,
			actor,
			actor));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.Unrelated,
			actor,
			actor));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
	}

	[Fact]
	public void HierarchicalEventSource_AcceptsDescendants()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Hierarchical",
				GameplayWaitAny
					.Create()
					.OnEvent(
						EventSchema.Damage,
						GameplayEventMatch.Hierarchical)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.Attack,
			actor,
			actor));

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived,
			actor,
			actor));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}

	[Fact]
	public void TimeoutRacesAnEvent_TheFirstSourceWins()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		AbilityRecorder recorder = new(runtime.Abilities);
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Race",
				GameplayWaitAny
					.Create()
					.WithTimeout(5f)
					.OnEvent(EventSchema.DamageReceived)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Tasks.Advance(1f);
		runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived,
			actor,
			actor));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);

		// The losing timeout was released with the task, so a later
		// advance cannot end the ability a second time.
		runtime.Tasks.Advance(10f);

		Assert.Equal(1, recorder.Count(GameplayAbilityChangeKind.Ended));
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);
	}

	[Fact]
	public void TagSource_CompletesWhenTheConditionHolds()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Tag",
				GameplayWaitAny
					.Create()
					.OnTag(TagSchema.StatusBurning)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Tasks.Advance(0.1f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusBurning));

		runtime.Tasks.Advance(0.1f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void ArmedTagSource_ThatIsDisarmedStillHonorsTheTimeout()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		List<GameplayAbilityTaskChange> changes = Record(runtime.Tasks);
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.ArmedDisarmed",
				GameplayWaitAny
					.Create()
					.WithTimeout(2f)
					.OnTag(TagSchema.StatusBurning)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.True(runtime.Tags.AddTag(actor, TagSchema.StatusBurning));
		Assert.True(runtime.Tags.RemoveTag(actor, TagSchema.StatusBurning));

		runtime.Tasks.Advance(0.1f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(1f);

		Assert.Equal(1, runtime.Tasks.PendingTaskCount);

		runtime.Tasks.Advance(1f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);

		GameplayAbilityTaskChange completed = changes.Single(
			change => change.Kind == GameplayAbilityTaskChangeKind.Completed);

		Assert.Equal(
			GameplayAbilityTaskCompletionReason.Elapsed,
			completed.Reason);
	}

	[Fact]
	public void CancellingTheAbility_ReleasesEverySource()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Cancel",
				GameplayWaitAny
					.Create()
					.WithTimeout(3f)
					.OnEvent(EventSchema.DamageReceived)
					.OnTag(TagSchema.StatusBurning)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);
		Assert.Equal(1, runtime.Events.SubscriptionCount);

		Assert.True(runtime.Abilities.Cancel(handle));

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);
		Assert.Equal(0, runtime.Abilities.ActiveAbilityCount);

		// The released sources deliver nothing afterwards.
		runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived,
			actor,
			actor));

		runtime.Tasks.Advance(10f);

		Assert.False(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void WaitAny_RejectsAnEmptyDescription()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();

		Assert.Throws<InvalidOperationException>(
			() => GameplayAbilityPlan.Create().WaitAny(
				GameplayWaitAny.Create()));

		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.WaitAny.Empty")
			.Plan(GameplayAbilityPlan.Create().End().Build())
			.Build();
		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);
		GameplayAbilityActivation activation =
			runtime.Abilities.TryActivate(handle).Activation;

		Assert.Throws<InvalidOperationException>(
			() => runtime.Tasks.WaitAny(
				activation,
				GameplayWaitAny.Create()));
	}

	[Fact]
	public void WaitAny_ValidatesItsSources()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayWaitAny.Create().WithTimeout(-1f));
		Assert.Throws<ArgumentException>(
			() => GameplayWaitAny.Create().OnEvent(GameplayTag.None));
		Assert.Throws<InvalidOperationException>(
			() => GameplayWaitAny
				.Create()
				.OnTag(TagSchema.StatusBurning)
				.OnTag(TagSchema.StatusStunned));
	}

	[Fact]
	public void WaitAny_AllocatesNothingAfterWarmup()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityDefinition definition = GameplayAbilityDefinition
			.Create("Test.WaitAny.Allocation")
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitAny(
						GameplayWaitAny
							.Create()
							.OnEvent(EventSchema.DamageReceived))
					.End()
					.Build())
			.Build();

		GameplayAbilityHandle handle =
			runtime.Abilities.GiveAbility(actor, definition);

		for (int index = 0; index < 128; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Events.Publish(new GameplayEvent(
				EventSchema.DamageReceived,
				actor,
				actor));
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			runtime.Abilities.TryActivate(handle);
			runtime.Events.Publish(new GameplayEvent(
				EventSchema.DamageReceived,
				actor,
				actor));
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void CompletionHandler_PublishingAnotherEventIsSafe()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		int completed = 0;
		bool reentered = false;

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind != GameplayAbilityTaskChangeKind.Completed)
			{
				return;
			}

			completed++;

			if (reentered)
			{
				return;
			}

			reentered = true;

			// The race already released its subscription, so this
			// publication must not complete the same task twice or
			// invalidate the dispatch in progress.
			runtime.Events.Publish(new GameplayEvent(
				EventSchema.DamageReceived,
				actor,
				actor));
		};

		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.Reentrant",
				GameplayWaitAny
					.Create()
					.OnEvent(EventSchema.DamageReceived)));

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		runtime.Events.Publish(new GameplayEvent(
			EventSchema.DamageReceived,
			actor,
			actor));

		Assert.True(reentered);
		Assert.Equal(1, completed);
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));
	}

	[Fact]
	public void CompletionHandlerException_LeavesTheRaceReleased()
	{
		using AbilityRuntime runtime = new();
		Entity actor = runtime.CreateActor();
		GameplayAbilityHandle handle = runtime.Abilities.GiveAbility(
			actor,
			Plan(
				"Test.WaitAny.HandlerThrows",
				GameplayWaitAny
					.Create()
					.WithTimeout(5f)
					.OnEvent(EventSchema.DamageReceived)
					.OnTag(TagSchema.StatusBurning)));

		runtime.Tasks.Changed += change =>
		{
			if (change.Kind == GameplayAbilityTaskChangeKind.Completed)
			{
				throw new InvalidOperationException(
					"The completion handler failed.");
			}
		};

		Assert.True(runtime.Abilities.TryActivate(handle).Succeeded);

		Assert.Throws<InvalidOperationException>(
			() => runtime.Events.Publish(new GameplayEvent(
				EventSchema.DamageReceived,
				actor,
				actor)));

		// The state was released and the plan continued before the
		// throwing notification ran, so no deadline, subscription, or
		// tag link survives the failure.
		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
		Assert.Equal(0, runtime.Events.SubscriptionCount);
		Assert.False(runtime.Abilities.IsActive(handle));
		Assert.True(runtime.Tags.HasTag(actor, TagSchema.EffectBuff));

		runtime.Tasks.Advance(10f);

		Assert.Equal(0, runtime.Tasks.PendingTaskCount);
	}
}
