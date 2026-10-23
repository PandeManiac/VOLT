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
/// Measures fixed-step cost with large dormant custom-task and idle
/// ability populations, and the cost of cancelling many custom tasks.
/// </summary>
/// <remarks>
/// A dormant custom task is not in the deadline heap and has no event
/// subscription, so it must not add per-step work. The benchmarks prove
/// that the step stays constant from 1,000 to 100,000 pending tasks
/// while the batch benchmark measures registration and cancellation.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAuthoringPopulationBenchmarks
{
	private const long FrameNanoseconds = 16_666_667;

	private SimulationRuntime _empty = null!;
	private SimulationRuntime _populated = null!;
	private SimulationRuntime _batch = null!;

	private GameplayAbilityHandle _handle;
	private GameplayAbilityHandle _batchHandle;
	private GameplayAbilityTaskDefinition _definition = null!;
	private GameplayAbilityTaskDefinition _batchDefinition = null!;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_empty = new SimulationRuntime();
		_batch = new SimulationRuntime();

		_populated = new SimulationRuntime();
		_definition = _populated.Abilities.RegisterTaskDefinition(
			"Bench.Population.Dormant");

		GameplayAbilityDefinition idle = GameplayAbilityDefinition
			.Create("Bench.Population.IdleCustomTask")
			.EndOnActivate(false)
			.Plan(
				GameplayAbilityPlan
					.Create()
					.WaitCustom(_definition)
					.End()
					.Build())
			.Build();

		_handle = _populated.Abilities.GiveAbility(
			_populated.Actor,
			idle);

		if (!_populated.Abilities.TryActivate(_handle).Succeeded)
		{
			throw new InvalidOperationException(
				"The population benchmark activation failed.");
		}

		if (!_populated.Abilities.TryGetActivation(
			_handle,
			out GameplayAbilityActivation activation))
		{
			throw new InvalidOperationException(
				"The population benchmark activation was unavailable.");
		}

		for (int index = 0; index < Population; index++)
		{
			if (_populated.Abilities.Tasks
				.WaitCustom(activation, _definition)
				.IsNone)
			{
				throw new InvalidOperationException(
					"The population benchmark task was not created.");
			}
		}

		// The batch benchmark needs a fresh activation per operation,
		// so it uses a second runtime rather than disturbing the
		// dormant population.
		_batchDefinition = _batch.Abilities.RegisterTaskDefinition(
			"Bench.Population.Batch");

		GameplayAbilityDefinition batchAbility = GameplayAbilityDefinition
			.Create("Bench.Population.BatchCustomTask")
			.EndOnActivate(false)
			.Build();

		_batchHandle = _batch.Abilities.GiveAbility(
			_batch.Actor,
			batchAbility);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_batch.Dispose();
		_populated.Dispose();
		_empty.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int Step_EmptyIntegration()
	{
		_empty.Step();

		return 1;
	}

	[Benchmark]
	public int Step_WithDormantCustomTasks()
	{
		_populated.Step();

		return 1;
	}

	[Benchmark]
	public int StartAndCancelManyCustomTasks()
	{
		if (!_batch.Abilities.TryActivate(_batchHandle).Succeeded)
		{
			return 0;
		}

		if (!_batch.Abilities.TryGetActivation(
			_batchHandle,
			out GameplayAbilityActivation activation))
		{
			return 0;
		}

		for (int index = 0; index < Population; index++)
		{
			_batch.Abilities.Tasks.WaitCustom(
				activation,
				_batchDefinition);
		}

		return _batch.Abilities.Cancel(_batchHandle) ? 1 : 0;
	}

	private sealed class SimulationRuntime : IDisposable
	{
		private readonly BenchmarkClock _clock;
		private readonly GameLoop _loop;

		public SimulationRuntime()
		{
			_clock = new BenchmarkClock();
			_loop = new GameLoop(GameLoopSettings.Default, _clock);
			World = new World();
			Attributes = new GameplayAttributeStore(World);
			Tags = new GameplayTagStore(World);
			Effects = new GameplayEffectSystem(World, Attributes, Tags);
			Events = new GameplayEventSystem(World);
			Abilities = new GameplayAbilitySystem(
				World,
				Tags,
				Effects,
				Attributes,
				Events);
			Simulation = new GameplaySimulation(_loop, Abilities, Effects);
			Actor = World.CreateEntity();
		}

		public World World { get; }

		public GameplayAttributeStore Attributes { get; }

		public GameplayTagStore Tags { get; }

		public GameplayEffectSystem Effects { get; }

		public GameplayEventSystem Events { get; }

		public GameplayAbilitySystem Abilities { get; }

		public GameplaySimulation Simulation { get; }

		public Entity Actor { get; }

		public void Step()
		{
			_clock.AdvanceNanoseconds(FrameNanoseconds);
			_loop.Advance();
		}

		public void Dispose()
		{
			Simulation.Dispose();
			Abilities.Dispose();
			Events.Dispose();
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			World.Dispose();
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
