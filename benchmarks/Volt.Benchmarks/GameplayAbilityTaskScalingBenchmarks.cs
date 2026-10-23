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
/// Measures batch task expiry, cancellation, ability ending, and owner
/// destruction at realistic populations.
/// </summary>
/// <remarks>
/// Each iteration starts a fresh batch in <see cref="StartBatch"/>,
/// so the measured operation only covers the batch under test. The
/// population is a parameter, which exposes scaling rather than a
/// single point. A single invocation per iteration keeps the setup
/// and the measured operation paired one to one. That pairing also
/// makes the in-process harness's per-invocation allocation visible in
/// a memory column, so allocation for these batch paths is asserted by
/// the allocation guards instead.
/// </remarks>
[Config(typeof(Config))]
public class GameplayAbilityTaskScalingBenchmarks
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
	private GameplayAbilityActivation _activation;
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
			"VoltBenchmarks.TaskScaling.Ability");

		_ability = GameplayAbilityDefinition
			.Create("Bench.TaskScaling.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		_actor = _world.CreateEntity();

		_handle = _abilities.GiveAbility(_actor, _ability);

		Assert(_abilities.TryActivate(_handle).Succeeded);
		Assert(_abilities.TryGetActivation(_handle, out _activation));

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
		if (!_abilities.IsActive(_handle))
		{
			Assert(_abilities.TryActivate(_handle).Succeeded);
		}

		Assert(_abilities.TryGetActivation(_handle, out _activation));

		for (int index = 0; index < TaskCount; index++)
		{
			_tasks.WaitDuration(_activation, 0f);
		}
	}

	[Benchmark]
	public int ExpireBatch()
	{
		_tasks.Advance(0.001f);

		return _tasks.PendingTaskCount;
	}

	[Benchmark]
	public int CancelBatch()
	{
		return _tasks.CancelAll(_actor);
	}

	[Benchmark]
	public bool EndAbilityWithTasks()
	{
		return _abilities.End(_handle);
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The task scaling benchmark setup failed.");
		}
	}
}
