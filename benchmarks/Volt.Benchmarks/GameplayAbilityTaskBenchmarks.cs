using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Benchmarks;

/// <summary>
/// Measures warmed-up single-task creation, completion, cancellation,
/// event waits, and tag waits.
/// </summary>
/// <remarks>
/// The ability stays active for the whole run, so each operation is a
/// task lifecycle rather than an activation. Task slots, event
/// subscriptions, heap entries, and capture buffers therefore stay
/// warm, which is what the zero-allocation claim is about.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAbilityTaskBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplayAbilityTaskSystem _tasks = null!;
	private GameplayAbilityDefinition _ability = null!;
	private GameplayAbilityHandle _handle;
	private GameplayAbilityActivation _activation;
	private GameplayTag _eventTag;
	private GameplayTag _conditionTag;
	private GameplayEvent _gameplayEvent;

	[GlobalSetup]
	public void Setup()
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

		GameplayTag assetTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Tasks.Ability");

		_eventTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Tasks.Event");

		_conditionTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Tasks.Condition");

		_gameplayEvent = new GameplayEvent(_eventTag);

		_ability = GameplayAbilityDefinition
			.Create("Bench.Tasks.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		Entity actor = _world.CreateEntity();

		_handle = _abilities.GiveAbility(actor, _ability);

		Assert(_abilities.TryActivate(_handle).Succeeded);
		Assert(_abilities.TryGetActivation(_handle, out _activation));

		Entity = actor;
		_tasks = _abilities.Tasks;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_abilities.Dispose();
		_events.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	internal Entity Entity { get; private set; }

	[Benchmark]
	public int StartAndCancelDuration()
	{
		GameplayAbilityTaskHandle task = _tasks.WaitDuration(
			_activation,
			1_000f);

		return _tasks.Cancel(task) ? 1 : 0;
	}

	[Benchmark]
	public int StartAndCompleteDuration()
	{
		GameplayAbilityTaskHandle task = _tasks.WaitDuration(
			_activation,
			0f);

		_tasks.Advance(0.001f);

		return task.IsNone ? 0 : 1;
	}

	[Benchmark]
	public int StartAndCancelEvent()
	{
		GameplayAbilityTaskHandle task = _tasks.WaitEvent(
			_activation,
			_eventTag);

		return _tasks.Cancel(task) ? 1 : 0;
	}

	[Benchmark]
	public int StartAndCompleteEvent()
	{
		_tasks.WaitEvent(_activation, _eventTag);

		return _events.Publish(_gameplayEvent);
	}

	[Benchmark]
	public int StartAndCancelTagCondition()
	{
		GameplayAbilityTaskHandle task = _tasks.WaitTag(
			_activation,
			_conditionTag,
			GameplayTagWaitMode.Present);

		return _tasks.Cancel(task) ? 1 : 0;
	}

	[Benchmark]
	public int StartAndSatisfyTagCondition()
	{
		_tasks.WaitTag(
			_activation,
			_conditionTag,
			GameplayTagWaitMode.Present);

		_tags.AddTag(Entity, _conditionTag);
		_tasks.Advance(0.001f);
		_tags.RemoveTag(Entity, _conditionTag);

		return 1;
	}

	[Benchmark]
	public int IdleAdvance_NoTasks()
	{
		_tasks.Advance(0.001f);

		return _tasks.PendingTaskCount;
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The task benchmark setup failed.");
		}
	}
}
