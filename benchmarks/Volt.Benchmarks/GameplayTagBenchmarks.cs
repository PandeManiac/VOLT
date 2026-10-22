using BenchmarkDotNet.Attributes;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures gameplay tag matching, query evaluation, authoring, and
/// name resolution.
/// </summary>
/// <remarks>
/// <para>
/// Matching benchmarks run on warmed sets and containers, which is the
/// case that matters for gameplay: definitions are built once and
/// requirements are evaluated on ability activation or effect
/// application. Construction benchmarks are included separately so the
/// cost of authoring is visible instead of being folded into a hot
/// path.
/// </para>
/// <para>
/// The allocation column is the interesting result for the matching
/// cases: the design goal is zero managed bytes for every warmed-up
/// lookup, including hierarchical and nested-query evaluation.
/// </para>
/// <para>
/// These run in process. BenchmarkDotNet's out-of-process generator
/// locates a benchmark project by assembly name under the working
/// directory, and this checkout contains untracked experiment
/// workspaces with copies of <c>Volt.Benchmarks.csproj</c>, which
/// makes that lookup ambiguous. Removing those copies would allow the
/// default toolchain; until then the numbers below are still measured
/// by BenchmarkDotNet's harness, only without a separate host process.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayTagBenchmarks
{
	private GameplayTag[] _bulk = null!;
	private GameplayTag _deepLeaf;
	private GameplayTag _root;
	private GameplayTag _unrelated;

	private GameplayTagSet _denseSet = null!;
	private GameplayTagSet _sparseSet = null!;
	private GameplayTagSet _requirement = null!;
	private GameplayTagSet _filter = null!;
	private GameplayTagQuery _nestedQuery = null!;

	private GameplayTagContainer _container = null!;

	[GlobalSetup]
	public void Setup()
	{
		GameplayTagRegistry registry = GameplayTagRegistry.Shared;

		_root = registry.Register("VoltBenchmarks.Status");
		_deepLeaf = registry.Register(
			"VoltBenchmarks.Status.Stunned.Deep.Leaf");
		_unrelated = registry.Register("VoltBenchmarks.Ability.Dash");

		_bulk = new GameplayTag[40];

		for (int index = 0; index < _bulk.Length; index++)
		{
			_bulk[index] = registry.Register(
				$"VoltBenchmarks.Bulk.Tag{index}");
		}

		_denseSet = GameplayTagSet.Create(_bulk);
		_sparseSet = GameplayTagSet.Create(_deepLeaf);

		_requirement = GameplayTagSet.Create(_bulk[0], _bulk[7]);
		_filter = GameplayTagSet.Create(_root);

		_nestedQuery = GameplayTagQuery.AllOf(
			GameplayTagQuery.AnyOf(
				GameplayTagQuery.AllTags(_bulk[0], _bulk[1]),
				GameplayTagQuery.AllTags(_deepLeaf)),
			GameplayTagQuery.NoTags(_unrelated));

		_container = new GameplayTagContainer(_bulk);
	}

	[Benchmark]
	public bool SparseHasTagExact()
	{
		return _sparseSet.HasTagExact(_deepLeaf);
	}

	[Benchmark]
	public bool DenseHasTagExact()
	{
		return _denseSet.HasTagExact(_bulk[39]);
	}

	[Benchmark]
	public bool SparseHasTagHierarchical()
	{
		return _sparseSet.HasTag(_root);
	}

	[Benchmark]
	public bool DenseHasTagHierarchical()
	{
		return _denseSet.HasTag(_root);
	}

	[Benchmark]
	public bool DenseHasAny()
	{
		return _denseSet.HasAny(_requirement);
	}

	[Benchmark]
	public bool DenseHasAll()
	{
		return _denseSet.HasAll(_requirement);
	}

	[Benchmark]
	public bool DenseFilter()
	{
		return _denseSet.Filter(_filter).Count == 0;
	}

	[Benchmark]
	public bool NestedQuery()
	{
		return _nestedQuery.Matches(_denseSet);
	}

	[Benchmark]
	public bool ContainerHasTag()
	{
		return _container.HasTag(_root);
	}

	[Benchmark]
	public bool ContainerHasAll()
	{
		return _container.HasAll(_requirement);
	}

	[Benchmark]
	public void ContainerAddRemove()
	{
		GameplayTag tag = _bulk[3];

		_container.Remove(tag);
		_container.Add(tag);
	}

	[Benchmark]
	public GameplayTagSet BuildSet40()
	{
		return GameplayTagSet.Create(_bulk);
	}

	[Benchmark]
	public bool RegistryTryGetTag()
	{
		return GameplayTagRegistry.Shared.TryGetTag(
			"VoltBenchmarks.Status.Stunned",
			out _);
	}

	[Benchmark]
	public GameplayTag RegisterExistingTag()
	{
		return GameplayTagRegistry.Shared.Register("VoltBenchmarks.Status");
	}
}
