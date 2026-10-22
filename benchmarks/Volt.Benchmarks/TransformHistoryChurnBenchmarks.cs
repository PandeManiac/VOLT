using System.Numerics;
using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;

namespace Volt.Benchmarks;

/// <summary>
/// Measures capture cost when renderable entities are churning.
/// </summary>
/// <remarks>
/// <para>
/// A capture scans the renderable chunks and then prunes history
/// entries that the scan did not stamp, so the steady-state and churn
/// cases differ only by how much work the prune has to do. Every case
/// uses a real <see cref="World"/> and a preallocated entity array, so
/// the reported allocation is the production cost rather than
/// benchmark scaffolding.
/// </para>
/// <para>
/// The destroy case also measures world mutation and entity creation,
/// because those are part of the churn cycle the application performs.
/// The invalidate case mutates only the renderer handle and therefore
/// isolates pruning plus re-registration more closely.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TransformHistoryChurnBenchmarks
{
	private static readonly MeshHandle BenchmarkMesh = new(0, 1);

	private static readonly MaterialHandle BenchmarkMaterial = new(0, 1);

	private World _world = null!;
	private TransformHistory _history = null!;
	private Entity[] _entities = null!;

	/// <summary>
	/// Gets or sets how many renderable entities the world holds.
	/// </summary>
	[Params(512, 4096)]
	public int EntityCount { get; set; }

	/// <summary>
	/// Gets or sets how many entities churn per capture.
	/// </summary>
	[Params(64)]
	public int ChurnCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();
		_entities = new Entity[EntityCount];

		for (int index = 0; index < EntityCount; index++)
		{
			_entities[index] = CreateEntity(index);
		}

		_history = new TransformHistory(_world, initialCapacity: EntityCount);

		MoveAll(0.5f);
		_history.Capture();
		MoveAll(1.0f);
		_history.Capture();
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_world.Dispose();
	}

	/// <summary>
	/// Captures with no entity changing renderability.
	/// </summary>
	[Benchmark(Baseline = true)]
	public int SteadyCapture()
	{
		_history.Capture();

		return _history.Count;
	}

	/// <summary>
	/// Destroys a batch, recreates it in the recycled slots, and
	/// captures.
	/// </summary>
	[Benchmark]
	public int DestroyRecreateAndCapture()
	{
		for (int index = 0; index < ChurnCount; index++)
		{
			if (!_world.DestroyEntity(_entities[index]))
			{
				throw new InvalidOperationException(
					"Churn entity was not alive.");
			}

			_entities[index] = CreateEntity(index);
		}

		_history.Capture();

		return _history.Count;
	}

	/// <summary>
	/// Invalidates a batch's mesh handle, captures the prune,
	/// revalidates the batch, and captures the fresh entries.
	/// </summary>
	[Benchmark]
	public int InvalidateRevalidateAndCapture()
	{
		SetMeshes(0, ChurnCount, default);
		_history.Capture();

		SetMeshes(0, ChurnCount, BenchmarkMesh);
		_history.Capture();

		return _history.Count;
	}

	private Entity CreateEntity(int index)
	{
		Entity entity = _world.CreateEntity();

		_world.AddComponent(entity, new PositionComponent(
			new Vector3(index * 0.01f, 0.0f, -1.0f)));

		_world.AddComponent(entity, new RotationComponent(
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				index * 0.001f)));

		_world.AddComponent(entity, new ScaleComponent(Vector3.One));

		_world.AddComponent(entity, new MeshRendererComponent(
			BenchmarkMesh,
			BenchmarkMaterial));

		return entity;
	}

	private void SetMeshes(int start, int count, MeshHandle mesh)
	{
		for (int index = start; index < start + count; index++)
		{
			ref MeshRendererComponent renderer =
				ref _world.GetComponent<MeshRendererComponent>(
					_entities[index]);

			renderer.Mesh = mesh;
		}
	}

	private void MoveAll(float offset)
	{
		for (int index = 0; index < _entities.Length; index++)
		{
			ref PositionComponent position =
				ref _world.GetComponent<PositionComponent>(_entities[index]);

			position.Value = new Vector3(
				(index * 0.01f) + offset,
				0.0f,
				-1.0f);
		}
	}
}
