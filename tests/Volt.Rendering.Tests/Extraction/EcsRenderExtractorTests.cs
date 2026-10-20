using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Extraction;

public sealed class EcsRenderExtractorTests
{
	private static readonly MeshHandle DefaultMesh = new(1, 1);
	private static readonly MaterialHandle DefaultMaterial = new(2, 1);

	private struct TagComponent
	{
		public int Value;
	}

	[Fact]
	public void Extract_EmptyWorldProducesNoInstances()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		RenderInstance[] destination = new RenderInstance[4];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(0, result.WrittenCount);
		Assert.Equal(0, result.RequiredCount);
		Assert.Equal(0, extractor.CountRequiredInstances());
	}

	[Fact]
	public void Extract_WritesOneMatchingEntity()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity entity = CreateRenderable(
			world,
			new Vector3(1.0f, 2.0f, 3.0f),
			Quaternion.Identity,
			new Vector3(2.0f),
			DefaultMesh,
			DefaultMaterial);

		RenderInstance[] destination = new RenderInstance[1];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(1, result.WrittenCount);
		Assert.Equal(1, result.RequiredCount);

		RenderInstance instance = destination[0];

		Assert.Equal(entity, instance.Entity);
		Assert.Equal(DefaultMesh, instance.Mesh);
		Assert.Equal(DefaultMaterial, instance.Material);

		// Uniform scale 2 with no rotation, translated by (1, 2, 3).
		Assert.Equal(2.0f, instance.Model.M11);
		Assert.Equal(2.0f, instance.Model.M22);
		Assert.Equal(2.0f, instance.Model.M33);
		Assert.Equal(1.0f, instance.Model.M41);
		Assert.Equal(2.0f, instance.Model.M42);
		Assert.Equal(3.0f, instance.Model.M43);
		Assert.Equal(1.0f, instance.Model.M44);

		AssertVectorNear(
			new Vector3(3.0f, 4.0f, 5.0f),
			Vector3.Transform(Vector3.One, instance.Model));
	}

	[Fact]
	public void Extract_WritesEveryMatchingEntity()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity first = CreateRenderable(
			world,
			new Vector3(-1.0f, 0.0f, 0.0f));

		Entity second = CreateRenderable(
			world,
			new Vector3(0.0f, 1.0f, 0.0f));

		Entity third = CreateRenderable(
			world,
			new Vector3(1.0f, 0.0f, 0.0f));

		RenderInstance[] destination = new RenderInstance[4];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(3, result.WrittenCount);

		Assert.Equal(3, extractor.CountRequiredInstances());

		RenderInstance[] matched =
			destination.AsSpan(0, result.WrittenCount).ToArray();

		Assert.Contains(matched, instance => instance.Entity == first);
		Assert.Contains(matched, instance => instance.Entity == second);
		Assert.Contains(matched, instance => instance.Entity == third);
	}

	[Fact]
	public void Extract_ExcludesEntitiesMissingARequiredComponent()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity complete = CreateRenderable(
			world,
			new Vector3(0.0f, 0.0f, 0.0f));

		Entity missingPosition = world.CreateEntity();
		world.AddComponent(missingPosition, new RotationComponent(
			Quaternion.Identity));
		world.AddComponent(missingPosition, new ScaleComponent(
			Vector3.One));
		world.AddComponent(missingPosition, new MeshRendererComponent(
			DefaultMesh,
			DefaultMaterial));

		Entity missingRotation = world.CreateEntity();
		world.AddComponent(missingRotation, new PositionComponent(
			Vector3.Zero));
		world.AddComponent(missingRotation, new ScaleComponent(
			Vector3.One));
		world.AddComponent(missingRotation, new MeshRendererComponent(
			DefaultMesh,
			DefaultMaterial));

		Entity missingScale = world.CreateEntity();
		world.AddComponent(missingScale, new PositionComponent(
			Vector3.Zero));
		world.AddComponent(missingScale, new RotationComponent(
			Quaternion.Identity));
		world.AddComponent(missingScale, new MeshRendererComponent(
			DefaultMesh,
			DefaultMaterial));

		Entity missingRenderer = world.CreateEntity();
		world.AddComponent(missingRenderer, new PositionComponent(
			Vector3.Zero));
		world.AddComponent(missingRenderer, new RotationComponent(
			Quaternion.Identity));
		world.AddComponent(missingRenderer, new ScaleComponent(
			Vector3.One));

		RenderInstance[] destination = new RenderInstance[4];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(1, result.WrittenCount);

		// The world still holds every entity; only extraction filters.
		Assert.Equal(5, world.Count);
		Assert.True(world.IsAlive(missingPosition));
		Assert.True(world.IsAlive(missingRotation));
		Assert.True(world.IsAlive(missingScale));
		Assert.True(world.IsAlive(missingRenderer));

		Assert.Equal(complete, destination[0].Entity);
	}

	[Fact]
	public void Extract_ExcludesInvalidResourceHandles()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity invalidMesh = CreateRenderable(
			world,
			Vector3.Zero,
			rotation: Quaternion.Identity,
			scale: Vector3.One,
			mesh: new MeshHandle(3, 0),
			material: DefaultMaterial);

		Entity invalidMaterial = CreateRenderable(
			world,
			Vector3.Zero,
			rotation: Quaternion.Identity,
			scale: Vector3.One,
			mesh: DefaultMesh,
			material: new MaterialHandle(4, 0));

		Entity valid = CreateRenderable(
			world,
			Vector3.Zero,
			rotation: Quaternion.Identity,
			scale: Vector3.One,
			mesh: DefaultMesh,
			material: DefaultMaterial);

		RenderInstance[] destination = new RenderInstance[4];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(1, result.WrittenCount);
		Assert.Equal(1, extractor.CountRequiredInstances());

		Assert.Equal(valid, destination[0].Entity);

		// The policy only affects extraction, never entity lifetime.
		Assert.True(world.IsAlive(invalidMesh));
		Assert.True(world.IsAlive(invalidMaterial));
		Assert.Equal(3, world.Count);
	}

	[Fact]
	public void Extract_PreservesCompleteGenerationalHandles()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		MeshHandle mesh = new(7, 3);
		MaterialHandle material = new(9, 2);

		CreateRenderable(
			world,
			Vector3.Zero,
			Quaternion.Identity,
			Vector3.One,
			mesh,
			material);

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination);

		Assert.Equal(7u, destination[0].Mesh.Index);
		Assert.Equal(3u, destination[0].Mesh.Generation);
		Assert.Equal(9u, destination[0].Material.Index);
		Assert.Equal(2u, destination[0].Material.Generation);

		// Same index, different generation is a different resource.
		Assert.NotEqual(new MeshHandle(7, 4), destination[0].Mesh);
	}

	[Fact]
	public void Extract_SharedMeshHandleProducesIndependentRecords()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		MeshHandle shared = new(0, 1);

		Entity first = CreateRenderable(
			world,
			new Vector3(-1.0f, 0.0f, 0.0f),
			Quaternion.Identity,
			Vector3.One,
			shared,
			DefaultMaterial);

		Entity second = CreateRenderable(
			world,
			new Vector3(1.0f, 0.0f, 0.0f),
			Quaternion.Identity,
			new Vector3(0.5f),
			shared,
			DefaultMaterial);

		RenderInstance[] destination = new RenderInstance[2];

		extractor.Extract(destination);

		RenderInstance firstRecord = Find(destination, first);
		RenderInstance secondRecord = Find(destination, second);

		Assert.Equal(firstRecord.Mesh, secondRecord.Mesh);
		Assert.Equal(firstRecord.Material, secondRecord.Material);

		Assert.NotEqual(firstRecord.Entity, secondRecord.Entity);
		Assert.NotEqual(firstRecord.Model, secondRecord.Model);

		AssertVectorNear(
			new Vector3(-1.0f, 0.0f, 0.0f),
			Vector3.Transform(Vector3.Zero, firstRecord.Model));

		AssertVectorNear(
			new Vector3(1.0f, 0.0f, 0.0f),
			Vector3.Transform(Vector3.Zero, secondRecord.Model));
	}

	[Fact]
	public void Extract_AppliesScaleRotationTranslationInOrder()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		CreateRenderable(
			world,
			new Vector3(10.0f, 20.0f, 30.0f),
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				MathF.PI / 2.0f),
			new Vector3(2.0f),
			DefaultMesh,
			DefaultMaterial);

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination);

		// Scale (1, 1, 1) by 2, rotate +90 degrees about Y onto
		// (2, 2, -2), then translate.
		AssertVectorNear(
			new Vector3(12.0f, 22.0f, 28.0f),
			Vector3.Transform(Vector3.One, destination[0].Model));
	}

	[Fact]
	public void Extract_SurvivesArchetypeMigration()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity entity = CreateRenderable(
			world,
			Vector3.One);

		RenderInstance[] destination = new RenderInstance[2];

		Assert.Equal(1, extractor.Extract(destination).WrittenCount);

		// Adding an unrelated component migrates the entity to a new
		// archetype that still matches the query.
		world.AddComponent(entity, new TagComponent { Value = 42 });

		Assert.Equal(1, extractor.Extract(destination).WrittenCount);
		Assert.Equal(entity, destination[0].Entity);

		// Removing a required component moves it out of the query.
		Assert.True(world.RemoveComponent<ScaleComponent>(entity));

		Assert.Equal(0, extractor.Extract(destination).WrittenCount);

		// Adding it back restores the entity to the extraction.
		world.AddComponent(entity, new ScaleComponent(
			new Vector3(2.0f)));

		RenderExtractionResult restored =
			extractor.Extract(destination);

		Assert.Equal(1, restored.WrittenCount);
		Assert.Equal(2.0f, destination[0].Model.M11);
	}

	[Fact]
	public void Extract_DropsDestroyedEntities()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity first = CreateRenderable(world, Vector3.Zero);
		Entity second = CreateRenderable(world, Vector3.UnitX);

		RenderInstance[] destination = new RenderInstance[2];

		Assert.Equal(2, extractor.Extract(destination).WrittenCount);

		Assert.True(world.DestroyEntity(first));

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.Equal(1, result.WrittenCount);
		Assert.Equal(second, destination[0].Entity);
	}

	[Fact]
	public void Extract_ReflectsComponentMutation()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity moved = CreateRenderable(
			world,
			new Vector3(-1.0f, 0.0f, 0.0f));

		Entity unchanged = CreateRenderable(
			world,
			new Vector3(4.0f, 0.0f, 0.0f));

		RenderInstance[] destination = new RenderInstance[2];

		extractor.Extract(destination);

		RenderInstance before = Find(destination, moved);

		ref PositionComponent position =
			ref world.GetComponent<PositionComponent>(moved);

		position.Value = new Vector3(1.5f, -2.0f, 0.5f);

		extractor.Extract(destination);

		RenderInstance after = Find(destination, moved);

		Assert.Equal(before.Entity, after.Entity);
		Assert.Equal(before.Mesh, after.Mesh);
		Assert.Equal(before.Material, after.Material);
		Assert.NotEqual(before.Model, after.Model);

		AssertVectorNear(
			position.Value,
			Vector3.Transform(Vector3.Zero, after.Model));

		AssertVectorNear(
			new Vector3(4.0f, 0.0f, 0.0f),
			Vector3.Transform(
				Vector3.Zero,
				Find(destination, unchanged).Model));
	}

	[Fact]
	public void Extract_ExactCapacitySucceeds()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		CreateRenderable(world, Vector3.Zero);
		CreateRenderable(world, Vector3.UnitX);
		CreateRenderable(world, Vector3.UnitY);

		RenderInstance[] destination =
			new RenderInstance[extractor.CountRequiredInstances()];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.False(result.CapacityInsufficient);
		Assert.Equal(3, result.WrittenCount);
		Assert.Equal(3, result.RequiredCount);
	}

	[Fact]
	public void Extract_InsufficientCapacityWritesNothing()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		CreateRenderable(world, Vector3.Zero);
		CreateRenderable(world, Vector3.UnitX);

		RenderInstance sentinel = new(
			default,
			new MeshHandle(90, 9),
			new MaterialHandle(91, 9),
			Matrix4x4.CreateTranslation(7.0f, 8.0f, 9.0f));

		RenderInstance[] destination = [sentinel];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.False(result.Succeeded);
		Assert.True(result.CapacityInsufficient);
		Assert.Equal(0, result.WrittenCount);
		Assert.Equal(2, result.RequiredCount);

		// No partial output: the caller cannot mistake the buffer for
		// a complete extraction.
		Assert.Equal(sentinel, destination[0]);
	}

	[Fact]
	public void Extract_ProducesSnapshots()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity entity = CreateRenderable(world, Vector3.Zero);

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination);

		RenderInstance snapshot = destination[0];

		ref PositionComponent position =
			ref world.GetComponent<PositionComponent>(entity);

		position.Value = new Vector3(5.0f, 6.0f, 7.0f);

		Assert.Equal(snapshot, destination[0]);
		Assert.Equal(
			Vector3.Zero,
			Vector3.Transform(Vector3.Zero, destination[0].Model));

		extractor.Extract(destination);

		AssertVectorNear(
			position.Value,
			Vector3.Transform(Vector3.Zero, destination[0].Model));
	}

	[Fact]
	public void Extract_DoesNotModifyTheWorld()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		Entity entity = CreateRenderable(
			world,
			new Vector3(1.0f, 2.0f, 3.0f),
			Quaternion.CreateFromYawPitchRoll(0.3f, -0.2f, 0.1f),
			new Vector3(2.0f, 3.0f, 4.0f));

		world.AddComponent(entity, new TagComponent { Value = 7 });

		RenderInstance[] destination = new RenderInstance[1];

		extractor.Extract(destination);

		Assert.Equal(1, world.Count);
		Assert.True(world.IsAlive(entity));

		Assert.Equal(
			new Vector3(1.0f, 2.0f, 3.0f),
			world.GetComponent<PositionComponent>(entity).Value);

		Assert.Equal(
			new Vector3(2.0f, 3.0f, 4.0f),
			world.GetComponent<ScaleComponent>(entity).Value);

		Assert.Equal(
			7,
			world.GetComponent<TagComponent>(entity).Value);

		Assert.True(world.HasComponent<MeshRendererComponent>(entity));
	}

	[Fact]
	public void Extract_SeesEntitiesCreatedAfterConstruction()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		RenderInstance[] destination = new RenderInstance[2];

		Assert.Equal(0, extractor.Extract(destination).WrittenCount);

		Entity entity = CreateRenderable(world, Vector3.One);

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.Equal(1, result.WrittenCount);
		Assert.Equal(entity, destination[0].Entity);
	}

	[Fact]
	public void Extract_ReadsEveryChunkOfAnArchetype()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		// A chunk stores roughly 240 entities with this component
		// layout, so 400 entities necessarily span several chunks.
		const int entityCount = 400;

		Entity[] entities = new Entity[entityCount];

		for (int index = 0; index < entityCount; index++)
		{
			entities[index] = CreateRenderable(
				world,
				new Vector3(index, 0.0f, 0.0f),
				Quaternion.Identity,
				Vector3.One,
				DefaultMesh,
				DefaultMaterial);
		}

		RenderInstance[] destination =
			new RenderInstance[entityCount + 1];

		RenderExtractionResult result =
			extractor.Extract(destination);

		Assert.True(result.Succeeded);
		Assert.Equal(entityCount, result.WrittenCount);
		Assert.Equal(entityCount, extractor.CountRequiredInstances());

		// Every entity appears exactly once, whatever the chunk
		// traversal order happens to be.
		for (int index = 0; index < entityCount; index++)
		{
			Entity entity = entities[index];

			Assert.Equal(
				1,
				Count(destination, result.WrittenCount, entity));

			RenderInstance record = Find(destination, entity);

			AssertVectorNear(
				new Vector3(index, 0.0f, 0.0f),
				Vector3.Transform(Vector3.Zero, record.Model));
		}
	}

	[Fact]
	public void RenderInstance_HasValueLayoutWithoutManagedReferences()
	{
		Assert.False(
			RuntimeHelpers.IsReferenceOrContainsReferences<
				RenderInstance>());

		// 12 bytes of entity, two 8-byte handles, and a 64-byte
		// matrix.
		Assert.Equal(92, Unsafe.SizeOf<RenderInstance>());
		Assert.Equal(92, Marshal.SizeOf<RenderInstance>());
	}

	[Fact]
	public void Extract_DoesNotAllocateInSteadyState()
	{
		using World world = new();
		EcsRenderExtractor extractor = new(world);

		for (int index = 0; index < 8; index++)
		{
			CreateRenderable(
				world,
				new Vector3(index, index * 0.5f, -index),
				Quaternion.CreateFromYawPitchRoll(0.1f, 0.2f, 0.3f),
				new Vector3(1.0f + (index * 0.1f)),
				DefaultMesh,
				DefaultMaterial);
		}

		RenderInstance[] destination = new RenderInstance[8];

		for (int iteration = 0; iteration < 64; iteration++)
		{
			extractor.Extract(destination);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < 1024; iteration++)
		{
			extractor.Extract(destination);
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	private static Entity CreateRenderable(
		World world,
		Vector3 position,
		Quaternion? rotation = null,
		Vector3? scale = null,
		MeshHandle? mesh = null,
		MaterialHandle? material = null)
	{
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(position));

		world.AddComponent(entity, new RotationComponent(
			rotation ?? Quaternion.Identity));

		world.AddComponent(entity, new ScaleComponent(
			scale ?? Vector3.One));

		world.AddComponent(entity, new MeshRendererComponent(
			mesh ?? DefaultMesh,
			material ?? DefaultMaterial));

		return entity;
	}

	private static int Count(
		ReadOnlySpan<RenderInstance> instances,
		int count,
		Entity entity)
	{
		int matches = 0;

		for (int index = 0; index < count; index++)
		{
			if (instances[index].Entity == entity)
			{
				matches++;
			}
		}

		return matches;
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

	private static void AssertVectorNear(
		Vector3 expected,
		Vector3 actual,
		float tolerance = 0.00001f)
	{
		Assert.True(
			Vector3.Distance(expected, actual) <= tolerance,
			$"Expected {expected}, actual {actual}.");
	}
}
