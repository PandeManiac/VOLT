using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Simulation;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Tests.Simulation;

/// <summary>
/// A world, gameplay services, fixed-step loop, and fake clock wired
/// together for integration tests.
/// </summary>
internal sealed class GameplaySimulationRuntime : IDisposable
{
	public const long FrameNanoseconds = 16_666_667;

	public GameplaySimulationRuntime(GameLoopSettings? settings = null)
	{
		Clock = new FakeMonotonicClock();
		Loop = new GameLoop(settings ?? GameLoopSettings.Default, Clock);
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
		Simulation = new GameplaySimulation(Loop, Abilities, Effects);
	}

	public FakeMonotonicClock Clock { get; }

	public GameLoop Loop { get; }

	public World World { get; }

	public GameplayAttributeStore Attributes { get; }

	public GameplayTagStore Tags { get; }

	public GameplayEffectSystem Effects { get; }

	public GameplayEventSystem Events { get; }

	public GameplayAbilitySystem Abilities { get; }

	public GameplaySimulation Simulation { get; }

	public GameplayAbilityTaskSystem Tasks => Abilities.Tasks;

	public Entity CreateActor()
	{
		return World.CreateEntity();
	}

	/// <summary>
	/// Advances one application frame with exactly one due fixed step.
	/// </summary>
	public FrameTiming Step()
	{
		Clock.AdvanceNanoseconds(FrameNanoseconds);

		return Loop.Advance();
	}

	public void Step(int count)
	{
		for (int index = 0; index < count; index++)
		{
			Step();
		}
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

	internal sealed class FakeMonotonicClock : IMonotonicClock
	{
		public const long NanosecondsPerSecond = 1_000_000_000;

		public long Timestamp { get; private set; }

		public long TicksPerSecond => NanosecondsPerSecond;

		public void AdvanceNanoseconds(long nanoseconds)
		{
			Timestamp += nanoseconds;
		}
	}
}
