using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Extraction;

public sealed class InterpolatedRenderExtractorTests
{
	private static readonly MeshHandle DefaultMesh = new(1, 1);
	private static readonly MaterialHandle DefaultMaterial = new(2, 1);

	[Fact]
	public void Extract_InterpolatesBetweenTheStoredPoses()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Move(world, entity, new Vector3(4.0f));
		history.Capture();

		RenderInstance[] destination = new RenderInstance[1];

		RenderExtractionResult result = extractor.Extract(
			destination,
			history,
			0.5f);

		Assert.True(result.Succeeded);
		Assert.Equal(1, result.WrittenCount);

		// Position 0 -> 4 at alpha 0.5 is 2; the translation lives
		// in the fourth row of the row-vector model matrix.
		Assert.Equal(2.0f, destination[0].Model.M41);
	}

	[Fact]
	public void Extract_AtZero_UsesThePreviousPose()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Move(world, entity, new Vector3(8.0f));
		history.Capture();

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination, history, 0.0f);

		Assert.Equal(0.0f, destination[0].Model.M41);
	}

	[Fact]
	public void Extract_AtOne_UsesTheCurrentPose()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Move(world, entity, new Vector3(8.0f));
		history.Capture();

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination, history, 1.0f);

		Assert.Equal(8.0f, destination[0].Model.M41);
	}

	[Fact]
	public void Extract_DoesNotModifyTheEcs()
	{
		using World world = new();
		Entity entity = CreateRenderable(
			world,
			new Vector3(1.0f, 2.0f, 3.0f),
			Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f),
			new Vector3(2.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);
		EcsRenderExtractor direct = new(world);

		Move(world, entity, new Vector3(9.0f, 8.0f, 7.0f));
		SetRotation(world, entity, Quaternion.Identity);
		SetScale(world, entity, Vector3.One);
		history.Capture();

		RenderInstance[] before = new RenderInstance[1];
		RenderInstance[] interpolated = new RenderInstance[1];
		RenderInstance[] after = new RenderInstance[1];

		direct.Extract(before);
		extractor.Extract(interpolated, history, 0.37f);

		// The interpolated matrix differs from the authoritative
		// one, yet every component read back is unchanged.
		Assert.NotEqual(before[0].Model, interpolated[0].Model);

		direct.Extract(after);

		Assert.Equal(before[0].Model, after[0].Model);
		Assert.Equal(new Vector3(9.0f, 8.0f, 7.0f), world.GetComponent<PositionComponent>(entity).Value);
		Assert.Equal(Quaternion.Identity, world.GetComponent<RotationComponent>(entity).Value);
		Assert.Equal(Vector3.One, world.GetComponent<ScaleComponent>(entity).Value);
		Assert.Equal(entity, after[0].Entity);
		Assert.Equal(DefaultMesh, after[0].Mesh);
		Assert.Equal(DefaultMaterial, after[0].Material);
	}

	[Fact]
	public void Extract_EntityWithoutHistory_UsesTheCurrentPose()
	{
		using World world = new();
		Entity tracked = CreateRenderable(world, new Vector3(0.0f));
		Entity untracked = CreateRenderable(world, new Vector3(5.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		// Drop history for one entity, simulating a scene that
		// interpolates only part of its content.
		Assert.True(history.Remove(untracked));

		Move(world, tracked, new Vector3(4.0f));
		Move(world, untracked, new Vector3(6.0f));
		history.Capture();

		RenderInstance[] destination = new RenderInstance[2];

		extractor.Extract(destination, history, 0.5f);

		RenderInstance trackedInstance = Find(destination, tracked);
		RenderInstance untrackedInstance = Find(destination, untracked);

		Assert.Equal(2.0f, trackedInstance.Model.M41);
		Assert.Equal(6.0f, untrackedInstance.Model.M41);
	}

	[Fact]
	public void Extract_DoesNotEmitADestroyedEntity()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Assert.True(world.DestroyEntity(entity));

		RenderInstance[] destination = new RenderInstance[1];

		RenderExtractionResult result = extractor.Extract(
			destination,
			history,
			0.5f);

		Assert.True(result.Succeeded);
		Assert.Equal(0, result.WrittenCount);

		// History may still hold the old identity; it is simply not
		// rendered.
		Assert.True(history.TryGetPose(entity, out _, out _));
	}

	[Fact]
	public void Extract_WithInsufficientCapacity_WritesNothing()
	{
		using World world = new();

		CreateRenderable(world, Vector3.Zero);
		CreateRenderable(world, Vector3.One);

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		RenderInstance[] destination = new RenderInstance[1];

		RenderExtractionResult result = extractor.Extract(
			destination,
			history,
			0.5f);

		Assert.False(result.Succeeded);
		Assert.True(result.CapacityInsufficient);
		Assert.Equal(2, result.RequiredCount);
		Assert.Equal(0, result.WrittenCount);
		Assert.Equal(2, extractor.CountRequiredInstances());
	}

	[Fact]
	public void Extract_IsAllocationFreeAfterWarmup()
	{
		using World world = new();

		for (int index = 0; index < 16; index++)
		{
			CreateRenderable(world, new Vector3(index));
		}

		TransformHistory history = new(world, initialCapacity: 16);
		InterpolatedRenderExtractor extractor = new(world);

		RenderInstance[] destination = new RenderInstance[16];

		for (int index = 0; index < 100; index++)
		{
			history.Capture();
			extractor.Extract(destination, history, 0.5f);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1000; index++)
		{
			history.Capture();
			extractor.Extract(destination, history, 0.5f);
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	[Fact]
	public void Extract_NullHistory_IsRejected()
	{
		using World world = new();
		InterpolatedRenderExtractor extractor = new(world);

		Assert.Throws<ArgumentNullException>(
			() => extractor.Extract(
				new RenderInstance[1],
				null!,
				0.5f));
	}

	[Fact]
	public void Extract_DoesNotRenderAnEntityThatBecameInvalid()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Move(world, entity, new Vector3(4.0f));
		history.Capture();

		RenderInstance[] destination = new RenderInstance[1];

		Assert.Equal(
			1,
			extractor.Extract(destination, history, 0.5f).WrittenCount);

		world.GetComponent<MeshRendererComponent>(entity).Mesh = default;

		// Extraction follows the renderable filter, not history.
		Assert.Equal(
			0,
			extractor.Extract(destination, history, 0.5f).WrittenCount);

		Assert.True(history.TryGetPose(entity, out _, out _));

		history.Capture();

		Assert.False(history.TryGetPose(entity, out _, out _));
	}

	[Fact]
	public void Extract_AfterReactivation_DoesNotSweepFromStaleHistory()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);
		InterpolatedRenderExtractor extractor = new(world);

		Move(world, entity, new Vector3(100.0f));
		history.Capture();

		Move(world, entity, new Vector3(200.0f));
		history.Capture();

		world.GetComponent<MeshRendererComponent>(entity).Mesh = default;
		history.Capture();

		Move(world, entity, new Vector3(900.0f));
		world.GetComponent<MeshRendererComponent>(entity).Mesh = DefaultMesh;
		history.Capture();

		RenderInstance[] destination = new RenderInstance[1];

		RenderExtractionResult result = extractor.Extract(
			destination,
			history,
			0.5f);

		Assert.Equal(1, result.WrittenCount);

		// A fresh pair holds the reactivated pose; the stale 100 -> 200
		// pair from before the disappearance is not used.
		Assert.Equal(900.0f, destination[0].Model.M41);
	}

	private static Entity CreateRenderable(
		World world,
		Vector3 position,
		Quaternion? rotation = null,
		Vector3? scale = null)
	{
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(position));
		world.AddComponent(entity, new RotationComponent(
			rotation ?? Quaternion.Identity));
		world.AddComponent(entity, new ScaleComponent(
			scale ?? Vector3.One));
		world.AddComponent(entity, new MeshRendererComponent(
			DefaultMesh,
			DefaultMaterial));

		return entity;
	}

	private static void Move(World world, Entity entity, Vector3 position)
	{
		world.GetComponent<PositionComponent>(entity).Value = position;
	}

	private static void SetRotation(
		World world,
		Entity entity,
		Quaternion rotation)
	{
		world.GetComponent<RotationComponent>(entity).Value = rotation;
	}

	private static void SetScale(World world, Entity entity, Vector3 scale)
	{
		world.GetComponent<ScaleComponent>(entity).Value = scale;
	}

	private static RenderInstance Find(
		ReadOnlySpan<RenderInstance> instances,
		Entity entity)
	{
		foreach (RenderInstance instance in instances)
		{
			if (instance.Entity == entity)
			{
				return instance;
			}
		}

		throw new InvalidOperationException(
			$"Entity {entity} was not extracted.");
	}
}
