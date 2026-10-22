using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Component;

namespace Volt.Benchmarks;

[StructLayout(LayoutKind.Sequential, Size = 8)]
internal struct MigrationPayload8
{
	public long Value;
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct MigrationPayload64
{
	public long Value;
}

[StructLayout(LayoutKind.Sequential, Size = 256)]
internal struct MigrationPayload256
{
	public long Value;
}

[StructLayout(LayoutKind.Sequential, Size = 1024)]
internal struct MigrationPayload1024
{
	public long Value;
}

[MemoryDiagnoser]
public class EcsMigrationWidthBenchmarks
{
	private const int BatchSize = 1_000;

	private World _world = null!;
	private Entity[] _entities = null!;

	[Params(8, 64, 256, 1_024)]
	public int PayloadBytes { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		ComponentRegistry.Register<BenchmarkVelocity>();

		switch (PayloadBytes)
		{
			case 8:
				ComponentRegistry.Register<MigrationPayload8>();
				break;
			case 64:
				ComponentRegistry.Register<MigrationPayload64>();
				break;
			case 256:
				ComponentRegistry.Register<MigrationPayload256>();
				break;
			case 1_024:
				ComponentRegistry.Register<MigrationPayload1024>();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(PayloadBytes));
		}

		_world = new World();
		_entities = new Entity[BatchSize];

		for (int i = 0; i < BatchSize; i++)
		{
			Entity entity = _world.CreateEntity();
			AddPayload(entity, i);
			_world.AddComponent(entity, new BenchmarkVelocity { X = 1 });
			_entities[i] = entity;
		}

		Entity warmupEntity = _entities[0];
		_world.RemoveComponent<BenchmarkVelocity>(warmupEntity);
		_world.AddComponent(warmupEntity, new BenchmarkVelocity { X = 1 });
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
			_world.AddComponent(entity, new BenchmarkVelocity { X = 1 });
		}
	}

	private void AddPayload(Entity entity, long value)
	{
		switch (PayloadBytes)
		{
			case 8:
				_world.AddComponent(entity, new MigrationPayload8 { Value = value });
				break;
			case 64:
				_world.AddComponent(entity, new MigrationPayload64 { Value = value });
				break;
			case 256:
				_world.AddComponent(entity, new MigrationPayload256 { Value = value });
				break;
			case 1_024:
				_world.AddComponent(entity, new MigrationPayload1024 { Value = value });
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(PayloadBytes));
		}
	}
}
