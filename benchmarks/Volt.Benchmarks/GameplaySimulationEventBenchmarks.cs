using BenchmarkDotNet.Attributes;
using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures a fixed step whose game callback publishes one gameplay
/// event to many listeners.
/// </summary>
/// <remarks>
/// Event delivery is synchronous inside the fixed-update callback, so
/// the measured step includes the loop overhead, the fixed callback,
/// and the entire event dispatch. It is deliberately separate from
/// event-only microbenchmarks to show the whole-step cost.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplaySimulationEventBenchmarks
{
	private const long FrameNanoseconds = 16_666_667;

	private static readonly Action<GameplayEvent> Handler = OnEvent;
	private static int s_sink;

	private BenchmarkClock _clock = null!;
	private GameLoop _loop = null!;
	private GameplayRuntime _runtime = null!;
	private GameplayEvent _event;

	[Params(0, 1, 1_000, 10_000)]
	public int ListenerCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_clock = new BenchmarkClock();
		_loop = new GameLoop(GameLoopSettings.Default, _clock);
		_runtime = new GameplayRuntime(_loop);

		GameplayTag eventTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.SimulationEvent.Hit");

		_event = new GameplayEvent(eventTag);

		for (int index = 0; index < ListenerCount; index++)
		{
			_runtime.Events.Subscribe(eventTag, Handler);
		}

		_loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
			_runtime.Events.Publish(_event));
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_runtime.Dispose();
	}

	[Benchmark]
	public int Step_WithEventDispatch()
	{
		_clock.AdvanceNanoseconds(FrameNanoseconds);
		_loop.Advance();

		return _runtime.Events.SubscriptionCount;
	}

	private static void OnEvent(GameplayEvent gameplayEvent)
	{
		s_sink += (int)gameplayEvent.Tag.Id;
	}

	private sealed class GameplayRuntime : IDisposable
	{
		private readonly GameLoop _loop;
		private readonly World _world;

		public GameplayRuntime(GameLoop loop)
		{
			_loop = loop;
			_world = new World();
			Attributes = new GameplayAttributeStore(_world);
			Tags = new GameplayTagStore(_world);
			Effects = new GameplayEffectSystem(_world, Attributes, Tags);
			Events = new GameplayEventSystem(_world);
			Abilities = new GameplayAbilitySystem(
				_world,
				Tags,
				Effects,
				Attributes,
				Events);
			Simulation = new GameplaySimulation(_loop, Abilities, Effects);
		}

		public GameplayAttributeStore Attributes { get; }

		public GameplayTagStore Tags { get; }

		public GameplayEffectSystem Effects { get; }

		public GameplayEventSystem Events { get; }

		public GameplayAbilitySystem Abilities { get; }

		public GameplaySimulation Simulation { get; }

		public void Dispose()
		{
			Simulation.Dispose();
			Abilities.Dispose();
			Events.Dispose();
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			_world.Dispose();
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
