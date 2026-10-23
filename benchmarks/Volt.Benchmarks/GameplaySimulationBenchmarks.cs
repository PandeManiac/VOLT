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
/// Measures one fixed step with no gameplay, an empty gameplay
/// integration, and a large idle gameplay population.
/// </summary>
/// <remarks>
/// Idle effects and tasks are not scanned: effects and duration/tag
/// tasks compare one heap root, and event tasks are indexed in the
/// router. The active ability count does not add per-frame work either,
/// because abilities have no clock of their own.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplaySimulationBenchmarks
{
	private const long FrameNanoseconds = 16_666_667;

	private GameLoop _plainLoop = null!;
	private BenchmarkClock _plainClock = null!;

	private GameplayRuntime _empty = null!;
	private GameplayRuntime _populated = null!;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_plainClock = new BenchmarkClock();
		_plainLoop = new GameLoop(GameLoopSettings.Default, _plainClock);

		_empty = new GameplayRuntime();

		_populated = new GameplayRuntime();

		Entity actor = _populated.World.CreateEntity();

		GameplayTag assetTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Simulation.Ability");

		GameplayAbilityDefinition idleAbility = GameplayAbilityDefinition
			.Create("Bench.Simulation.IdleAbility")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		GameplayAbilityHandle handle = _populated.Abilities.GiveAbility(
			actor,
			idleAbility);

		Assert(_populated.Abilities.TryActivate(handle).Succeeded);

		GameplayEffectDefinition idleEffect = GameplayEffectDefinition
			.Create("Bench.Simulation.IdleEffect")
			.HasDuration(1_000_000f)
			.Build();

		for (int index = 0; index < Population; index++)
		{
			Assert(_populated.Effects.Apply(actor, idleEffect).Applied);
		}

		Assert(_populated.Abilities.TryGetActivation(
			handle,
			out GameplayAbilityActivation activation));

		for (int index = 0; index < Population; index++)
		{
			Assert(!_populated.Abilities.Tasks
				.WaitDuration(activation, 1_000_000f)
				.IsNone);
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_populated.Dispose();
		_empty.Dispose();
	}

	[Benchmark(Baseline = true)]
	public int Step_NoGameplay()
	{
		_plainClock.AdvanceNanoseconds(FrameNanoseconds);
		_plainLoop.Advance();

		return 0;
	}

	[Benchmark]
	public int Step_EmptyIntegration()
	{
		return _empty.Step() ? 1 : 0;
	}

	[Benchmark]
	public int Step_IdleGameplayPopulation()
	{
		return _populated.Step() ? 1 : 0;
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The gameplay simulation benchmark setup failed.");
		}
	}

	private sealed class GameplayRuntime : IDisposable
	{
		private readonly BenchmarkClock _clock;
		private readonly GameLoop _loop;

		public GameplayRuntime()
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
		}

		public World World { get; }

		public GameplayAttributeStore Attributes { get; }

		public GameplayTagStore Tags { get; }

		public GameplayEffectSystem Effects { get; }

		public GameplayEventSystem Events { get; }

		public GameplayAbilitySystem Abilities { get; }

		public GameplaySimulation Simulation { get; }

		public bool Step()
		{
			_clock.AdvanceNanoseconds(FrameNanoseconds);
			_loop.Advance();

			return true;
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
