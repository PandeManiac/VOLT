using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Gameplay.Attributes;

namespace Volt.Benchmarks;

/// <summary>
/// Measures attribute reads, writes, and modifier reevaluation.
/// </summary>
/// <remarks>
/// Attribute reads return a cached current value, so they should not
/// depend on how many modifiers are active. The modifier benchmarks
/// deliberately keep eight modifiers active so the reevaluation cost
/// of adding and removing one is visible rather than hidden behind an
/// empty list.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class GameplayAttributeBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _store = null!;
	private GameplayAttribute _health;
	private Entity _tracked;
	private Entity _untouched;

	[Params(1_000, 100_000, 1_000_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_health = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Attributes.Health",
			100f,
			0f,
			100f);

		_world = new World();
		_store = new GameplayAttributeStore(_world);

		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			if (index == 0)
			{
				_tracked = entity;
			}
			else if (index == 1)
			{
				_untouched = entity;
			}

			if (index % 100 == 0)
			{
				_store.SetBaseValue(entity, _health, 75f);
			}
		}

		for (int index = 0; index < 8; index++)
		{
			_store.AddModifier(
				_tracked,
				_health,
				GameplayModifierOperation.AddFinal,
				1f);
		}
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_store.Dispose();
		_world.Dispose();
	}

	[Benchmark]
	public float GetCurrentValue_Tracked()
	{
		return _store.GetCurrentValue(_tracked, _health);
	}

	[Benchmark]
	public float GetCurrentValue_Untouched()
	{
		return _store.GetCurrentValue(_untouched, _health);
	}

	[Benchmark]
	public float GetBaseValue_Tracked()
	{
		return _store.GetBaseValue(_tracked, _health);
	}

	[Benchmark]
	public void SetBaseValue_Alternating()
	{
		_store.SetBaseValue(_tracked, _health, 70f);
		_store.SetBaseValue(_tracked, _health, 75f);
	}

	[Benchmark]
	public void AddRemoveModifier_EightActive()
	{
		GameplayModifierHandle handle = _store.AddModifier(
			_tracked,
			_health,
			GameplayModifierOperation.AddFinal,
			1f);

		_store.RemoveModifier(handle);
	}
}

/// <summary>
/// Measures attribute reads when every entity owns attribute state.
/// </summary>
[MemoryDiagnoser]
[InProcess]
public class GameplayAttributeDenseBenchmarks
{
	private World _world = null!;
	private GameplayAttributeStore _store = null!;
	private GameplayAttribute _health;
	private Entity _tracked;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_health = GameplayAttributeRegistry.Shared.Register(
			"VoltBenchmarks.Attributes.Dense.Health",
			100f,
			0f,
			100f);

		_world = new World();
		_store = new GameplayAttributeStore(_world);

		for (int index = 0; index < EntityCount; index++)
		{
			Entity entity = _world.CreateEntity();

			_store.SetBaseValue(entity, _health, 75f);

			if (index == 0)
			{
				_tracked = entity;
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
	public float GetCurrentValue_Dense()
	{
		return _store.GetCurrentValue(_tracked, _health);
	}
}
