using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures ability granting, activation, and rejection at a populated
/// and at a sparse world size.
/// </summary>
/// <remarks>
/// <para>
/// The workloads mirror the specification's cost model: granting and
/// rejecting an ability must touch only the owner's grant list, not the
/// world's population, and a warmed-up activation must not allocate.
/// </para>
/// <para>
/// Runs in process for the same reason as the other gameplay
/// benchmarks: duplicate benchmark projects in untracked experiment
/// workspaces make BenchmarkDotNet's out-of-process project discovery
/// ambiguous.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAbilityBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;
	private GameplayAbilitySystem _abilities = null!;

	private GameplayAbilityDefinition _free = null!;
	private GameplayAbilityDefinition _costed = null!;
	private GameplayAbilityDefinition _cooling = null!;
	private GameplayAbilityDefinition _required = null!;
	private GameplayAbilityDefinition _blocked = null!;
	private GameplayAbilityDefinition _input = null!;

	private GameplayAbilityHandle _freeHandle;
	private GameplayAbilityHandle _costedHandle;
	private GameplayAbilityHandle _coolingHandle;
	private GameplayAbilityHandle _requiredHandle;
	private GameplayAbilityHandle _blockedHandle;

	private Entity _freeActor;
	private Entity _costedActor;
	private Entity _coolingActor;
	private Entity _requiredActor;
	private Entity _blockedActor;
	private Entity _inputActor;
	private Entity _grantActor;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		GameplayAttribute attribute = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Abilities.Resource");

		GameplayTag assetRoot =
			GameplayTagRegistry.Shared.Register("VoltBenchmarks.Abilities");

		GameplayTag coolingTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Abilities.Cooldown");

		GameplayTag requiredTag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Abilities.Required");

		GameplayEffectDefinition cost = GameplayEffectDefinition
			.Create("Bench.Ability.Cost")
			.Instant()
			.Modifier(attribute, GameplayModifierOperation.AddBase, -0.001f)
			.Build();

		GameplayEffectDefinition cooldown = GameplayEffectDefinition
			.Create("Bench.Ability.Cooldown")
			.Infinite()
			.GrantTags(coolingTag)
			.Build();

		_free = GameplayAbilityDefinition
			.Create("Bench.Ability.Free")
			.AssetTags(assetRoot)
			.EndOnActivate(false)
			.Build();

		_costed = GameplayAbilityDefinition
			.Create("Bench.Ability.Costed")
			.AssetTags(assetRoot)
			.Cost(cost)
			.Build();

		_cooling = GameplayAbilityDefinition
			.Create("Bench.Ability.Cooling")
			.AssetTags(assetRoot)
			.Cooldown(cooldown)
			.Build();

		_required = GameplayAbilityDefinition
			.Create("Bench.Ability.Required")
			.AssetTags(assetRoot)
			.RequireToActivate(
				GameplayTagRequirements.Require(
					GameplayTagSet.Create(requiredTag)))
			.Build();

		_blocked = GameplayAbilityDefinition
			.Create("Bench.Ability.Blocked")
			.AssetTags(assetRoot)
			.Build();

		_input = GameplayAbilityDefinition
			.Create("Bench.Ability.Input")
			.AssetTags(assetRoot)
			.Build();

		GameplayAbilityDefinition blocker = GameplayAbilityDefinition
			.Create("Bench.Ability.Blocker")
			.AssetTags(assetRoot)
			.BlockAbilitiesWithTags(assetRoot)
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

		// Populate the world with one grant each, so every measured
		// operation runs against a realistic owner list rather than an
		// empty system. The filler grants are never activated.
		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			_abilities.GiveAbility(entity, _input);
		}

		_freeActor = _world.CreateEntity();
		_costedActor = _world.CreateEntity();
		_coolingActor = _world.CreateEntity();
		_requiredActor = _world.CreateEntity();
		_blockedActor = _world.CreateEntity();
		_inputActor = _world.CreateEntity();
		_grantActor = _world.CreateEntity();

		// The cost must stay affordable for the whole run. The
		// attribute is unbounded and starts far above one unit per
		// million iterations.
		_attributes.SetBaseValue(_costedActor, attribute, 1_000_000_000f);

		_freeHandle = _abilities.GiveAbility(_freeActor, _free);
		_costedHandle = _abilities.GiveAbility(_costedActor, _costed);
		_coolingHandle = _abilities.GiveAbility(_coolingActor, _cooling);
		_requiredHandle = _abilities.GiveAbility(_requiredActor, _required);

		_blockedHandle = _abilities.GiveAbility(_blockedActor, _blocked);

		_abilities.GiveAbility(_inputActor, _input, inputId: 1);

		GameplayAbilityHandle activeBlocker =
			_abilities.GiveAbility(_blockedActor, blocker);

		_abilities.TryActivate(activeBlocker);

		// Warm the cooldown so every measured attempt is rejected.
		_abilities.TryActivate(_coolingHandle);

		_abilities.TryActivate(_freeHandle);
		_abilities.End(_freeHandle);
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
	public GameplayAbilityActivationStatus ActivateAndEnd_Ability()
	{
		GameplayAbilityActivationResult result =
			_abilities.TryActivate(_freeHandle);

		_abilities.End(_freeHandle);

		return result.Status;
	}

	[Benchmark]
	public GameplayAbilityActivationStatus Activate_CostedAbility()
	{
		return _abilities.TryActivate(_costedHandle).Status;
	}

	[Benchmark]
	public GameplayAbilityActivationStatus Activate_RejectedByCooldown()
	{
		return _abilities.TryActivate(_coolingHandle).Status;
	}

	[Benchmark]
	public GameplayAbilityActivationStatus Activate_RejectedByRequirements()
	{
		return _abilities.TryActivate(_requiredHandle).Status;
	}

	[Benchmark]
	public GameplayAbilityActivationStatus Activate_BlockedByActiveAbility()
	{
		return _abilities.TryActivate(_blockedHandle).Status;
	}

	[Benchmark]
	public int Activate_ByInput()
	{
		return _abilities.TryActivateByInput(_inputActor, 1);
	}

	[Benchmark]
	public int GiveAndRevoke()
	{
		GameplayAbilityHandle handle =
			_abilities.GiveAbility(_grantActor, _free);

		_abilities.RevokeAbility(handle);

		return (int)handle.Generation;
	}

	[Benchmark]
	public int GetAbilityCount()
	{
		return _abilities.GetAbilityCount(_freeActor);
	}

	[Benchmark]
	public double GetCooldownTimeRemaining()
	{
		return _abilities.GetCooldownTimeRemaining(
			_coolingActor,
			_cooling);
	}
}
