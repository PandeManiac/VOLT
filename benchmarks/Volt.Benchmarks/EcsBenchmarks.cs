
using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Component;

namespace Volt.Benchmarks;

internal struct BenchmarkPosition
{
	public float X;
	public float Y;
	public float Z;
}

internal struct BenchmarkVelocity
{
	public float X;
	public float Y;
	public float Z;
}

[MemoryDiagnoser]
public class EcsBenchmarks
{
	private World _world = null!;
	private Entity[] _entities = null!;
	private WorldQuery _query = null!;

	[Params(1_000, 100_000, 1_000_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<BenchmarkPosition>();
		ComponentRegistry.Register<BenchmarkVelocity>();

		_world = new World();
		_entities = new Entity[EntityCount];

		for (int i = 0; i < EntityCount; i++)
		{
			Entity entity = _world.CreateEntity();

			_world.AddComponent(entity, new BenchmarkPosition
			{
				X = i,
				Y = i * 2,
				Z = i * 3
			});

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1,
				Y = 2,
				Z = 3
			});

			_entities[i] = entity;
		}

		_query = _world.Query(new ComponentQuery(
			required: new ComponentSignature(
				ComponentRegistry.Get<BenchmarkPosition>(),
				ComponentRegistry.Get<BenchmarkVelocity>())));
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_world.Dispose();
	}

	[Benchmark]
	public float IteratePositionVelocity()
	{
		float sum = 0;

		foreach (WorldQueryChunk chunk in _query)
		{
			Span<BenchmarkPosition> positions =
				chunk.GetSpan<BenchmarkPosition>();

			Span<BenchmarkVelocity> velocities =
				chunk.GetSpan<BenchmarkVelocity>();

			for (int i = 0; i < chunk.Count; i++)
			{
				sum += positions[i].X + velocities[i].X;
			}
		}

		return sum;
	}

	[Benchmark]
	public int IterateEntities()
	{
		int count = 0;

		foreach (WorldQueryChunk chunk in _query)
		{
			count += chunk.GetEntities().Length;
		}

		return count;
	}

	[Benchmark]
	public float EntityHandleComponentAccess()
	{
		float sum = 0;

		for (int i = 0; i < _entities.Length; i++)
		{
			ref BenchmarkPosition position =
				ref _world.GetComponent<BenchmarkPosition>(_entities[i]);

			sum += position.X;
		}

		return sum;
	}
}


[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class EcsStructuralBenchmarks
{
	private const int BatchSize = 1_000;

	private World _world = null!;
	private Entity[] _entities = null!;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<BenchmarkPosition>();
		ComponentRegistry.Register<BenchmarkVelocity>();
	}

	[IterationSetup]
	public void IterationSetup()
	{
		_world = new World();
		_entities = new Entity[EntityCount];

		for (int i = 0; i < EntityCount; i++)
		{
			Entity entity = _world.CreateEntity();

			_world.AddComponent(entity, new BenchmarkPosition
			{
				X = i
			});

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1
			});

			_entities[i] = entity;
		}
	}

	[IterationCleanup]
	public void IterationCleanup()
	{
		_world.Dispose();
	}

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void CreateEntity()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			_world.CreateEntity();
		}
	}

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void AddRemoveComponent()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			Entity entity = _entities[i];

			_world.RemoveComponent<BenchmarkVelocity>(entity);

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1,
				Y = 2,
				Z = 3
			});
		}
	}
}

[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class EcsChunkAvailabilityBenchmarks
{
	private const int BatchSize = 1_000;

	private World _world = null!;
	private Entity[] _entities = null!;
	private int _nextEntity;

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<BenchmarkPosition>();
		ComponentRegistry.Register<BenchmarkVelocity>();
	}

	[IterationSetup]
	public void IterationSetup()
	{
		_world = new World();
		_entities = new Entity[EntityCount + BatchSize];
		_nextEntity = EntityCount;

		for (int i = 0; i < EntityCount; i++)
		{
			Entity entity = _world.CreateEntity();

			_world.AddComponent(entity, new BenchmarkPosition
			{
				X = i
			});

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1
			});

			_entities[i] = entity;
		}
	}

	[IterationCleanup]
	public void IterationCleanup()
	{
		_world.Dispose();
	}

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void AppendToArchetype()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			Entity entity = _world.CreateEntity();

			_world.AddComponent(entity, new BenchmarkPosition
			{
				X = i
			});

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1
			});

			_entities[_nextEntity++] = entity;
		}
	}

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void MigrateExistingEntities()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			Entity entity = _entities[i];

			_world.RemoveComponent<BenchmarkVelocity>(entity);

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1
			});
		}
	}
}


[MemoryDiagnoser]
[SimpleJob(invocationCount: 1)]
public class EcsFragmentationBenchmarks
{
	private const int BatchSize = 1_000;

	private World _world = null!;
	private Entity[] _entities = null!;

	[Params(10_000, 100_000, 1_000_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<BenchmarkPosition>();
		ComponentRegistry.Register<BenchmarkVelocity>();
	}

	[IterationSetup]
	public void IterationSetup()
	{
		_world = new World();
		_entities = new Entity[EntityCount];

		for (int i = 0; i < EntityCount; i++)
		{
			Entity entity = _world.CreateEntity();

			_world.AddComponent(entity, new BenchmarkPosition
			{
				X = i
			});

			_world.AddComponent(entity, new BenchmarkVelocity
			{
				X = 1
			});

			_entities[i] = entity;
		}

		// Remove Velocity from early entities.
		// This creates reusable capacity in the
		// Position + Velocity archetype.
		for (int i = 0; i < BatchSize; i++)
		{
			_world.RemoveComponent<BenchmarkVelocity>(
				_entities[i]);
		}
	}

	[IterationCleanup]
	public void IterationCleanup()
	{
		_world.Dispose();
	}

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void RefillFragmentedArchetype()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			_world.AddComponent(
				_entities[i],
				new BenchmarkVelocity
				{
					X = 1
				});
		}
	}
}


internal struct MigrationComponent0 { public long Value; }
internal struct MigrationComponent1 { public long Value; }
internal struct MigrationComponent2 { public long Value; }
internal struct MigrationComponent3 { public long Value; }
internal struct MigrationComponent4 { public long Value; }
internal struct MigrationComponent5 { public long Value; }
internal struct MigrationComponent6 { public long Value; }
internal struct MigrationComponent7 { public long Value; }


[MemoryDiagnoser]
public class EcsMigrationBenchmarks
{
	private const int BatchSize = 1_000;

	private World _world = null!;
	private Entity[] _entities = null!;

	[Params(0, 1, 2, 8)]
	public int ComponentCount { get; set; }

	[Params(1_000, 100_000)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<MigrationComponent0>();
		ComponentRegistry.Register<MigrationComponent1>();
		ComponentRegistry.Register<MigrationComponent2>();
		ComponentRegistry.Register<MigrationComponent3>();
		ComponentRegistry.Register<MigrationComponent4>();
		ComponentRegistry.Register<MigrationComponent5>();
		ComponentRegistry.Register<MigrationComponent6>();
		ComponentRegistry.Register<MigrationComponent7>();
		ComponentRegistry.Register<BenchmarkVelocity>();

		_world = new World();

		_entities = new Entity[BatchSize];

		for (int i = 0; i < EntityCount; i++)
		{
			Entity entity = _world.CreateEntity();

			if (ComponentCount >= 1)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent0 { Value = i });
			}

			if (ComponentCount >= 2)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent1 { Value = i });
			}

			if (ComponentCount >= 3)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent2 { Value = i });
			}

			if (ComponentCount >= 4)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent3 { Value = i });
			}

			if (ComponentCount >= 5)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent4 { Value = i });
			}

			if (ComponentCount >= 6)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent5 { Value = i });
			}

			if (ComponentCount >= 7)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent6 { Value = i });
			}

			if (ComponentCount >= 8)
			{
				_world.AddComponent(
					entity,
					new MigrationComponent7 { Value = i });
			}

			_world.AddComponent(
				entity,
				new BenchmarkVelocity { X = 1 });

			if (i < BatchSize)
			{
				_entities[i] = entity;
			}
		}

		// Prewarm both migration directions.
		// This also ensures the cached plans exist
		// before the timed benchmark begins.
		Entity warmupEntity = _entities[0];

		_world.RemoveComponent<BenchmarkVelocity>(warmupEntity);

		_world.AddComponent(
			warmupEntity,
			new BenchmarkVelocity { X = 1 });
	}

	[GlobalCleanup]
	public void Cleanup() => _world.Dispose();

	[Benchmark(OperationsPerInvoke = BatchSize)]
	public void RemoveAndReAddVelocity()
	{
		for (int i = 0; i < BatchSize; i++)
		{
			Entity entity = _entities[i];

			_world.RemoveComponent<BenchmarkVelocity>(entity);

			_world.AddComponent(
				entity,
				new BenchmarkVelocity { X = 1 });
		}
	}
}
