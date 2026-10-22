using System.Numerics;
using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;

namespace Volt.Benchmarks;

/// <summary>
/// Separates the four costs of interpolated ECS rendering: capturing
/// component transforms, updating history, computing interpolated
/// poses, and extracting interpolated render instances.
/// </summary>
/// <remarks>
/// <para>
/// Every case uses a real <see cref="World"/> whose entities carry the
/// spatial components and a valid mesh renderer, because history and
/// extraction apply the same renderable filter as the un-interpolated
/// path. Storage is caller-owned and sized in
/// <see cref="GlobalSetup"/>, so the measured methods allocate nothing
/// beyond what the production types allocate at steady state.
/// </para>
/// <para>
/// The entity count is a parameter instead of a second benchmark class
/// so small and large worlds are measured with identical code. The
/// large case is a representative world rather than a microbenchmark
/// fixture: it exercises the chunked query path for thousands of
/// entities.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TransformInterpolationBenchmarks
{
	private static readonly MeshHandle BenchmarkMesh = new(0, 1);

	private static readonly MaterialHandle BenchmarkMaterial = new(0, 1);

	private World _world = null!;
	private TransformHistory _history = null!;
	private InterpolatedRenderExtractor _extractor = null!;
	private RenderInstance[] _instances = null!;
	private TransformPose[] _poses = null!;
	private Entity[] _entities = null!;

	/// <summary>
	/// Gets or sets how many renderable entities the world holds.
	/// </summary>
	[Params(64, 4096)]
	public int EntityCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_world = new World();
		_entities = new Entity[EntityCount];

		for (int index = 0; index < EntityCount; index++)
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

			_entities[index] = entity;
		}

		_history = new TransformHistory(_world, initialCapacity: EntityCount);
		_extractor = new InterpolatedRenderExtractor(_world);
		_instances = new RenderInstance[EntityCount];
		_poses = new TransformPose[EntityCount];

		// Move every entity once and capture twice so history holds a
		// genuine previous/current pair instead of two identical
		// poses.
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
	/// Captures every renderable entity's committed pose into
	/// history.
	/// </summary>
	[Benchmark(Baseline = true)]
	public int CaptureTransforms()
	{
		_history.Capture();

		return _history.Count;
	}

	/// <summary>
	/// Computes an interpolated pose per entity into preallocated
	/// storage.
	/// </summary>
	[Benchmark]
	public int ComputeInterpolatedPoses()
	{
		int written = 0;

		foreach (Entity entity in _entities)
		{
			if (_history.TryGetPose(entity, out TransformPose previous, out TransformPose current))
			{
				_poses[written] = TransformPose.Lerp(previous, current, 0.5f);
				written++;
			}
		}

		return written;
	}

	/// <summary>
	/// Extracts interpolated render instances into a caller-owned
	/// buffer.
	/// </summary>
	[Benchmark]
	public int ExtractInterpolatedInstances()
	{
		RenderExtractionResult result =
			_extractor.Extract(_instances, _history, 0.5f);

		return result.WrittenCount;
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
