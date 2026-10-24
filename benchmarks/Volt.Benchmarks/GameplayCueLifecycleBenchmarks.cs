using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures cue matching breadth, persistent cue populations, and the
/// effect-to-cue transitions that a large simulation pays for. The
/// population parameters cover the 1,000, 10,000, and 100,000 scale
/// points; every row runs in process so the measurement does not
/// depend on a separate toolchain build.
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayCueLifecycleBenchmarks
{
	private static readonly Action<GameplayCueEvent> Handler = OnCue;
	private static int s_sink;

	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayCueSystem _cues = null!;
	private GameplayCueSystem _hierarchical = null!;
	private GameplayCueSystem _irrelevant = null!;
	private GameplayCueSystem _persistent = null!;
	private GameplayEffectDefinition _cueEffect = null!;
	private GameplayTag _root;
	private GameplayTag _descendant;
	private GameplayTag _other;
	private Entity[] _entities = [];
	private GameplayCueHandle[] _handles = [];
	private GameplayCueHandle _updated;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);
		_cues = new GameplayCueSystem(_world);
		_cues.BindEffects(_effects);
		_hierarchical = new GameplayCueSystem(_world);
		_irrelevant = new GameplayCueSystem(_world);
		_persistent = new GameplayCueSystem(_world);
		_root = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.Root");
		_descendant = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Cues.Root.Descendant");
		_other = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.Other");
		_hierarchical.Subscribe(_root, Handler, GameplayCueMatch.Hierarchical);

		for (int index = 0; index < 1_000; index++)
		{
			_irrelevant.Subscribe(_other, Handler);
		}

		_cueEffect = GameplayEffectDefinition
			.Create("VoltBenchmarks.Cues.Effect")
			.HasDuration(1f)
			.GameplayCue(_descendant)
			.Build();
		_handles = new GameplayCueHandle[Population];
		_updated = _persistent.Add(_descendant, magnitude: 1f, stackCount: 1);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_cues.Dispose();
		_hierarchical.Dispose();
		_irrelevant.Dispose();
		_persistent.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int Execute_HierarchicalListener() => _hierarchical.Execute(_descendant);

	[Benchmark]
	public int Execute_UnrelatedListeners() => _irrelevant.Execute(_descendant);

	[Benchmark]
	public bool UpdatePersistentCue() =>
		_persistent.Update(_updated, magnitude: 2f, stackCount: 2);

	[Benchmark]
	public int AddPersistentCues()
	{
		GameplayCueSystem cues = _persistent;

		for (int index = 0; index < Population; index++)
		{
			_handles[index] = cues.Add(_descendant);
		}

		return cues.ActiveCueCount;
	}

	[IterationCleanup(Target = nameof(AddPersistentCues))]
	public void CleanupAddPersistentCues()
	{
		for (int index = 0; index < Population; index++)
		{
			_persistent.Remove(_handles[index]);
		}
	}

	[IterationSetup(Target = nameof(RemovePersistentCues))]
	public void SetupRemovePersistentCues()
	{
		for (int index = 0; index < Population; index++)
		{
			_handles[index] = _persistent.Add(_descendant);
		}
	}

	[Benchmark]
	public int RemovePersistentCues()
	{
		int removed = 0;

		for (int index = 0; index < Population; index++)
		{
			if (_persistent.Remove(_handles[index]))
			{
				removed++;
			}
		}

		return removed;
	}

	[IterationSetup(Target = nameof(ApplyAndExpireCueEffects))]
	public void SetupApplyAndExpireCueEffects()
	{
		_entities = new Entity[Population];

		for (int index = 0; index < Population; index++)
		{
			_entities[index] = _world.CreateEntity();
		}
	}

	[Benchmark]
	public int ApplyAndExpireCueEffects()
	{
		for (int index = 0; index < Population; index++)
		{
			_effects.Apply(_entities[index], _cueEffect);
		}

		_effects.Advance(2f);
		int active = _cues.ActiveCueCount;

		for (int index = 0; index < Population; index++)
		{
			_world.DestroyEntity(_entities[index]);
		}

		_entities = [];
		return active;
	}

	[IterationSetup(Target = nameof(DestroyEntitiesWithCues))]
	public void SetupDestroyEntitiesWithCues()
	{
		_entities = new Entity[Population];

		for (int index = 0; index < Population; index++)
		{
			Entity entity = _world.CreateEntity();
			_entities[index] = entity;
			_effects.Apply(entity, _cueEffect);
		}
	}

	[Benchmark]
	public int DestroyEntitiesWithCues()
	{
		int destroyed = 0;

		for (int index = 0; index < Population; index++)
		{
			if (_world.DestroyEntity(_entities[index]))
			{
				destroyed++;
			}
		}

		_entities = [];
		return destroyed;
	}

	private static void OnCue(GameplayCueEvent cue) =>
		s_sink += (int)cue.Tag.Id;
}
