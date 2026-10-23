using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Benchmarks;

/// <summary>
/// Measures entity destruction and index reuse with and without
/// gameplay state owned by the recycled generation.
/// </summary>
/// <remarks>
/// The affected workload subscribes before the stores, so every
/// destruction recycles the index and repopulates the replacement
/// before the old generation's store callbacks run. That is the
/// adversarial order the lifecycle audit targets.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayLifecycleBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _attributes = null!;
	private GameplayTagStore _tags = null!;
	private GameplayEffectSystem _effects = null!;

	private GameplayAttribute _attribute;
	private GameplayTag _tag;
	private GameplayEffectDefinition _effect = null!;

	private Entity _affected;
	private Entity _plain;
	private Entity? _replacement;

	[GlobalSetup]
	public void Setup()
	{
		_attribute = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Lifecycle.Value");

		_tag = GameplayTagRegistry.Shared.Register(
			"VoltBenchmarks.Lifecycle.Tag");

		_effect = GameplayEffectDefinition
			.Create("Bench.Lifecycle.Effect")
			.Infinite()
			.Modifier(_attribute, GameplayModifierOperation.AddBase, 5f)
			.GrantTags(_tag)
			.Build();

		_world = new World();

		_world.EntityDestroyed += destroyed =>
		{
			if (destroyed != _affected)
			{
				return;
			}

			Entity replacement = _world.CreateEntity();

			_tags.AddTag(replacement, _tag);
			_attributes.SetBaseValue(replacement, _attribute, 42f);

			for (int index = 0; index < 4; index++)
			{
				_effects.Apply(replacement, _effect);
			}

			_replacement = replacement;
		};

		// The stores subscribe after the recycling handler.
		_attributes = new GameplayAttributeStore(_world);
		_tags = new GameplayTagStore(_world);
		_effects = new GameplayEffectSystem(_world, _attributes, _tags);

		_affected = _world.CreateEntity();
		_plain = _world.CreateEntity();

		_tags.AddTag(_affected, _tag);
		_attributes.SetBaseValue(_affected, _attribute, 42f);

		for (int index = 0; index < 4; index++)
		{
			_effects.Apply(_affected, _effect);
		}

		RecycleAffected();
		RecycleUntracked();
	}

	[Benchmark]
	public void RecycleAffected()
	{
		Entity destroyed = _affected;

		_world.DestroyEntity(destroyed);

		_affected = _replacement!.Value;
		_replacement = null;
	}

	[Benchmark]
	public void RecycleUntracked()
	{
		Entity destroyed = _plain;

		_world.DestroyEntity(destroyed);

		_plain = _world.CreateEntity();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_effects.Dispose();
		_tags.Dispose();
		_attributes.Dispose();
		_world.Dispose();
	}
}
