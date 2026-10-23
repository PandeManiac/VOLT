using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.GameplayDemo;

/// <summary>
/// A deterministic, graphics-free walkthrough of event-driven
/// abilities.
/// </summary>
/// <remarks>
/// The demonstration advances simulation time explicitly, so every
/// printed value is reproducible. It covers the normal charged dash,
/// a stun during the wind-up, overlapping abilities with independent
/// task ownership, the application-facing task API, and owner
/// destruction with pending tasks. Any violated expectation throws and
/// the process exits with a non-zero status.
/// </remarks>
internal sealed class ChargedDashDemo : IDisposable
{
	private const int CounterSwingTicks = 3;

	private readonly World _world;
	private readonly GameplayAttributeStore _attributes;
	private readonly GameplayTagStore _tags;
	private readonly GameplayEffectSystem _effects;
	private readonly GameplayEventSystem _events;
	private readonly GameplayAbilitySystem _abilities;
	private readonly GameLoop _loop;
	private readonly GameplaySimulation _simulation;
	private readonly DemoMonotonicClock _clock;
	private readonly GameplayAbilityHandle _dash;
	private readonly GameplayAbilityHandle _focus;
	private readonly GameplayAbilityHandle _overcharge;
	private readonly GameplayAbilityHandle _counterattack;
	private readonly GameplayAbilityTaskDefinition _counterSwing;
	private readonly Entity _player;

	private Entity _counterTarget;
	private GameplayAbilityTaskHandle _counterSwingTask;
	private int _counterSwingTicks;
	private int _assertions;

	internal ChargedDashDemo()
	{
		_world = new World();
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);
		_events = new GameplayEventSystem(_world);
		_abilities = new GameplayAbilitySystem(
			_world,
			_tags,
			_effects,
			_attributes,
			_events);

		_clock = new DemoMonotonicClock();
		_loop = new GameLoop(GameLoopSettings.Default, _clock);
		_simulation = new GameplaySimulation(_loop, _abilities, _effects);

		_player = _world.CreateEntity();

		_dash = _abilities.GiveAbility(_player, DemoSchema.ChargedDash);
		_focus = _abilities.GiveAbility(_player, DemoSchema.Focus);
		_overcharge = _abilities.GiveAbility(_player, DemoSchema.Overcharge);

		// Custom tasks are registered once. The definition is shared
		// by every activation and carries no per-activation state.
		_counterSwing = _abilities.RegisterTaskDefinition(
			"Demo.CounterSwing");

		GameplayAbilityDefinition counterattack = GameplayAbilityDefinition
			.Create("Demo.Ability.Counterattack")
			.AssetTags(DemoSchema.AbilityCounter)
			.Cost(DemoSchema.CounterCost)
			.CancelOnTags(DemoSchema.StatusStunned)
			.RequireToActivate(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(DemoSchema.StatusStunned)))
			.Plan(
				GameplayAbilityPlan
					.Create()
					.ApplyEffect(DemoSchema.CounterGuard)
					.WaitAny(
						GameplayWaitAny
							.Create()
							.WithTimeout(0.5f)
							.OnEvent(DemoSchema.EventIncomingHit))
					.If(GameplayCondition.All(
						GameplayCondition.TargetHasTag(
							DemoSchema.StatusStunned),
						GameplayCondition.OwnerAttribute(
							DemoSchema.Stamina,
							GameplayAttributeComparison.GreaterThanOrEqual,
							40f)))
					.ApplyEffect(DemoSchema.CounterEnhanced)
					.PublishEvent(DemoSchema.EventCounterLanded)
					.Else()
					.WaitCustom(_counterSwing)
					.ApplyEffect(DemoSchema.CounterStandard)
					.PublishEvent(DemoSchema.EventCounterLanded)
					.EndIf()
					.WaitTag(
						DemoSchema.StatusGuarding,
						GameplayTagWaitMode.Absent)
					.End()
					.Build())
			.Build();

		_counterattack = _abilities.GiveAbility(
			_player,
			counterattack);

		_counterTarget = _world.CreateEntity();

		Tasks.Changed += change =>
		{
			if (change.TaskKind != GameplayAbilityTaskKind.Custom)
			{
				return;
			}

			if (change.Kind == GameplayAbilityTaskChangeKind.Started)
			{
				_counterSwingTask = change.Handle;
				_counterSwingTicks = 0;
			}
			else if (change.Handle == _counterSwingTask)
			{
				// Completion, cancellation, or owner destruction may
				// end this handle. Ignore terminal notifications for
				// unrelated custom task instances.
				_counterSwingTask = GameplayAbilityTaskHandle.None;
				_counterSwingTicks = 0;
			}
		};

		// The application owns the interaction that the custom task
		// waits for. This ticker is the demo's stand-in for an
		// animation or interaction system; the gameplay runtime never
		// polls the task.
		_loop.RegisterFixedUpdate((in FixedUpdateContext context) =>
		{
			if (_counterSwingTask.IsNone)
			{
				return;
			}

			_counterSwingTicks++;

			if (_counterSwingTicks >= CounterSwingTicks)
			{
				Tasks.Complete(_counterSwingTask);
			}
		});
	}

	private GameplayAbilityTaskSystem Tasks => _abilities.Tasks;

	public void Run()
	{
		Console.WriteLine("VOLT gameplay demo: event-driven Charged Dash");

		RunChargedDash();
		RunInterruptedWindUp();
		RunOverlappingAbilities();
		RunApplicationTasks();
		RunOwnerDestruction();
		RunCounterattack();
		RunPauseAndTimeScale();
		VerifyCleanShutdown();

		Console.WriteLine();
		Console.WriteLine(
			$"All {_assertions} assertions passed in {Tasks.Time:0.00} " +
			$"simulation seconds.");
	}

	public void Dispose()
	{
		_simulation.Dispose();
		_abilities.Dispose();
		_events.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	private void RunChargedDash()
	{
		Begin("1. Normal charged dash");

		Report($"stamina before activation: {Stamina:0}");

		GameplayAbilityActivationResult activation =
			_abilities.TryActivate(_dash);

		Require(activation.Succeeded, "the charged dash did not activate");
		Require(
			_abilities.IsActive(_dash),
			"the charged dash ended during activation");

		Report(
			$"activated: stamina={Stamina:0}, " +
			$"cooldown={_abilities.IsOnCooldown(_player, DemoSchema.ChargedDash)}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(Stamina == 40f, "the dash cost was not paid");
		Require(
			_abilities.IsOnCooldown(_player, DemoSchema.ChargedDash),
			"the dash cooldown was not applied");
		Require(
			Tasks.PendingTaskCount == 1,
			"the plan did not start its wind-up task");

		Advance(0.2f);

		Report(
			$"t={Tasks.Time:0.00}: wind-up pending=" +
			$"{Tasks.PendingTaskCount}, speed={Speed:0}, " +
			$"dashing={IsDashing}");

		Require(
			Tasks.PendingTaskCount == 1,
			"the wind-up task ended before its deadline");
		Require(!IsDashing, "the dash buff applied during the wind-up");
		Require(Speed == 10f, "the dash buff changed speed early");

		Advance(0.05f);

		Report(
			$"t={Tasks.Time:0.00}: wind-up done, waiting for " +
			$"Event.Dash.Released, pendingTasks={Tasks.PendingTaskCount}");

		Require(
			Tasks.PendingTaskCount == 1,
			"the plan did not start the release wait");

		int delivered = _events.Publish(
			new GameplayEvent(DemoSchema.EventDashReleased, _player, _player));

		Report(
			$"released: delivered={delivered}, dashing={IsDashing}, " +
			$"speed={Speed:0}, pendingTasks={Tasks.PendingTaskCount}");

		Require(delivered == 1, "the release event had no waiting task");
		Require(IsDashing, "the dash buff was not applied on release");
		Require(Speed == 15f, "the dash buff did not raise speed");
		Require(
			Tasks.PendingTaskCount == 1,
			"the plan did not start the buff-completion wait");

		Advance(0.5f);

		Report(
			$"t={Tasks.Time:0.00}: buff expired, active=" +
			$"{_abilities.IsActive(_dash)}, pendingTasks=" +
			$"{Tasks.PendingTaskCount}, speed={Speed:0}, " +
			$"stamina={Stamina:0}");

		Require(!_abilities.IsActive(_dash), "the charged dash did not end");
		Require(Tasks.PendingTaskCount == 0, "the plan leaked a task");
		Require(!IsDashing, "the dash buff did not expire");
		Require(Speed == 10f, "speed did not return to its base value");
		Require(Stamina == 40f, "the paid cost changed after the dash");

		Advance(0.3f);

		Require(
			!_abilities.IsOnCooldown(_player, DemoSchema.ChargedDash),
			"the cooldown did not expire");
	}

	private void RunInterruptedWindUp()
	{
		Begin("2. Stun during the wind-up");

		GameplayAbilityActivationResult activation =
			_abilities.TryActivate(_dash);

		Require(activation.Succeeded, "the second dash did not activate");
		Require(
			Tasks.PendingTaskCount == 1,
			"the second dash did not start its wind-up");

		Span<GameplayAbilityTaskHandle> tasks =
			stackalloc GameplayAbilityTaskHandle[4];

		int count = Tasks.CopyTasks(_player, tasks);

		Require(count == 1, "the wind-up task was not discoverable");

		GameplayAbilityTaskHandle windUp = tasks[0];

		Advance(0.1f);

		GameplayEffectApplyResult stun =
			_effects.Apply(_player, DemoSchema.Stun, _player);

		Require(
			stun.Status == GameplayEffectApplyStatus.Applied,
			"the stun effect did not apply");

		Report(
			$"stunned at t={Tasks.Time:0.00}: active=" +
			$"{_abilities.IsActive(_dash)}, pendingTasks=" +
			$"{Tasks.PendingTaskCount}, stamina={Stamina:0}, " +
			$"cooldown={_abilities.IsOnCooldown(_player, DemoSchema.ChargedDash)}");

		Require(
			!_abilities.IsActive(_dash),
			"the stun did not cancel the charged dash");
		Require(
			Tasks.PendingTaskCount == 0,
			"the cancelled activation kept its wind-up task");
		Require(
			Stamina == 30f,
			"the committed cost was rolled back on cancellation");
		Require(
			_abilities.IsOnCooldown(_player, DemoSchema.ChargedDash),
			"the committed cooldown was rolled back on cancellation");
		Require(
			!Tasks.Cancel(windUp),
			"the stale wind-up handle still resolved");

		Advance(5f);

		int lateDelivery = _events.Publish(
			new GameplayEvent(DemoSchema.EventDashReleased, _player, _player));

		Report(
			$"advanced to t={Tasks.Time:0.00} and released late: " +
			$"delivered={lateDelivery}, dashing={IsDashing}, active=" +
			$"{_abilities.IsActive(_dash)}, pendingTasks=" +
			$"{Tasks.PendingTaskCount}");

		Require(lateDelivery == 0, "the dead wind-up still listened");
		Require(!IsDashing, "the late release applied the dash buff");
		Require(!_abilities.IsActive(_dash), "the late release restarted dash");
		Require(Tasks.PendingTaskCount == 0, "a stale task survived the advance");

		bool removed = _effects.Remove(stun.Handle);

		Require(removed, "the stun effect could not be removed");
		Require(!IsStunned, "the stun tag survived effect removal");
	}

	private void RunOverlappingAbilities()
	{
		Begin("3. Overlapping abilities and independent task ownership");

		GameplayAbilityActivationResult dash =
			_abilities.TryActivate(_dash);

		GameplayAbilityActivationResult focus =
			_abilities.TryActivate(_focus);

		Require(dash.Succeeded, "the overlapping dash did not activate");
		Require(focus.Succeeded, "the focus ability did not activate");

		Report(
			$"both active: dash={_abilities.IsActive(_dash)}, " +
			$"focus={_abilities.IsActive(_focus)}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(
			Tasks.PendingTaskCount == 2,
			"the two activations did not own one task each");

		bool cancelled = _abilities.Cancel(_dash);

		Report(
			$"cancelled dash: success={cancelled}, dashActive=" +
			$"{_abilities.IsActive(_dash)}, focusActive=" +
			$"{_abilities.IsActive(_focus)}, pendingTasks=" +
			$"{Tasks.PendingTaskCount}");

		Require(cancelled, "the overlapping dash could not be cancelled");
		Require(
			!_abilities.IsActive(_dash),
			"the dash remained active after cancellation");
		Require(
			_abilities.IsActive(_focus),
			"cancelling the dash also ended the focus ability");
		Require(
			Tasks.PendingTaskCount == 1,
			"the focus ability lost its task with the dash");

		Advance(0.5f);

		Report(
			$"t={Tasks.Time:0.00}: focusActive=" +
			$"{_abilities.IsActive(_focus)}, speed={Speed:0}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(!_abilities.IsActive(_focus), "the focus ability did not end");
		Require(Speed == 12f, "the focus buff did not apply");
		Require(Tasks.PendingTaskCount == 0, "the focus plan leaked a task");

		Advance(1f);

		Require(Speed == 10f, "the focus buff did not expire");
	}

	private void RunApplicationTasks()
	{
		Begin("4. Application tasks and stale handles");

		GameplayAbilityActivationResult activation =
			_abilities.TryActivate(_overcharge);

		Require(activation.Succeeded, "the overcharge did not activate");
		Require(
			_abilities.TryGetActivation(_overcharge, out var first),
			"the overcharge activation token was unavailable");

		GameplayAbilityTaskHandle task = Tasks.WaitDuration(first, 0.4f);

		Require(!task.IsNone, "the duration task was not created");
		Require(
			Tasks.TryGetTask(task, out GameplayAbilityTaskInfo info),
			"the duration task could not be inspected");

		Report(
			$"started task: kind={info.Kind}, planStep={info.PlanStep}, " +
			$"remaining={info.RemainingTime:0.00}s");

		Require(
			info.PlanStep < 0,
			"an application task was attributed to a plan step");
		Require(
			Math.Abs(info.RemainingTime - 0.4) < 0.0001,
			"the task reported the wrong remaining time");

		Advance(0.2f);

		Require(
			Tasks.TryGetTask(task, out info)
				&& Math.Abs(info.RemainingTime - 0.2) < 0.0001,
			"the task remaining time did not advance");

		Advance(0.2f);

		Require(
			!Tasks.TryGetTask(task, out _),
			"the completed task still resolved");
		Require(
			!Tasks.Cancel(task),
			"the completed task was cancelled twice");
		Require(
			_abilities.IsActive(_overcharge),
			"a task completion ended the application ability");

		_abilities.End(_overcharge);

		Require(
			_abilities.TryActivate(_overcharge).Succeeded,
			"the overcharge did not reactivate");

		Require(
			_abilities.TryGetActivation(_overcharge, out var second),
			"the second overcharge activation was unavailable");

		GameplayAbilityTaskHandle stale = Tasks.WaitDuration(second, 1f);

		Require(_abilities.End(_overcharge), "the overcharge did not end");
		Require(
			!Tasks.TryGetTask(stale, out _),
			"ending the ability left its task resolvable");
		Require(
			!Tasks.Cancel(stale),
			"a task of the ended activation was still cancellable");

		_abilities.TryActivate(_overcharge);

		Require(
			_abilities.TryGetActivation(_overcharge, out var third),
			"the third overcharge activation was unavailable");

		GameplayAbilityTaskHandle current = Tasks.WaitDuration(third, 1f);

		Report(
			$"stale cancel refused={!Tasks.Cancel(stale)}, " +
			$"current task pending={Tasks.TryGetTask(current, out _)}");

		Require(
			!Tasks.Cancel(stale),
			"the old task handle cancelled the new activation");
		Require(
			Tasks.TryGetTask(current, out _),
			"the stale handle disturbed the new task");
		Require(Tasks.Cancel(current), "the current task was not cancellable");
		Require(_abilities.End(_overcharge), "the overcharge did not clean up");
	}

	private void RunOwnerDestruction()
	{
		Begin("5. Owner destruction with pending tasks");

		Entity victim = _world.CreateEntity();

		GameplayAbilityHandle handle =
			_abilities.GiveAbility(victim, DemoSchema.ChargedDash);

		GameplayAbilityActivationResult activation =
			_abilities.TryActivate(handle);

		Require(activation.Succeeded, "the victim dash did not activate");
		Require(
			Tasks.GetPendingTaskCount(victim) == 1,
			"the victim dash did not start a task");

		Advance(0.1f);

		int before = Tasks.PendingTaskCount;

		Require(
			_world.DestroyEntity(victim),
			"the victim entity could not be destroyed");

		Report(
			$"destroyed owner: pendingBefore={before}, " +
			$"ownerTasks={Tasks.GetPendingTaskCount(victim)}, " +
			$"totalTasks={Tasks.PendingTaskCount}");

		Require(
			Tasks.GetPendingTaskCount(victim) == 0,
			"the destroyed owner kept pending tasks");
		Require(!_world.IsAlive(victim), "the destroyed entity is still alive");

		Advance(1f);

		int lateDelivery = _events.Publish(
			new GameplayEvent(DemoSchema.EventDashReleased, victim, victim));

		Require(lateDelivery == 0, "the destroyed owner still listened");
		Require(
			Tasks.PendingTaskCount == 0,
			"a task survived its owner's destruction");
	}

	private float Stamina =>
		_attributes.GetCurrentValue(_player, DemoSchema.Stamina);

	private float Speed =>
		_attributes.GetCurrentValue(_player, DemoSchema.Speed);

	private bool IsDashing =>
		_tags.HasTag(_player, DemoSchema.StatusDashing);

	private bool IsStunned =>
		_tags.HasTag(_player, DemoSchema.StatusStunned);

	private bool IsGuarding =>
		_tags.HasTag(_player, DemoSchema.StatusGuarding);

	private bool IsCounterStandard =>
		_tags.HasTag(_player, DemoSchema.StatusCounterStandard);

	private bool IsCounterEnhanced =>
		_tags.HasTag(_player, DemoSchema.StatusCounterEnhanced);

	/// <summary>
	/// Advances real time through the fixed-step loop. At normal time
	/// scale this is the same duration of simulation time. The gameplay
	/// integration advances effects and then tasks for every fixed
	/// step the loop executes.
	/// </summary>
	private void Advance(float seconds)
	{
		int frames = (int)MathF.Round(seconds * 60f);

		for (int index = 0; index < frames; index++)
		{
			_clock.AdvanceNanoseconds(DemoMonotonicClock.FrameNanoseconds);
			_loop.Advance();
		}
	}

	private void RunCounterattack()
	{
		Begin("6. Charged counterattack: branch, race, and custom task");

		_attributes.SetBaseValue(_player, DemoSchema.Stamina, 50f);

		List<GameplayAbilityChange> abilityChanges = [];
		List<GameplayAbilityTaskChange> taskChanges = [];

		void TrackAbilityChange(GameplayAbilityChange change)
		{
			abilityChanges.Add(change);
		}

		void TrackTaskChange(GameplayAbilityTaskChange change)
		{
			if (change.TaskKind == GameplayAbilityTaskKind.Custom)
			{
				taskChanges.Add(change);
			}
		}

		_abilities.Changed += TrackAbilityChange;
		Tasks.Changed += TrackTaskChange;

		Require(
			_abilities.TryActivate(_counterattack, _counterTarget).Succeeded,
			"the counterattack did not activate");

		Report(
			$"activated: stamina={Stamina:0}, guard={IsGuarding}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(Stamina == 45f, "the counterattack cost was not paid");
		Require(IsGuarding, "the guard effect did not start");
		Require(
			Tasks.PendingTaskCount == 1,
			"the race did not start exactly one task");

		Advance(0.1f);

		Require(
			_abilities.IsActive(_counterattack),
			"the counterattack ended before the hit or the timeout");

		int listeners = _events.Publish(new GameplayEvent(
			DemoSchema.EventIncomingHit,
			_counterTarget,
			_player));

		Report($"incoming hit delivered to {listeners} listener(s)");

		Require(listeners > 0, "the incoming hit reached no listener");
		Require(
			!_counterSwingTask.IsNone,
			"the standard branch did not wait for the custom task");
		Require(
			Tasks.GetPendingTaskCount(_player) == 1,
			"the standard branch did not own exactly one task");
		Require(
			CountAbilityChanges(
				abilityChanges,
				GameplayAbilityChangeKind.Activated) == 1,
			"the standard path changed activation notifications");
		Require(
			taskChanges.Count == 1
				&& taskChanges[0].Kind == GameplayAbilityTaskChangeKind.Started
				&& taskChanges[0].Handle == _counterSwingTask,
			"the custom task did not emit one matching Started transition");

		// The event won the race against the 0.5 s timeout, so the
		// timeout is gone. The custom task now waits for the demo's
		// own interaction ticker.
		Advance((CounterSwingTicks - 1) / 60f);

		Require(
			!IsCounterStandard,
			"the standard counter landed before the interaction finished");

		Advance(1f / 60f);

		Report(
			$"interaction finished: standard={IsCounterStandard}, " +
			$"enhanced={IsCounterEnhanced}, guard={IsGuarding}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(
			IsCounterStandard,
			"the standard counter did not land after the interaction");
		Require(
			_counterSwingTask.IsNone,
			"the completed custom task was still tracked");
		Require(
			taskChanges.Count == 2
				&& taskChanges[1].Kind == GameplayAbilityTaskChangeKind.Completed
				&& taskChanges[1].Handle == taskChanges[0].Handle,
			"the custom task did not emit a matching Completed transition");
		Require(
			_abilities.IsActive(_counterattack),
			"the plan did not wait for the guard to expire");
		Require(
			Tasks.PendingTaskCount == 1,
			"the guard wait did not start a task");

		Advance(0.6f);

		Report(
			$"guard expired: active={_abilities.IsActive(_counterattack)}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(
			!_abilities.IsActive(_counterattack),
			"the guard wait did not end the counterattack");
		Require(
			Tasks.PendingTaskCount == 0,
			"the counterattack kept tasks after ending");

		// Enhanced branch: the target is stunned, so the condition
		// chooses the enhanced counter and no interaction is needed.
		Advance(2f);

		_counterTarget = _world.CreateEntity();

		_attributes.SetBaseValue(_player, DemoSchema.Stamina, 50f);

		Require(
			_effects.Apply(_counterTarget, DemoSchema.Stun).Applied,
			"the counter target could not be stunned");
		Require(
			_abilities.TryActivate(_counterattack, _counterTarget).Succeeded,
			"the enhanced counterattack did not activate");

		Advance(0.1f);

		_events.Publish(new GameplayEvent(
			DemoSchema.EventIncomingHit,
			_counterTarget,
			_player));

		Report(
			$"stunned target: enhanced={IsCounterEnhanced}, " +
			$"standard={IsCounterStandard}, " +
			$"customTaskPending={!_counterSwingTask.IsNone}");

		Require(
			IsCounterEnhanced,
			"the branch did not choose the enhanced counter");
		Require(
			!IsCounterStandard,
			"the enhanced branch also applied the standard counter");
		Require(
			_counterSwingTask.IsNone,
			"the enhanced branch waited for an interaction");
		Require(
			CountAbilityChanges(
				abilityChanges,
				GameplayAbilityChangeKind.Activated) == 2,
			"the enhanced path did not activate independently");
		Require(
			taskChanges.Count == 2
				&& taskChanges[1].Kind == GameplayAbilityTaskChangeKind.Completed,
			"the enhanced path unexpectedly created a custom task");

		Advance(0.6f);

		Require(
			!_abilities.IsActive(_counterattack),
			"the enhanced counterattack did not end");

		// Interruption: a stun during the wind-up cancels the ability
		// and releases the custom task it was waiting on.
		Advance(2f);

		// A fresh, unstunned target, so the standard branch runs and
		// the interruption has a custom task to release.
		_world.DestroyEntity(_counterTarget);
		_counterTarget = _world.CreateEntity();
		_attributes.SetBaseValue(_player, DemoSchema.Stamina, 50f);

		Require(
			_abilities.TryActivate(_counterattack, _counterTarget).Succeeded,
			"the interrupted counterattack did not activate");

		Advance(0.1f);

		_events.Publish(new GameplayEvent(
			DemoSchema.EventIncomingHit,
			_counterTarget,
			_player));

		Require(
			!_counterSwingTask.IsNone,
			"the interrupted branch did not start the custom task");
		GameplayAbilityTaskHandle interruptedTask = _counterSwingTask;
		Require(
			taskChanges.Count == 3
				&& taskChanges[2].Kind == GameplayAbilityTaskChangeKind.Started
				&& taskChanges[2].Handle == interruptedTask,
			"the interrupted custom task did not emit a Started transition");

		Require(
			_effects.Apply(_player, DemoSchema.Stun).Applied,
			"the player could not be stunned");

		Report(
			$"stunned during wind-up: active=" +
			$"{_abilities.IsActive(_counterattack)}, " +
			$"pendingTasks={Tasks.PendingTaskCount}, " +
			$"customTaskPending={!_counterSwingTask.IsNone}");

		Require(
			!_abilities.IsActive(_counterattack),
			"the stun did not cancel the counterattack");
		Require(
			Tasks.PendingTaskCount == 0,
			"the cancelled counterattack kept tasks");
		Require(
			_counterSwingTask.IsNone,
			"the cancelled counterattack kept its custom task");
		Require(
			taskChanges.Count == 4
				&& taskChanges[3].Kind == GameplayAbilityTaskChangeKind.Cancelled
				&& taskChanges[3].Handle == interruptedTask,
			"cancelling did not emit the matching custom-task transition");

		_abilities.Changed -= TrackAbilityChange;
		Tasks.Changed -= TrackTaskChange;
		_effects.RemoveAll(_player);
		_world.DestroyEntity(_counterTarget);

		Advance(1f);

		Require(
			Tasks.PendingTaskCount == 0,
			"a task survived the counterattack scenarios");
	}

	private void RunPauseAndTimeScale()
	{
		Begin("7. Pause, single step, and time scaling");

		Require(
			_abilities.TryActivate(_overcharge).Succeeded,
			"the pause scenario did not activate");

		Require(
			_abilities.TryGetActivation(_overcharge, out var activation),
			"the pause scenario activation token was unavailable");

		GameplayAbilityTaskHandle task = Tasks.WaitDuration(
			activation,
			0.25f);

		double beforePause = Tasks.Time;

		_loop.Pause();
		Advance(1f);

		Report(
			$"paused for 1 s: simulationTime={Tasks.Time:0.000}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(Tasks.Time == beforePause, "paused time advanced gameplay");
		Require(
			Tasks.TryGetTask(task, out _),
			"the paused task disappeared");

		Require(_loop.StepOnce(), "a single step was not pending");

		Advance(1f / 60f);

		Require(
			Math.Abs(Tasks.Time - beforePause - _loop.FixedDeltaSeconds)
				< 0.000001,
			"a single step did not advance exactly one fixed delta");

		_loop.Resume();

		_loop.TimeScale = TimeScale.FromSingle(2f);

		double beforeFast = Tasks.Time;

		Advance(0.125f);

		Report(
			$"2x for 0.125 real seconds: simulationTime={Tasks.Time:0.000}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(
			!Tasks.TryGetTask(task, out _),
			"the 2x simulation did not complete the 0.25 s task");
		Require(
			Tasks.Time - beforeFast > 0.2,
			"2x time scaling did not advance simulation faster");

		Require(_abilities.End(_overcharge), "the pause ability did not end");

		_loop.TimeScale = TimeScale.FromSingle(0.5f);

		Require(
			_abilities.TryActivate(_overcharge).Succeeded,
			"the slow scenario did not activate");

		Require(
			_abilities.TryGetActivation(_overcharge, out var slow),
			"the slow scenario activation token was unavailable");

		GameplayAbilityTaskHandle slowTask = Tasks.WaitDuration(slow, 0.25f);
		double beforeSlow = Tasks.Time;

		Advance(0.5f);

		Report(
			$"0.5x for 0.5 real seconds: simulationTime={Tasks.Time:0.000}, " +
			$"pendingTasks={Tasks.PendingTaskCount}");

		Require(
			Tasks.Time - beforeSlow < 0.3,
			"0.5x time scaling advanced too much simulation time");
		Require(
			!Tasks.TryGetTask(slowTask, out _),
			"the 0.5x simulation did not complete the task");

		_loop.TimeScale = TimeScale.Normal;

		Require(_abilities.End(_overcharge), "the pause ability did not end");
	}

	private void VerifyCleanShutdown()
	{
		Begin("8. Clean shutdown");

		Report(
			$"activeAbilities={_abilities.ActiveAbilityCount}, " +
			$"pendingTasks={Tasks.PendingTaskCount}, " +
			$"activeEffects={_effects.ActiveEffectCount}, " +
			$"subscriptions={_events.SubscriptionCount}");

		Require(
			_abilities.ActiveAbilityCount == 0,
			"an ability survived the demonstration");
		Require(
			Tasks.PendingTaskCount == 0,
			"a task survived the demonstration");
		Require(
			_effects.ActiveEffectCount == 0,
			"an effect survived the demonstration");
		Require(
			_events.SubscriptionCount == 0,
			"an event subscription survived the demonstration");
	}

	private sealed class DemoMonotonicClock : IMonotonicClock
	{
		public const long NanosecondsPerSecond = 1_000_000_000;
		public const long FrameNanoseconds = 16_666_667;

		public long Timestamp { get; private set; }

		public long TicksPerSecond => NanosecondsPerSecond;

		public void AdvanceNanoseconds(long nanoseconds)
		{
			Timestamp += nanoseconds;
		}
	}

	private static void Begin(string title)
	{
		Console.WriteLine();
		Console.WriteLine($"=== {title} ===");
	}

	private static void Report(string message)
	{
		Console.WriteLine($"    {message}");
	}

	private static int CountAbilityChanges(
		List<GameplayAbilityChange> changes,
		GameplayAbilityChangeKind kind)
	{
		int count = 0;

		for (int index = 0; index < changes.Count; index++)
		{
			if (changes[index].Kind == kind)
			{
				count++;
			}
		}

		return count;
	}

	private void Require(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				$"Demo assertion failed: {message}.");
		}

		_assertions++;
	}
}
