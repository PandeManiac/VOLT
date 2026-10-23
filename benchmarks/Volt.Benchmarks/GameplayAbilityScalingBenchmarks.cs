using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures how ability operations scale with the number of grants on
/// one owner.
/// </summary>
/// <remarks>
/// <para>
/// Every workload isolates one operation whose cost should be linear,
/// or whose scan should be visible. The cancellation workloads include
/// the activation pass that seeds the active set, so their measured
/// cost is activation plus cancellation.
/// </para>
/// <para>
/// A linear implementation keeps the per-operation time roughly
/// constant as <see cref="GrantCount"/> grows; a quadratic one grows
/// with the grant count, which shows up as a rising cost per call.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAbilityScalingBenchmarks
{
	private const int InputId = 7;

	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayAbilitySystem _abilities = null!;

	private GameplayTag _stunTag;

	private GameplayAbilityDefinition _instant = null!;
	private GameplayAbilityDefinition _persistent = null!;
	private GameplayAbilityDefinition _blocker = null!;
	private GameplayAbilityDefinition _otherBlocker = null!;
	private GameplayAbilityDefinition _candidate = null!;
	private GameplayAbilityDefinition _tagCancelled = null!;

	private Entity _inputActor;
	private Entity _blockedActor;
	private Entity _cancelActor;
	private Entity _tagActor;

	private GameplayAbilityHandle _candidateHandle;
	private GameplayAbilityHandle[] _cancelHandles = null!;
	private GameplayAbilityHandle[] _tagHandles = null!;

	[Params(1, 8, 32, 64, 256)]
	public int GrantCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		GameplayTag abilityTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.AbilityScale.Ability");

		GameplayTag targetTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.AbilityScale.Target");

		GameplayTag otherTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.AbilityScale.Other");

		_stunTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.AbilityScale.Stun");

		GameplayAttribute resource = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.AbilityScale.Resource");

		_instant = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.Instant")
			.AssetTags(abilityTag)
			.Build();

		// No asset tags: activation then skips the block scan, so the
		// cancellation workloads measure cancellation rather than the
		// growing active set used to reach it.
		_persistent = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.Persistent")
			.EndOnActivate(false)
			.Build();

		_blocker = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.Blocker")
			.AssetTags(abilityTag)
			.BlockAbilitiesWithTags(targetTag)
			.EndOnActivate(false)
			.Build();

		_otherBlocker = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.OtherBlocker")
			.AssetTags(abilityTag)
			.BlockAbilitiesWithTags(otherTag)
			.EndOnActivate(false)
			.Build();

		_candidate = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.Candidate")
			.AssetTags(targetTag)
			.Build();

		_tagCancelled = GameplayAbilityDefinition
			.Create("Bench.AbilityScale.TagCancelled")
			.CancelOnTags(_stunTag)
			.EndOnActivate(false)
			.Build();

		_world = new World();
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);
		_abilities = new GameplayAbilitySystem(
			_world,
			_tags,
			_effects,
			_attributes);

		_inputActor = _world.CreateEntity();
		_blockedActor = _world.CreateEntity();
		_cancelActor = _world.CreateEntity();
		_tagActor = _world.CreateEntity();

		for (int index = 0; index < GrantCount; index++)
		{
			_abilities.GiveAbility(_inputActor, _instant, inputId: InputId);
		}

		// The matching blocker is the oldest grant, so the scan in the
		// block check walks every non-matching blocker before it.
		GameplayAbilityHandle blockerHandle =
			_abilities.GiveAbility(_blockedActor, _blocker);

		AssertActivated(blockerHandle);

		for (int index = 1; index < GrantCount; index++)
		{
			GameplayAbilityHandle other = _abilities.GiveAbility(
				_blockedActor,
				_otherBlocker);

			AssertActivated(other);
		}

		_candidateHandle = _abilities.GiveAbility(_blockedActor, _candidate);

		_cancelHandles = new GameplayAbilityHandle[GrantCount];
		_tagHandles = new GameplayAbilityHandle[GrantCount];

		for (int index = 0; index < GrantCount; index++)
		{
			_cancelHandles[index] =
				_abilities.GiveAbility(_cancelActor, _persistent);

			_tagHandles[index] =
				_abilities.GiveAbility(_tagActor, _tagCancelled);
		}

		// The resource is kept so the stores cannot optimize the
		// attribute path away; these workloads do not spend it.
		_attributes.SetBaseValue(_cancelActor, resource, 1_000f);

		// Prime the operation buffers with the public operations they
		// serve, so a measured call never grows one.
		for (int index = 0; index < GrantCount; index++)
		{
			_abilities.TryActivate(_cancelHandles[index]);
			_abilities.TryActivate(_tagHandles[index]);
		}

		_abilities.CancelAll(_cancelActor);

		_tags.AddTag(_tagActor, _stunTag);
		_tags.RemoveTag(_tagActor, _stunTag);

		_abilities.TryActivateByInput(_inputActor, InputId);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_abilities.Dispose();
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public int ActivateByInput()
	{
		return _abilities.TryActivateByInput(_inputActor, InputId);
	}

	[Benchmark]
	public GameplayAbilityActivationStatus ActivateBlocked()
	{
		return _abilities.TryActivate(_candidateHandle).Status;
	}

	[Benchmark]
	public int ActivateAllAndCancelAll()
	{
		for (int index = 0; index < _cancelHandles.Length; index++)
		{
			_abilities.TryActivate(_cancelHandles[index]);
		}

		return _abilities.CancelAll(_cancelActor);
	}

	[Benchmark]
	public int ActivateAllAndCancelByTag()
	{
		for (int index = 0; index < _tagHandles.Length; index++)
		{
			_abilities.TryActivate(_tagHandles[index]);
		}

		_tags.AddTag(_tagActor, _stunTag);

		int active = _abilities.GetActiveAbilityCount(_tagActor);

		_tags.RemoveTag(_tagActor, _stunTag);

		return active;
	}

	private void AssertActivated(GameplayAbilityHandle handle)
	{
		if (!_abilities.TryActivate(handle).Succeeded)
		{
			throw new InvalidOperationException(
				$"Benchmark setup could not activate {handle}.");
		}
	}
}
