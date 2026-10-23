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
/// Measures the cost of a tag mutation while an entity has many
/// tag-condition tasks, both relevant and irrelevant to the changed
/// tag.
/// </summary>
/// <remarks>
/// The task runtime indexes waits by watched tag, so a mutation should
/// visit only the bucket for the changed tag (exact) or that tag and
/// its ancestors (hierarchical). This benchmark keeps every task
/// pending and never advances, so the measured work is the tag index
/// and the tag store notification, not task completion.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayTagWaitScalingBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEventSystem _events = null!;
	private GameplayAbilitySystem _abilities = null!;
	private GameplayAbilityTaskSystem _tasks = null!;
	private GameplayAbilityActivation _activation;
	private Entity _actor;
	private GameplayTag[] _watched = null!;
	private GameplayTag _irrelevant;

	[Params(1, 64, 1_024, 10_000)]
	public int WatchedTagCount { get; set; }

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
			"VoltBenchmarks.TagWait.Ability");

		GameplayAbilityDefinition ability = GameplayAbilityDefinition
			.Create("Bench.TagWait.Ability")
			.AssetTags(assetTag)
			.EndOnActivate(false)
			.Build();

		_actor = _world.CreateEntity();

		GameplayAbilityHandle handle = _abilities.GiveAbility(
			_actor,
			ability);

		Assert(_abilities.TryActivate(handle).Succeeded);
		Assert(_abilities.TryGetActivation(handle, out _activation));

		_tasks = _abilities.Tasks;
		_watched = new GameplayTag[WatchedTagCount];

		for (int index = 0; index < _watched.Length; index++)
		{
			GameplayTag tag = GameplayTagRegistry.Shared.Register(
				$"VoltBenchmarks.TagWait.Watched{index}");

			_watched[index] = tag;

			GameplayAbilityTaskHandle task = _tasks.WaitTag(
				_activation,
				tag,
				GameplayTagWaitMode.Present,
				exact: true);

			Assert(!task.IsNone);
		}

		_irrelevant = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.TagWait.Irrelevant");
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
	public bool RelevantTagChange()
	{
		GameplayTag tag = _watched[0];

		return _tags.AddTag(_actor, tag)
			&& _tags.RemoveTag(_actor, tag);
	}

	[Benchmark]
	public bool IrrelevantTagChange()
	{
		return _tags.AddTag(_actor, _irrelevant)
			&& _tags.RemoveTag(_actor, _irrelevant);
	}

	private static void Assert(bool condition)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				"The tag-wait benchmark setup failed.");
		}
	}
}
