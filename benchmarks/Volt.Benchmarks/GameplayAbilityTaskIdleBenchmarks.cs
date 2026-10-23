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
/// Measures the per-frame cost of advancing a purely dormant task
/// population.
/// </summary>
/// <remarks>
/// The tasks are scheduled far beyond the measured advance, so no
/// deadline is due. The measured operation should be constant time:
/// the task system must not scan one hundred thousand idle tasks per
/// frame.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAbilityTaskIdleBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplayAbilityTaskSystem _tasks = null!;

	[Params(1_000, 10_000, 100_000)]
	public int TaskCount { get; set; }

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
			"VoltBenchmarks.TaskIdle.Ability");

		GameplayAbilityDefinition ability = GameplayAbilityDefinition
			.Create("Bench.TaskIdle.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		Entity actor = _world.CreateEntity();

		GameplayAbilityHandle handle =
			_abilities.GiveAbility(actor, ability);

		Assert(_abilities.TryActivate(handle).Succeeded);
		Assert(_abilities.TryGetActivation(handle, out var activation));

		_tasks = _abilities.Tasks;

		for (int index = 0; index < TaskCount; index++)
		{
			_tasks.WaitDuration(activation, 1_000_000f);
		}
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

	[Benchmark]
	public int Advance_IdlePopulation()
	{
		_tasks.Advance(0.0001f);

		return _tasks.PendingTaskCount;
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The idle task benchmark setup failed.");
		}
	}
}
