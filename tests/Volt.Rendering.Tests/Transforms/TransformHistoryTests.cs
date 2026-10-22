using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Transforms;

public sealed class TransformHistoryTests
{
	private static readonly MeshHandle DefaultMesh = new(1, 1);
	private static readonly MaterialHandle DefaultMaterial = new(2, 1);

	private struct TagComponent
	{
		public int Value;
	}

	[Fact]
	public void Construction_StartsWithIdenticalPoses()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(1.0f));

		TransformHistory history = new(world);

		Assert.Equal(1, history.Count);
		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));
		Assert.Equal(previous.Position, current.Position);
		Assert.Equal(previous.Rotation, current.Rotation);
		Assert.Equal(previous.Scale, current.Scale);
	}

	[Fact]
	public void Capture_ShiftsTheCurrentPoseToPrevious()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(1.0f));
		history.Capture();

		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));

		Assert.Equal(Vector3.Zero, previous.Position);
		Assert.Equal(new Vector3(1.0f), current.Position);
	}

	[Fact]
	public void Capture_RetainsOnlyTheLastTwoCompletedStates()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(1.0f));
		history.Capture();

		SetPosition(world, entity, new Vector3(2.0f));
		history.Capture();

		SetPosition(world, entity, new Vector3(3.0f));
		history.Capture();

		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));

		Assert.Equal(new Vector3(2.0f), previous.Position);
		Assert.Equal(new Vector3(3.0f), current.Position);
	}

	[Fact]
	public void Capture_WithNoStep_KeepsBothPoses()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, new Vector3(0.0f));

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(5.0f));
		history.Capture();

		// A frame with zero fixed steps does not capture at all, so
		// running one only when a step completed leaves the state
		// untouched. A repeated capture of an unchanged world is
		// equally stable.
		history.Capture();

		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));

		Assert.Equal(new Vector3(5.0f), previous.Position);
		Assert.Equal(new Vector3(5.0f), current.Position);
	}

	[Fact]
	public void NewEntity_DoesNotInheritAReusedSlot()
	{
		using World world = new();
		Entity first = CreateRenderable(world, new Vector3(1.0f));

		TransformHistory history = new(world);

		SetPosition(world, first, new Vector3(9.0f));
		history.Capture();

		uint index = first.Index;
		uint generation = first.Generation;

		Assert.True(world.DestroyEntity(first));

		Entity second = CreateRenderable(world, new Vector3(-4.0f));

		Assert.Equal(index, second.Index);
		Assert.NotEqual(generation, second.Generation);

		history.Capture();

		Assert.True(history.TryGetPose(second, out TransformPose previous, out TransformPose current));

		Assert.Equal(new Vector3(-4.0f), previous.Position);
		Assert.Equal(new Vector3(-4.0f), current.Position);

		// The old identity can no longer be looked up, so a stale
		// handle cannot read the new occupant's pose.
		Assert.False(history.TryGetPose(first, out _, out _));
	}

	[Fact]
	public void Remove_ReleasesAnEntityEagerly()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		Assert.True(history.Remove(entity));
		Assert.False(history.Remove(entity));
		Assert.Equal(0, history.Count);
	}

	[Fact]
	public void Clear_RemovesEveryEntry()
	{
		using World world = new();

		CreateRenderable(world, Vector3.Zero);
		CreateRenderable(world, Vector3.One);

		TransformHistory history = new(world);

		Assert.Equal(2, history.Count);

		history.Clear();

		Assert.Equal(0, history.Count);
	}

	[Fact]
	public void Snap_CollapsesHistoryOntoTheCurrentPose()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(5.0f));
		history.Capture();

		// Teleport far away, then snap: no interpolation sweep.
		SetPosition(world, entity, new Vector3(100.0f));

		Assert.True(history.Snap(entity));

		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));

		Assert.Equal(new Vector3(100.0f), previous.Position);
		Assert.Equal(new Vector3(100.0f), current.Position);
	}

	[Fact]
	public void SnapAll_CollapsesEveryEntity()
	{
		using World world = new();
		Entity first = CreateRenderable(world, Vector3.Zero);
		Entity second = CreateRenderable(world, Vector3.One);

		TransformHistory history = new(world);

		SetPosition(world, first, new Vector3(10.0f));
		SetPosition(world, second, new Vector3(20.0f));

		history.SnapAll();

		Assert.True(history.TryGetPose(first, out TransformPose firstPrevious, out _));
		Assert.True(history.TryGetPose(second, out TransformPose secondPrevious, out _));

		Assert.Equal(new Vector3(10.0f), firstPrevious.Position);
		Assert.Equal(new Vector3(20.0f), secondPrevious.Position);
	}

	[Fact]
	public void Snap_UnknownEntity_ReturnsFalse()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		Assert.True(world.DestroyEntity(entity));
		Assert.False(history.Snap(entity));
	}

	[Fact]
	public void ArchetypeMigration_PreservesTheEntityEntry()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(1.0f));
		history.Capture();

		// Adding a component moves the entity to a new archetype and
		// bumps the structural version.
		world.AddComponent(entity, new TagComponent { Value = 7 });

		SetPosition(world, entity, new Vector3(2.0f));
		history.Capture();

		Assert.True(history.TryGetPose(entity, out TransformPose previous, out TransformPose current));

		Assert.Equal(new Vector3(1.0f), previous.Position);
		Assert.Equal(new Vector3(2.0f), current.Position);
	}

	[Fact]
	public void NonRenderableEntities_HaveNoHistory()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(Vector3.One));
		world.AddComponent(entity, RotationComponent.Identity);
		world.AddComponent(entity, ScaleComponent.Identity);
		world.AddComponent(entity, new MeshRendererComponent(
			default,
			DefaultMaterial));

		TransformHistory history = new(world);

		Assert.Equal(0, history.Count);
	}

	[Fact]
	public void RepeatedCapture_AllocatesNothing()
	{
		using World world = new();

		for (int index = 0; index < 8; index++)
		{
			CreateRenderable(world, new Vector3(index));
		}

		TransformHistory history = new(world, initialCapacity: 8);

		for (int index = 0; index < 100; index++)
		{
			history.Capture();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1000; index++)
		{
			history.Capture();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	[Fact]
	public void CreationOrder_DoesNotChangeHistory()
	{
		using World firstWorld = new();
		using World secondWorld = new();

		Entity firstA = CreateRenderable(firstWorld, new Vector3(1.0f));
		Entity firstB = CreateRenderable(firstWorld, new Vector3(2.0f));

		Entity secondB = CreateRenderable(secondWorld, new Vector3(2.0f));
		Entity secondA = CreateRenderable(secondWorld, new Vector3(1.0f));

		TransformHistory firstHistory = new(firstWorld);
		TransformHistory secondHistory = new(secondWorld);

		SetPosition(firstWorld, firstA, new Vector3(10.0f));
		SetPosition(firstWorld, firstB, new Vector3(20.0f));
		SetPosition(secondWorld, secondA, new Vector3(10.0f));
		SetPosition(secondWorld, secondB, new Vector3(20.0f));

		firstHistory.Capture();
		secondHistory.Capture();

		Assert.True(firstHistory.TryGetPose(firstA, out _, out TransformPose firstCurrent));
		Assert.True(secondHistory.TryGetPose(secondA, out _, out TransformPose secondCurrent));

		Assert.Equal(firstCurrent.Position, secondCurrent.Position);
	}

	[Fact]
	public void InvalidMesh_PrunesHistoryAndReactivationStartsSnapped()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		SetMesh(world, entity, default);
		history.Capture();

		Assert.Equal(0, history.Count);
		Assert.False(history.TryGetPose(entity, out _, out _));

		// Reactivate far from the pre-disappearance pose: the first
		// valid capture must hold, not sweep.
		SetPosition(world, entity, new Vector3(500.0f));
		SetMesh(world, entity, DefaultMesh);
		history.Capture();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(500.0f), previous.Position);
		Assert.Equal(new Vector3(500.0f), current.Position);

		SetPosition(world, entity, new Vector3(510.0f));
		history.Capture();

		Assert.True(history.TryGetPose(entity, out previous, out current));

		Assert.Equal(new Vector3(500.0f), previous.Position);
		Assert.Equal(new Vector3(510.0f), current.Position);
	}

	[Fact]
	public void InvalidMaterial_PrunesHistoryAndReactivationStartsSnapped()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		SetMaterial(world, entity, default);
		history.Capture();

		Assert.Equal(0, history.Count);

		SetPosition(world, entity, new Vector3(700.0f));
		SetMaterial(world, entity, DefaultMaterial);
		history.Capture();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(700.0f), previous.Position);
		Assert.Equal(new Vector3(700.0f), current.Position);
	}

	[Fact]
	public void RemovedSpatialComponent_PrunesHistoryAndReactivationStartsSnapped()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		Assert.True(world.RemoveComponent<ScaleComponent>(entity));
		history.Capture();

		Assert.Equal(0, history.Count);
		Assert.False(history.TryGetPose(entity, out _, out _));

		SetPosition(world, entity, new Vector3(900.0f));
		world.AddComponent(entity, ScaleComponent.Identity);
		history.Capture();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(900.0f), previous.Position);
		Assert.Equal(new Vector3(900.0f), current.Position);
	}

	[Fact]
	public void RemovedRenderer_PrunesHistoryAndReactivationStartsSnapped()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		Assert.True(world.RemoveComponent<MeshRendererComponent>(entity));
		history.Capture();

		Assert.Equal(0, history.Count);

		SetPosition(world, entity, new Vector3(1_100.0f));
		world.AddComponent(entity, new MeshRendererComponent(
			DefaultMesh,
			DefaultMaterial));

		history.Capture();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(1_100.0f), previous.Position);
		Assert.Equal(new Vector3(1_100.0f), current.Position);
	}

	[Fact]
	public void DestroyedEntity_IsPrunedByTheNextCapture()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		Assert.True(world.DestroyEntity(entity));
		history.Capture();

		Assert.Equal(0, history.Count);
		Assert.False(history.TryGetPose(entity, out _, out _));
	}

	[Fact]
	public void RecycledSlot_AfterPruning_StartsSnapped()
	{
		using World world = new();
		Entity first = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, first, new Vector3(10.0f));
		history.Capture();

		uint index = first.Index;
		uint generation = first.Generation;

		Assert.True(world.DestroyEntity(first));
		history.Capture();

		Entity second = CreateRenderable(world, new Vector3(-42.0f));

		Assert.Equal(index, second.Index);
		Assert.NotEqual(generation, second.Generation);

		history.Capture();

		Assert.True(history.TryGetPose(
			second,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(-42.0f), previous.Position);
		Assert.Equal(new Vector3(-42.0f), current.Position);

		// The stale generation cannot read the new occupant's pose.
		Assert.False(history.TryGetPose(first, out _, out _));
	}

	[Fact]
	public void NewRenderableEntity_StartsSnappedUntilASecondCapture()
	{
		using World world = new();
		Entity existing = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		Entity created = CreateRenderable(world, new Vector3(5.0f));
		history.Capture();

		Assert.True(history.TryGetPose(
			created,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(5.0f), previous.Position);
		Assert.Equal(new Vector3(5.0f), current.Position);

		SetPosition(world, created, new Vector3(6.0f));
		history.Capture();

		Assert.True(history.TryGetPose(created, out previous, out current));

		Assert.Equal(new Vector3(5.0f), previous.Position);
		Assert.Equal(new Vector3(6.0f), current.Position);

		// The entity that stayed renderable keeps its pair.
		Assert.True(history.TryGetPose(existing, out previous, out current));
		Assert.Equal(Vector3.Zero, previous.Position);
		Assert.Equal(Vector3.Zero, current.Position);
	}

	[Fact]
	public void ArchetypeMigrationToNonRenderable_PrunesAndReactivationSnaps()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		// Leaving the query is a migration, not a new entity.
		Assert.True(world.RemoveComponent<RotationComponent>(entity));
		history.Capture();

		Assert.Equal(0, history.Count);

		SetPosition(world, entity, new Vector3(1_300.0f));
		world.AddComponent(entity, RotationComponent.Identity);

		history.Capture();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(1_300.0f), previous.Position);
		Assert.Equal(new Vector3(1_300.0f), current.Position);
	}

	[Fact]
	public void RepeatedCreationAndDestruction_KeepsActiveHistoryBounded()
	{
		using World world = new();
		TransformHistory history = new(world);
		Entity[] batch = new Entity[32];

		for (int cycle = 0; cycle < 200; cycle++)
		{
			for (int index = 0; index < batch.Length; index++)
			{
				batch[index] = CreateRenderable(
					world,
					new Vector3(index));
			}

			history.Capture();

			Assert.Equal(batch.Length, history.Count);
			Assert.True(history.Capacity >= history.Count);

			for (int index = 0; index < batch.Length; index++)
			{
				Assert.True(world.DestroyEntity(batch[index]));
			}

			history.Capture();

			Assert.Equal(0, history.Count);

			for (int index = 0; index < batch.Length; index++)
			{
				Assert.False(history.TryGetPose(
					batch[index],
					out _,
					out _));
			}
		}

		// Capacity is a high-water mark of the dictionary, not a
		// running total of every entity ever seen.
		int capacityAfterChurn = history.Capacity;

		Assert.True(capacityAfterChurn >= batch.Length);

		for (int cycle = 0; cycle < 200; cycle++)
		{
			for (int index = 0; index < batch.Length; index++)
			{
				batch[index] = CreateRenderable(
					world,
					new Vector3(index));
			}

			history.Capture();

			for (int index = 0; index < batch.Length; index++)
			{
				Assert.True(world.DestroyEntity(batch[index]));
			}

			history.Capture();
		}

		Assert.Equal(0, history.Count);
		Assert.Equal(capacityAfterChurn, history.Capacity);
	}

	[Fact]
	public void PruningUnderChurn_AllocatesNothingAfterWarmup()
	{
		using World world = new();
		Entity[] entities = new Entity[32];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = CreateRenderable(world, new Vector3(index));
		}

		TransformHistory history = new(
			world,
			initialCapacity: entities.Length);

		for (int cycle = 0; cycle < 20; cycle++)
		{
			SetAllMeshes(world, entities, default);
			history.Capture();
			SetAllMeshes(world, entities, DefaultMesh);
			history.Capture();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int cycle = 0; cycle < 200; cycle++)
		{
			SetAllMeshes(world, entities, default);
			history.Capture();
			SetAllMeshes(world, entities, DefaultMesh);
			history.Capture();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
		Assert.Equal(entities.Length, history.Count);
	}

	[Fact]
	public void Clear_ReleasesRetainedCapacityAndCaptureRepopulates()
	{
		using World world = new();
		Entity[] entities = new Entity[128];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = CreateRenderable(
				world,
				new Vector3(index));
		}

		TransformHistory history = new(
			world,
			initialCapacity: entities.Length);

		Assert.Equal(entities.Length, history.Count);
		Assert.True(history.Capacity >= entities.Length);

		history.Clear();

		Assert.Equal(0, history.Count);
		Assert.Equal(0, history.Capacity);

		history.Capture();

		Assert.Equal(entities.Length, history.Count);
		Assert.True(history.Capacity >= entities.Length);

		for (int index = 0; index < entities.Length; index++)
		{
			Assert.True(history.TryGetPose(
				entities[index],
				out TransformPose previous,
				out TransformPose current));

			Assert.Equal(previous.Position, current.Position);
		}
	}

	[Fact]
	public void SnapAll_AfterRecycledSlot_KeepsOnlyTheNewGeneration()
	{
		using World world = new();
		Entity first = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, first, new Vector3(10.0f));
		history.Capture();

		uint index = first.Index;
		uint generation = first.Generation;

		Assert.True(world.DestroyEntity(first));

		Entity second = CreateRenderable(world, new Vector3(500.0f));

		Assert.Equal(index, second.Index);
		Assert.NotEqual(generation, second.Generation);

		// No Capture between destruction/recreation and the bulk snap.
		history.SnapAll();

		Assert.Equal(1, history.Count);

		Assert.True(history.TryGetPose(
			second,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(500.0f), previous.Position);
		Assert.Equal(new Vector3(500.0f), current.Position);
		Assert.False(history.TryGetPose(first, out _, out _));
	}

	[Fact]
	public void SnapAll_PrunesDestroyedAndNonRenderableEntries()
	{
		using World world = new();
		Entity destroyed = CreateRenderable(world, Vector3.Zero);
		Entity invalid = CreateRenderable(world, Vector3.One);

		TransformHistory history = new(world);

		SetPosition(world, destroyed, new Vector3(10.0f));
		SetPosition(world, invalid, new Vector3(20.0f));
		history.Capture();

		Assert.True(world.DestroyEntity(destroyed));
		SetMesh(world, invalid, default);

		// No Capture: the bulk snap itself must reconcile both.
		history.SnapAll();

		Assert.Equal(0, history.Count);
		Assert.False(history.TryGetPose(destroyed, out _, out _));
		Assert.False(history.TryGetPose(invalid, out _, out _));

		SetPosition(world, invalid, new Vector3(30.0f));
		SetMesh(world, invalid, DefaultMesh);
		history.SnapAll();

		Assert.Equal(1, history.Count);

		Assert.True(history.TryGetPose(
			invalid,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(30.0f), previous.Position);
		Assert.Equal(new Vector3(30.0f), current.Position);
	}

	[Fact]
	public void SnapAll_PrunesEntityThatLostARequiredComponent()
	{
		using World world = new();
		Entity entity = CreateRenderable(world, Vector3.Zero);

		TransformHistory history = new(world);

		SetPosition(world, entity, new Vector3(10.0f));
		history.Capture();

		Assert.True(world.RemoveComponent<RotationComponent>(entity));
		history.SnapAll();

		Assert.Equal(0, history.Count);
		Assert.False(history.TryGetPose(entity, out _, out _));

		SetPosition(world, entity, new Vector3(40.0f));
		world.AddComponent(entity, RotationComponent.Identity);
		history.SnapAll();

		Assert.True(history.TryGetPose(
			entity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(new Vector3(40.0f), previous.Position);
		Assert.Equal(new Vector3(40.0f), current.Position);
	}

	[Fact]
	public void SnapAll_AllocatesNothingAfterWarmup()
	{
		using World world = new();
		Entity[] entities = new Entity[32];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = CreateRenderable(
				world,
				new Vector3(index));
		}

		TransformHistory history = new(
			world,
			initialCapacity: entities.Length);

		for (int iteration = 0; iteration < 100; iteration++)
		{
			history.SnapAll();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < 1000; iteration++)
		{
			history.SnapAll();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
		Assert.Equal(entities.Length, history.Count);
	}

	private static Entity CreateRenderable(
		World world,
		Vector3 position,
		MeshHandle? mesh = null)
	{
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(position));
		world.AddComponent(entity, RotationComponent.Identity);
		world.AddComponent(entity, ScaleComponent.Identity);
		world.AddComponent(entity, new MeshRendererComponent(
			mesh ?? DefaultMesh,
			DefaultMaterial));

		return entity;
	}

	private static void SetPosition(
		World world,
		Entity entity,
		Vector3 position)
	{
		ref PositionComponent component =
			ref world.GetComponent<PositionComponent>(entity);

		component.Value = position;
	}

	private static void SetMesh(
		World world,
		Entity entity,
		MeshHandle mesh)
	{
		ref MeshRendererComponent renderer =
			ref world.GetComponent<MeshRendererComponent>(entity);

		renderer.Mesh = mesh;
	}

	private static void SetMaterial(
		World world,
		Entity entity,
		MaterialHandle material)
	{
		ref MeshRendererComponent renderer =
			ref world.GetComponent<MeshRendererComponent>(entity);

		renderer.Material = material;
	}

	private static void SetAllMeshes(
		World world,
		Entity[] entities,
		MeshHandle mesh)
	{
		for (int index = 0; index < entities.Length; index++)
		{
			SetMesh(world, entities[index], mesh);
		}
	}
}
