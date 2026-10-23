using BenchmarkDotNet.Attributes;
using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Benchmarks;

/// <summary>
/// Measures one fixed step that completes a large batch of ability
/// tasks with the same deadline.
/// </summary>
/// <remarks>
/// Setup runs outside the measured region so the result is the
/// completion burst, not task registration. The batch is rebuilt
/// before every invocation, and every task is due on the measured step.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplaySimulationTaskBurstBenchmarks
{
	private const long FrameNanoseconds = 16_666_667;

	private BenchmarkClock _clock = null!;
	private GameLoop _loop = null!;
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplaySimulation _simulation = null!;
	private GameplayAbilityTaskSystem _tasks = null!;
	private GameplayAbilityHandle _handle;
	private Entity _actor;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_clock = new BenchmarkClock();
		_loop = new GameLoop(GameLoopSettings.Default, _clock);
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
		_simulation = new GameplaySimulation(_loop, _abilities, _effects);
		_tasks = _abilities.Tasks;

		GameplayTag assetTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.TaskBurst.Ability");

		GameplayAbilityDefinition ability = GameplayAbilityDefinition
			.Create("Bench.TaskBurst.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		_actor = _world.CreateEntity();
		_handle = _abilities.GiveAbility(_actor, ability);

		Assert(_abilities.TryActivate(_handle).Succeeded);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_simulation.Dispose();
		_abilities.Dispose();
		_events.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	[IterationCleanup]
	public void CancelRemainder()
	{
		_tasks.CancelAll(_actor);
	}

	[Benchmark]
	public int CreateAndCompleteDurationTasks()
	{
		Assert(_abilities.TryGetActivation(
			_handle,
			out GameplayAbilityActivation activation));

		for (int index = 0; index < Population; index++)
		{
			Assert(!_tasks.WaitDuration(activation, _loop.FixedDeltaSeconds)
				.IsNone);
		}

		_clock.AdvanceNanoseconds(FrameNanoseconds);
		_loop.Advance();

		return _tasks.PendingTaskCount;
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The task burst benchmark setup failed.");
		}
	}

	private sealed class BenchmarkClock : IMonotonicClock
	{
		public long Timestamp { get; private set; }

		public long TicksPerSecond => 1_000_000_000;

		public void AdvanceNanoseconds(long nanoseconds)
		{
			Timestamp += nanoseconds;
		}
	}
}

/// <summary>
/// Measures one fixed step that expires a large batch of effects with
/// the same deadline.
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class GameplaySimulationEffectBurstBenchmarks
{
	private const long FrameNanoseconds = 16_666_667;

	private BenchmarkClock _clock = null!;
	private GameLoop _loop = null!;
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplaySimulation _simulation = null!;
	private GameplayEffectDefinition _effect = null!;
	private Entity _actor;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_clock = new BenchmarkClock();
		_loop = new GameLoop(GameLoopSettings.Default, _clock);
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
		_simulation = new GameplaySimulation(_loop, _abilities, _effects);
		_effect = GameplayEffectDefinition
			.Create("Bench.EffectBurst.Effect")
			.HasDuration(_loop.FixedDeltaSeconds)
			.Build();
		_actor = _world.CreateEntity();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_simulation.Dispose();
		_abilities.Dispose();
		_events.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	[IterationCleanup]
	public void ClearRemainder()
	{
		_effects.RemoveAll(_actor);
	}

	[Benchmark]
	public int ApplyAndExpireEffects()
	{
		for (int index = 0; index < Population; index++)
		{
			Assert(_effects.Apply(_actor, _effect).Applied);
		}

		_clock.AdvanceNanoseconds(FrameNanoseconds);
		_loop.Advance();

		return _effects.ActiveEffectCount;
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The effect burst benchmark setup failed.");
		}
	}

	private sealed class BenchmarkClock : IMonotonicClock
	{
		public long Timestamp { get; private set; }

		public long TicksPerSecond => 1_000_000_000;

		public void AdvanceNanoseconds(long nanoseconds)
		{
			Timestamp += nanoseconds;
		}
	}
}
