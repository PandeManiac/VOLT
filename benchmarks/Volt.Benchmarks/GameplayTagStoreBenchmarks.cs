using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures per-entity tag storage on sparse and large populations.
/// </summary>
/// <remarks>
/// <para>
/// The sparse cases tag one percent of entities, which is the shape
/// most worlds have: a few stunned, burning, or dashing actors among
/// many that carry no gameplay state at all. The dense cases tag every
/// entity and show the cost when the feature is used broadly.
/// </para>
/// <para>
/// Runs in process for the same reason as
/// <see cref="GameplayTagBenchmarks"/>: duplicate benchmark projects
/// in untracked experiment workspaces make BenchmarkDotNet's
/// out-of-process project discovery ambiguous.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayTagStoreBenchmarks
{
	private World _world = null!;
	private GameplayTagStore _store = null!;
	private GameplayTag _tag;
	private GameplayTag _parent;
	private Entity _tagged;
	private Entity _untagged;
	private Entity[] _buffer = [];
	private GameplayTagQuery _query = null!;

	[Params(1_000, 100_000, 1_000_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		GameplayTagRegistry registry = GameplayTagRegistry.Shared;

		_tag = registry.Register("VoltBenchmarks.Entity.Status.Stunned.Deep");
		_parent = registry.Register("VoltBenchmarks.Entity.Status");

		_query = GameplayTagQuery.AllOf(
			GameplayTagQuery.AnyTags(_tag),
			GameplayTagQuery.NoTags(
				registry.Register("VoltBenchmarks.Entity.Status.Immune")));

		_world = new World();
		_store = new GameplayTagStore(_world);

		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			if (index == 0)
			{
				_tagged = entity;
			}
			else if (index == 1)
			{
				_untagged = entity;
			}

			if (index % 100 == 0)
			{
				_store.AddTag(entity, _tag);
			}
		}

		_buffer = new Entity[_store.TaggedEntityCount];
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_store.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public bool HasTag_Hierarchical_Sparse()
	{
		return _store.HasTag(_tagged, _parent);
	}

	[Benchmark]
	public bool HasTag_UntaggedEntity()
	{
		return _store.HasTag(_untagged, _tag);
	}

	[Benchmark]
	public bool MatchesQuery_Sparse()
	{
		return _store.Matches(_tagged, _query);
	}

	[Benchmark]
	public int CopyTaggedEntities_Sparse()
	{
		return _store.CopyTaggedEntities(_buffer);
	}

	[Benchmark]
	public void AddRemove_TaggedEntity()
	{
		_store.AddTag(_untagged, _tag);
		_store.RemoveTag(_untagged, _tag);
	}
}

/// <summary>
/// Measures dense tag participation and the cost of creating and
/// destroying a tagged entity.
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayTagStoreDenseBenchmarks
{
	private World _world = null!;
	private GameplayTagStore _store = null!;
	private GameplayTag _tag;
	private GameplayTag _parent;
	private Entity _tagged;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		GameplayTagRegistry registry = GameplayTagRegistry.Shared;

		_tag = registry.Register("VoltBenchmarks.Dense.Status.Stunned");
		_parent = registry.Register("VoltBenchmarks.Dense.Status");

		_world = new World();
		_store = new GameplayTagStore(_world);

		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			_store.AddTag(entity, _tag);

			if (index == 0)
			{
				_tagged = entity;
			}
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_store.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public bool HasTag_Dense()
	{
		return _store.HasTag(_tagged, _parent);
	}

	[Benchmark]
	public void CreateDestroy_Untagged()
	{
		Entity entity = _world.CreateEntity();

		_world.DestroyEntity(entity);
	}

	[Benchmark]
	public void CreateTagDestroy_Tagged()
	{
		Entity entity = _world.CreateEntity();

		_store.AddTag(entity, _tag);
		_world.DestroyEntity(entity);
	}
}
