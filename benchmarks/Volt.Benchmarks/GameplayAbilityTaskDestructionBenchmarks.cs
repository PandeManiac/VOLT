using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Benchmarks;

/// <summary>
/// Measures destroying an entity whose activation still owns many
/// pending tasks.
/// </summary>
/// <remarks>
/// Each iteration creates a fresh actor, grants and activates the
/// ability, starts the batch, and then the measured operation destroys
/// the actor. Entity destruction must cancel the activation, dispose
/// the tasks, and remove their event, tag, and deadline state. A
/// single invocation per iteration keeps setup and measurement paired;
/// allocation is asserted by the lifecycle allocation guards instead of
/// the in-process harness's per-invocation memory column.
/// </remarks>
[Config(typeof(Config))]
public class GameplayAbilityTaskDestructionBenchmarks
{
	private sealed class Config : ManualConfig
	{
		public Config()
		{
			AddJob(Job.InProcess
				.WithInvocationCount(1)
				.WithUnrollFactor(1));
		}
	}

	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplayAbilityTaskSystem _tasks = null!;
	private GameplayAbilityDefinition _ability = null!;
	private GameplayAbilityHandle _handle;
	private Entity _actor;

	[Params(1_000, 10_000)]
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
			"VoltBenchmarks.TaskDestruction.Ability");

		_ability = GameplayAbilityDefinition
			.Create("Bench.TaskDestruction.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

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

	[IterationSetup]
	public void StartBatch()
	{
		_actor = _world.CreateEntity();

		_handle = _abilities.GiveAbility(_actor, _ability);

		Assert(_abilities.TryActivate(_handle).Succeeded);
		Assert(_abilities.TryGetActivation(_handle, out var activation));

		for (int index = 0; index < TaskCount; index++)
		{
			_tasks.WaitDuration(activation, 0f);
		}
	}

	[Benchmark]
	public bool DestroyOwnerWithTasks()
	{
		return _world.DestroyEntity(_actor);
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The task destruction benchmark setup failed.");
		}
	}
}
