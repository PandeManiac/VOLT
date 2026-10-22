using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures gameplay effect application, idle stepping, and periodic
/// ticking.
/// </summary>
/// <remarks>
/// <para>
/// The workloads mirror the specification's cost model: an idle effect
/// population must cost one comparison per step, a ticking population
/// must cost one execution per due effect, and applying or removing an
/// effect must not allocate once the slot arrays are warm.
/// </para>
/// <para>
/// Runs in process for the same reason as the tag benchmarks: duplicate
/// benchmark projects in untracked experiment workspaces make
/// BenchmarkDotNet's out-of-process project discovery ambiguous.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayEffectBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;

	private GameplayEffectDefinition _duration = null!;
	private GameplayEffectDefinition _periodic = null!;
	private GameplayEffectDefinition _stacking = null!;
	private GameplayEffectDefinition _instant = null!;

	private GameplayAttribute _attribute;
	private GameplayTag _grantedTag;

	private Entity _applyTarget;
	private Entity _stackTarget;
	private Entity _tickTarget;
	private Entity _blockedTarget;
	private Entity _source;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_attribute = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Effects.Value");

		GameplayTag granted = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Effects.Status.Buff");

		_grantedTag = granted;

		GameplayTag required = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Effects.Status.Required");

		// The durations are effectively endless: the benchmark harness
		// invokes each method many times, and a short duration would
		// expire mid-run and leave later iterations measuring an empty
		// system.
		_duration = GameplayEffectDefinition
			.Create("Bench.Duration")
			.HasDuration(1_000_000_000f)
			.Modifier(_attribute, GameplayModifierOperation.AddBase, 5f)
			.GrantTags(granted)
			.Build();

		_periodic = GameplayEffectDefinition
			.Create("Bench.Periodic")
			.HasDuration(1_000_000_000f)
			.Period(1f)
			.Modifier(_attribute, GameplayModifierOperation.AddBase, -0.001f)
			.Build();

		_stacking = GameplayEffectDefinition
			.Create("Bench.Stacking")
			.HasDuration(1_000_000_000f)
			.Modifier(_attribute, GameplayModifierOperation.AddBase, -0.001f)
			.GrantTags(granted)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 10)
			.Build();

		_instant = GameplayEffectDefinition
			.Create("Bench.Instant")
			.Instant()
			.Modifier(_attribute, GameplayModifierOperation.AddBase, -0.001f)
			.RequireToApply(
				GameplayTagRequirements.Ignore(
					GameplayTagSet.Create(required)))
			.Build();

		_world = new World();
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);

		_source = _world.CreateEntity();
		_applyTarget = _world.CreateEntity();
		_stackTarget = _world.CreateEntity();
		_tickTarget = _world.CreateEntity();
		_blockedTarget = _world.CreateEntity();

		_effects.Apply(_stackTarget, _stacking, _source);
		_effects.Apply(_tickTarget, _periodic, _source);

		// Warm every path once so the measured iterations do not pay
		// for first-use array growth.
		GameplayEffectHandle warm =
			_effects.Apply(_applyTarget, _duration, _source).Handle;

		_effects.Remove(warm);
		_effects.Apply(_applyTarget, _instant, _source);
		_effects.Apply(_stackTarget, _stacking, _source);
		_tags.AddTag(_blockedTarget, required);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int ApplyAndRemove_DurationEffect()
	{
		GameplayEffectHandle handle =
			_effects.Apply(_applyTarget, _duration, _source).Handle;

		_effects.Remove(handle);

		return (int)handle.Generation;
	}

	[Benchmark]
	public bool Apply_InstantEffect()
	{
		return _effects.Apply(_applyTarget, _instant, _source).Applied;
	}

	[Benchmark]
	public int ApplyStack_AtTheLimit()
	{
		return _effects.Apply(_stackTarget, _stacking, _source).StackCount;
	}

	[Benchmark]
	public int Apply_RejectedByRequirements()
	{
		return (int)_effects.Apply(
			_blockedTarget,
			_instant,
			_source).Status;
	}

	[Benchmark]
	public void Advance_OnePeriodicTick()
	{
		_effects.Advance(1f);
	}

	[Benchmark]
	public double ReadAttribute_WithActiveEffect()
	{
		return _attributes.GetCurrentValue(_stackTarget, _attribute);
	}

	[Benchmark]
	public bool HasTag_GrantedByActiveEffect()
	{
		return _tags.HasTag(_stackTarget, _grantedTag);
	}
}

/// <summary>
/// Measures a populated effect system: many idle instances and many
/// simultaneous periodic ticks.
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayEffectDenseBenchmarks
{
	private World _world = null!;
	private World _idleWorld = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayEffectSystem _idleEffects = null!;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		GameplayAttribute attribute = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Effects.DenseValue");

		GameplayEffectDefinition idle = GameplayEffectDefinition
			.Create("Bench.DenseIdle")
			.HasDuration(1_000_000_000f)
			.Modifier(attribute, GameplayModifierOperation.AddBase, 1f)
			.Build();

		_world = new World();
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);

		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			_effects.Apply(entity, idle);
		}

		// A second world of the same size with no gameplay state at
		// all, to show what the subsystem costs when it is unused.
		_idleWorld = new World();
		_idleEffects = new GameplayEffectSystem(
			_idleWorld,
			new GameplayAttributeStore(_idleWorld),
			new GameplayTagStore(_idleWorld));

		for (int index = 0; index < EntityCount; index++)
		{
			_idleWorld.CreateEntity();
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();

		_idleEffects.Dispose();
		_idleWorld.Dispose();
	}

	[Benchmark]
	public void Advance_AllEffectsIdle()
	{
		_effects.Advance(0.1f);
	}

	[Benchmark]
	public void Advance_NoGameplayState()
	{
		_idleEffects.Advance(0.1f);
	}

	[Benchmark]
	public int ActiveEffectCount()
	{
		return _effects.ActiveEffectCount;
	}
}
