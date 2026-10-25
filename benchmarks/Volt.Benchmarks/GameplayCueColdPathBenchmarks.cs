using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures first use against a fresh cue runtime. Iteration setup and cleanup
/// are outside the measured operation; every workload iteration receives new
/// internal cue storage. Process startup, JIT compilation, and shared registry
/// initialization are deliberately excluded.
/// </summary>
/// <remarks>
/// The in-process BenchmarkDotNet job may warm the benchmark method and CLR
/// code. "Cold" here means the measured runtime's relevant internal state is
/// new for every invocation, not that each sample starts a new OS process.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayCueColdPathBenchmarks
{
	private static readonly Action<GameplayCueEvent> Handler = OnCue;
	private static int s_sink;

	private GameplayTag _tag;
	private GameplayTag _hierarchyRoot;
	private GameplayTag _hierarchyDescendant;
	private GameplayTag[] _dictionaryTags = [];
	private GameplayCueEvent _event;
	private GameplayEffectDefinition _cueEffect = null!;
	private GameplayAbilityDefinition _cueAbility = null!;
	private CueRuntime? _runtime;
	private EffectRuntime? _effectRuntime;
	private AbilityRuntime? _abilityRuntime;
	private GameplayCueBuffer? _buffer;
	private GameplayCueHandle _cue;
	private GameplayEffectHandle _effect;
	private CueRuntime[] _independentRuntimes = [];

	[GlobalSetup]
	public void DefineSharedInputs()
	{
		_tag = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath");
		_hierarchyRoot = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Hierarchical");
		_hierarchyDescendant = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Hierarchical.Child");
		_dictionaryTags =
		[
			GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Dictionary.A"),
			GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Dictionary.B"),
			GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Dictionary.C"),
			GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Dictionary.D")
		];
		_event = new GameplayCueEvent(
			GameplayCueKind.Execute,
			_tag,
			default,
			default,
			default,
			default,
			1f,
			0,
			default,
			default);
		_cueEffect = GameplayEffectDefinition
			.Create("Bench.ColdCue.Effect")
			.HasDuration(1f)
			.GameplayCue(_tag)
			.Build();
		GameplayTag abilityTag = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPath.Ability");
		GameplayAbilityPlan plan = GameplayAbilityPlan.Create()
			.AddCue(_tag)
			.Wait(60f)
			.Build();
		_cueAbility = GameplayAbilityDefinition
			.Create("Bench.ColdCue.Ability")
			.AssetTags(abilityTag)
			.Plan(plan)
			.Build();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_runtime?.Dispose();
		_effectRuntime?.Dispose();
		_abilityRuntime?.Dispose();
		_buffer?.Dispose();
		DisposeIndependentRuntimes();
	}

	/// <summary>Measures construction of a world and its first cue system; disposal is outside the timed operation.</summary>
	[Benchmark]
	public int FirstConstructionAndInitialization()
	{
		_runtime = new CueRuntime();
		return _runtime.Cues.ActiveCueCount;
	}

	[IterationCleanup(Target = nameof(FirstConstructionAndInitialization))]
	public void CleanupFirstConstructionAndInitialization() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstExecuteWithoutListeners))]
	public void SetupFirstExecuteWithoutListeners() => _runtime = new CueRuntime();

	/// <summary>Measures the first no-listener execute; tag registration is excluded.</summary>
	[Benchmark]
	public int FirstExecuteWithoutListeners() => _runtime!.Cues.Execute(_tag);

	[IterationCleanup(Target = nameof(FirstExecuteWithoutListeners))]
	public void CleanupFirstExecuteWithoutListeners() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstExecuteWithOneListener))]
	public void SetupFirstExecuteWithOneListener()
	{
		_runtime = new CueRuntime();
		_runtime.Cues.Subscribe(_tag, Handler);
	}

	/// <summary>Subscription creation is setup; this is the first one-listener dispatch.</summary>
	[Benchmark]
	public int FirstExecuteWithOneListener() => _runtime!.Cues.Execute(_tag);

	[IterationCleanup(Target = nameof(FirstExecuteWithOneListener))]
	public void CleanupFirstExecuteWithOneListener() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstHierarchicalDispatch))]
	public void SetupFirstHierarchicalDispatch()
	{
		_runtime = new CueRuntime();
		_runtime.Cues.Subscribe(_hierarchyRoot, Handler, GameplayCueMatch.Hierarchical);
	}

	/// <summary>Measures first ancestor lookup and hierarchical listener capture.</summary>
	[Benchmark]
	public int FirstHierarchicalDispatch() => _runtime!.Cues.Execute(_hierarchyDescendant);

	[IterationCleanup(Target = nameof(FirstHierarchicalDispatch))]
	public void CleanupFirstHierarchicalDispatch() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstPersistentCueAdd))]
	public void SetupFirstPersistentCueAdd() => _runtime = new CueRuntime();

	/// <summary>Measures first persistent slot allocation and Added dispatch.</summary>
	[Benchmark]
	public GameplayCueHandle FirstPersistentCueAdd() => _runtime!.Cues.Add(_tag);

	[IterationCleanup(Target = nameof(FirstPersistentCueAdd))]
	public void CleanupFirstPersistentCueAdd() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstPersistentCueUpdate))]
	public void SetupFirstPersistentCueUpdate()
	{
		_runtime = new CueRuntime();
		_cue = _runtime.Cues.Add(_tag);
	}

	/// <summary>Add is setup; measures first Updated transition dispatch.</summary>
	[Benchmark]
	public bool FirstPersistentCueUpdate() => _runtime!.Cues.Update(_cue, 2f, 2);

	[IterationCleanup(Target = nameof(FirstPersistentCueUpdate))]
	public void CleanupFirstPersistentCueUpdate() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstPersistentCueRemove))]
	public void SetupFirstPersistentCueRemove()
	{
		_runtime = new CueRuntime();
		_cue = _runtime.Cues.Add(_tag);
	}

	/// <summary>Add is setup; measures first cue retirement and Removed dispatch.</summary>
	[Benchmark]
	public bool FirstPersistentCueRemove() => _runtime!.Cues.Remove(_cue);

	[IterationCleanup(Target = nameof(FirstPersistentCueRemove))]
	public void CleanupFirstPersistentCueRemove() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstSubscriptionCreation))]
	public void SetupFirstSubscriptionCreation() => _runtime = new CueRuntime();

	/// <summary>Tag registration is excluded; measures first listener/subscription storage.</summary>
	[Benchmark]
	public GameplayCueSubscription FirstSubscriptionCreation() =>
		_runtime!.Cues.Subscribe(_tag, Handler);

	[IterationCleanup(Target = nameof(FirstSubscriptionCreation))]
	public void CleanupFirstSubscriptionCreation() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstDictionaryExpansion))]
	public void SetupFirstDictionaryExpansion()
	{
		_runtime = new CueRuntime();
		_runtime.Cues.Subscribe(_dictionaryTags[0], Handler);
		_runtime.Cues.Subscribe(_dictionaryTags[1], Handler);
		_runtime.Cues.Subscribe(_dictionaryTags[2], Handler);
	}

	/// <summary>Three distinct tags fill the initial Dictionary capacity; measures the fourth insertion.</summary>
	[Benchmark]
	public GameplayCueSubscription FirstDictionaryExpansion() =>
		_runtime!.Cues.Subscribe(_dictionaryTags[3], Handler);

	[IterationCleanup(Target = nameof(FirstDictionaryExpansion))]
	public void CleanupFirstDictionaryExpansion() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstCueSlotExpansion))]
	public void SetupFirstCueSlotExpansion()
	{
		_runtime = new CueRuntime();
		for (int index = 0; index < 16; index++)
		{
			_runtime.Cues.Add(_tag);
		}
	}

	/// <summary>Sixteen cues fill the initial cue-slot array; measures cue seventeen.</summary>
	[Benchmark]
	public GameplayCueHandle FirstCueSlotExpansion() => _runtime!.Cues.Add(_tag);

	[IterationCleanup(Target = nameof(FirstCueSlotExpansion))]
	public void CleanupFirstCueSlotExpansion() => DisposeRuntime();

	[IterationSetup(Target = nameof(FirstPresentationEnqueueAndDrain))]
	public void SetupFirstPresentationEnqueueAndDrain()
	{
		_runtime = new CueRuntime();
		_buffer = new GameplayCueBuffer(1, 16);
		_runtime.Cues.PresentationBuffer = _buffer;
	}

	/// <summary>Buffer and cue system creation are setup; measures the first cue dispatch, presentation enqueue, and drain without growth.</summary>
	[Benchmark]
	public int FirstPresentationEnqueueAndDrain()
	{
		_runtime!.Cues.Execute(_tag);
		Span<GameplayCueEvent> output = stackalloc GameplayCueEvent[1];
		return _buffer!.Drain(output);
	}

	[IterationCleanup(Target = nameof(FirstPresentationEnqueueAndDrain))]
	public void CleanupFirstPresentationEnqueueAndDrain()
	{
		DisposeRuntime();
		DisposeBuffer();
	}

	[IterationSetup(Target = nameof(FirstPresentationBufferExpansion))]
	public void SetupFirstPresentationBufferExpansion()
	{
		_buffer = new GameplayCueBuffer(1, 16);
		_buffer.Enqueue(_event);
	}

	/// <summary>One queued event fills capacity; measures the second enqueue and geometric growth.</summary>
	[Benchmark]
	public int FirstPresentationBufferExpansion()
	{
		_buffer!.Enqueue(_event);
		return _buffer.Capacity;
	}

	[IterationCleanup(Target = nameof(FirstPresentationBufferExpansion))]
	public void CleanupFirstPresentationBufferExpansion() => DisposeBuffer();

	[IterationSetup(Target = nameof(FirstEffectBackedCueCreation))]
	public void SetupFirstEffectBackedCueCreation() => _effectRuntime = new EffectRuntime();

	/// <summary>Stores, definition, binding, and entity are setup; measures first effect apply plus cue creation.</summary>
	[Benchmark]
	public int FirstEffectBackedCueCreation() =>
		_effectRuntime!.Effects.Apply(_effectRuntime.Target, _cueEffect).Applied ? 1 : 0;

	[IterationCleanup(Target = nameof(FirstEffectBackedCueCreation))]
	public void CleanupFirstEffectBackedCueCreation() => DisposeEffectRuntime();

	[IterationSetup(Target = nameof(FirstEffectBackedCueExpiration))]
	public void SetupFirstEffectBackedCueExpiration()
	{
		_effectRuntime = new EffectRuntime();
		_effect = _effectRuntime.Effects.Apply(_effectRuntime.Target, _cueEffect).Handle;
	}

	/// <summary>Effect and cue creation are setup; measures first scheduled expiry and cue removal.</summary>
	[Benchmark]
	public int FirstEffectBackedCueExpiration()
	{
		_effectRuntime!.Effects.Advance(2f);
		return _effectRuntime.Cues.ActiveCueCount;
	}

	[IterationCleanup(Target = nameof(FirstEffectBackedCueExpiration))]
	public void CleanupFirstEffectBackedCueExpiration() => DisposeEffectRuntime();

	[IterationSetup(Target = nameof(FirstEffectBackedCueRemoval))]
	public void SetupFirstEffectBackedCueRemoval()
	{
		_effectRuntime = new EffectRuntime();
		_effect = _effectRuntime.Effects.Apply(_effectRuntime.Target, _cueEffect).Handle;
	}

	/// <summary>Effect and cue creation are setup; measures explicit effect removal and cue retirement.</summary>
	[Benchmark]
	public bool FirstEffectBackedCueRemoval() => _effectRuntime!.Effects.Remove(_effect);

	[IterationCleanup(Target = nameof(FirstEffectBackedCueRemoval))]
	public void CleanupFirstEffectBackedCueRemoval() => DisposeEffectRuntime();

	[IterationSetup(Target = nameof(FirstActivationOwnedCueCreation))]
	public void SetupFirstActivationOwnedCueCreation()
	{
		_abilityRuntime = new AbilityRuntime(_cueAbility);
	}

	/// <summary>Definition, stores, grant, and owner are setup; includes first activation, cue system, cue, and wait task.</summary>
	[Benchmark]
	public GameplayAbilityActivationStatus FirstActivationOwnedCueCreation() =>
		_abilityRuntime!.Abilities.TryActivate(_abilityRuntime.Grant).Status;

	[IterationCleanup(Target = nameof(FirstActivationOwnedCueCreation))]
	public void CleanupFirstActivationOwnedCueCreation() => DisposeAbilityRuntime();

	[IterationSetup(Target = nameof(FirstActivationOwnedCueCleanup))]
	public void SetupFirstActivationOwnedCueCleanup()
	{
		_abilityRuntime = new AbilityRuntime(_cueAbility);
		if (!_abilityRuntime.Abilities.TryActivate(_abilityRuntime.Grant).Succeeded
			|| _abilityRuntime.Abilities.Cues.ActiveCueCount != 1)
		{
			throw new InvalidOperationException("The activation-owned cue benchmark setup did not create its cue.");
		}
	}

	/// <summary>Activation and cue creation are setup; measures first End and activation-owned cue cleanup.</summary>
	[Benchmark]
	public bool FirstActivationOwnedCueCleanup() => _abilityRuntime!.Abilities.End(_abilityRuntime.Grant);

	[IterationCleanup(Target = nameof(FirstActivationOwnedCueCleanup))]
	public void CleanupFirstActivationOwnedCueCleanup() => DisposeAbilityRuntime();

	[IterationSetup(Target = nameof(RepeatedIndependentColdInitialization))]
	public void SetupRepeatedIndependentColdInitialization() => _independentRuntimes = new CueRuntime[16];

	/// <summary>Constructs sixteen fresh independent world/cue runtimes; reports one per instance.</summary>
	[Benchmark(OperationsPerInvoke = 16)]
	public int RepeatedIndependentColdInitialization()
	{
		for (int index = 0; index < _independentRuntimes.Length; index++)
		{
			_independentRuntimes[index] = new CueRuntime();
		}

		return _independentRuntimes.Length;
	}

	[IterationCleanup(Target = nameof(RepeatedIndependentColdInitialization))]
	public void CleanupRepeatedIndependentColdInitialization() => DisposeIndependentRuntimes();

	private void DisposeRuntime()
	{
		_runtime?.Dispose();
		_runtime = null;
	}

	private void DisposeEffectRuntime()
	{
		_effectRuntime?.Dispose();
		_effectRuntime = null;
	}

	private void DisposeAbilityRuntime()
	{
		_abilityRuntime?.Dispose();
		_abilityRuntime = null;
	}

	private void DisposeBuffer()
	{
		_buffer?.Dispose();
		_buffer = null;
	}

	private void DisposeIndependentRuntimes()
	{
		foreach (CueRuntime? runtime in _independentRuntimes)
		{
			runtime?.Dispose();
		}

		_independentRuntimes = [];
	}

	private static void OnCue(GameplayCueEvent cueEvent) => s_sink += (int)cueEvent.Tag.Id;

	private sealed class CueRuntime : IDisposable
	{
		public CueRuntime()
		{
			World = new World();
			Cues = new GameplayCueSystem(World);
		}

		public World World { get; }
		public GameplayCueSystem Cues { get; }

		public void Dispose()
		{
			Cues.Dispose();
			World.Dispose();
		}
	}

	private sealed class EffectRuntime : IDisposable
	{
		public EffectRuntime()
		{
			World = new World();
			Attributes = new GameplayAttributeStore(World);
			Tags = new GameplayTagStore(World);
			Effects = new GameplayEffectSystem(World, Attributes, Tags);
			Cues = new GameplayCueSystem(World);
			Cues.BindEffects(Effects);
			Target = World.CreateEntity();
		}

		private World World { get; }
		private GameplayAttributeStore Attributes { get; }
		private GameplayTagStore Tags { get; }
		public GameplayEffectSystem Effects { get; }
		public GameplayCueSystem Cues { get; }
		public Entity Target { get; }

		public void Dispose()
		{
			Cues.Dispose();
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			World.Dispose();
		}
	}

	private sealed class AbilityRuntime : IDisposable
	{
		public AbilityRuntime(GameplayAbilityDefinition definition)
		{
			World = new World();
			Attributes = new GameplayAttributeStore(World);
			Tags = new GameplayTagStore(World);
			Effects = new GameplayEffectSystem(World, Attributes, Tags);
			Abilities = new GameplayAbilitySystem(World, Tags, Effects, Attributes);
			Owner = World.CreateEntity();
			Grant = Abilities.GiveAbility(Owner, definition);
		}

		private World World { get; }
		private GameplayAttributeStore Attributes { get; }
		private GameplayTagStore Tags { get; }
		private GameplayEffectSystem Effects { get; }
		public GameplayAbilitySystem Abilities { get; }
		public Entity Owner { get; }
		public GameplayAbilityHandle Grant { get; }

		public void Dispose()
		{
			Abilities.Dispose();
			Effects.Dispose();
			Tags.Dispose();
			Attributes.Dispose();
			World.Dispose();
		}
	}
}

/// <summary>Measures initial persistent cue population growth from a fresh empty system.</summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayCueColdPopulationBenchmarks
{
	private GameplayTag _tag;
	private World? _world;
	private GameplayCueSystem? _cues;

	[Params(1_000, 10_000, 100_000)]
	public int Population { get; set; }

	[GlobalSetup]
	public void RegisterTag() => _tag = GameplayTagRegistry.Shared.Register("VoltBenchmarks.Cues.ColdPopulation");

	[IterationSetup]
	public void SetupFreshPopulation()
	{
		_world = new World();
		_cues = new GameplayCueSystem(_world);
	}

	/// <summary>Includes all Add calls and backing-array growth; tag registration and runtime construction are setup.</summary>
	[Benchmark]
	public int BuildPersistentCuePopulation()
	{
		for (int index = 0; index < Population; index++)
		{
			_cues!.Add(_tag);
		}

		return _cues!.ActiveCueCount;
	}

	[IterationCleanup]
	public void CleanupFreshPopulation()
	{
		_cues?.Dispose();
		_world?.Dispose();
		_cues = null;
		_world = null;
	}
}
