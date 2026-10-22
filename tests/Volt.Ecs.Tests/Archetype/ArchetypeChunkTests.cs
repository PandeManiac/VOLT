
using System.Runtime.CompilerServices;
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed unsafe class ArchetypeChunkTests
{
#pragma warning disable CS0649

	private struct Position
	{
		public float X;
		public float Y;
		public float Z;
	}

	private struct Velocity
	{
		public float X;
		public float Y;
		public float Z;
	}

	private struct Health
	{
		public int Value;
	}

#pragma warning restore CS0649

	private static ComponentType PositionType => ComponentRegistry.Get<Position>();
	private static ComponentType VelocityType => ComponentRegistry.Get<Velocity>();
	private static ComponentType HealthType => ComponentRegistry.Get<Health>();

	private static Archetype CreateArchetype(params ComponentType[] types)
	{
		return new Archetype(new ComponentSignature(types));
	}

	[Fact]
	public void Constructor_NullArchetype_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => new ArchetypeChunk(null!));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(-1024)]
	public void Constructor_InvalidChunkSize_Throws(int chunkSize)
	{
		Archetype archetype = CreateArchetype(PositionType);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new ArchetypeChunk(archetype, chunkSize));
	}

	[Fact]
	public void Constructor_DefaultChunkSize_UsesExpectedBudget()
	{
		Archetype archetype = CreateArchetype(PositionType);
		using ArchetypeChunk chunk = new(archetype);

		Assert.Equal(16 * 1024, chunk.ChunkSize);
		Assert.True(chunk.AllocatedBytes <= (nuint)chunk.ChunkSize);
	}

	[Fact]
	public void Constructor_PreservesArchetypeReference()
	{
		Archetype archetype = CreateArchetype(PositionType);
		using ArchetypeChunk chunk = new(archetype);
		Assert.Same(archetype, chunk.Archetype);
	}

	[Fact]
	public void Constructor_InitialState_IsEmpty()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Equal(0, chunk.Count);
		Assert.True(chunk.IsEmpty);
		Assert.False(chunk.IsFull);
		Assert.True(chunk.Capacity > 0);
		Assert.Equal(2, chunk.ComponentCount);
	}

	[Fact]
	public void Constructor_EmptyArchetype_AllocatesEntityStorage()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		Assert.Equal(0, chunk.ComponentCount);
		Assert.True(chunk.Capacity > 0);
		Assert.True(chunk.AllocatedBytes > 0);
		Assert.True(chunk.GetEntityColumnPointer() != null);
	}

	[Fact]
	public void Constructor_SmallBudgetThatCannotFitEntity_Throws()
	{
		Archetype archetype = CreateArchetype();
		int chunkSize = Unsafe.SizeOf<Entity>() - 1;

		Assert.Throws<ArgumentException>(
			() => new ArchetypeChunk(archetype, chunkSize));
	}

	[Fact]
	public void Constructor_SmallBudgetThatCannotFitComponentLayout_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		Assert.Throws<ArgumentException>(
			() => new ArchetypeChunk(archetype, 16));
	}

	[Theory]
	[InlineData(256)]
	[InlineData(1024)]
	[InlineData(4096)]
	[InlineData(16384)]
	[InlineData(32768)]
	public void Allocation_StaysWithinConfiguredBudget(int chunkSize)
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype, chunkSize);

		Assert.True(chunk.AllocatedBytes > 0);
		Assert.True(chunk.AllocatedBytes <= (nuint)chunkSize);
	}

	[Fact]
	public void Allocation_IsAlignedToMinimumAlignment()
	{
		Archetype archetype = CreateArchetype(PositionType);
		using ArchetypeChunk chunk = new(archetype);

		nuint address = (nuint)chunk.GetEntityColumnPointer();

		Assert.Equal((nuint)0, address % 16);
		Assert.Equal((nuint)0, chunk.AllocatedBytes % 16);
	}

	[Fact]
	public void Allocation_IsZeroInitialized()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType);

		using ArchetypeChunk chunk = new(archetype);
		byte* memory = (byte*)chunk.GetEntityColumnPointer();

		for (nuint i = 0; i < chunk.AllocatedBytes; i++)
		{
			Assert.Equal((byte)0, memory[i]);
		}
	}

	[Fact]
	public void EntityColumn_BeginsAtAllocationBase()
	{
		Archetype archetype = CreateArchetype(PositionType);
		using ArchetypeChunk chunk = new(archetype);

		Assert.True(chunk.GetEntityColumnPointer() != null);

		Assert.Equal(
			(nuint)0,
			(nuint)chunk.GetEntityColumnPointer() % 16);
	}

	[Fact]
	public void ComponentColumns_AreOrderedAndNonOverlapping()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype);

		nuint entityColumnEnd =
			(nuint)chunk.Capacity * (nuint)Unsafe.SizeOf<Entity>();

		nuint previousEnd = entityColumnEnd;

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			nuint offset = chunk.GetComponentOffset(i);
			nuint stride = chunk.GetComponentStride(i);

			Assert.True(offset >= previousEnd);
			Assert.True(stride > 0);

			previousEnd = offset + (nuint)chunk.Capacity * stride;
		}

		Assert.True(previousEnd <= chunk.AllocatedBytes);
	}

	[Fact]
	public void ComponentColumns_HaveCorrectMetadataOrdering()
	{
		Archetype archetype = CreateArchetype(
			HealthType,
			PositionType,
			VelocityType);

		using ArchetypeChunk chunk = new(archetype);

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			ComponentRegistry.ComponentInfo info =
				archetype.GetComponentInfoAt(i);

			Assert.True(
				chunk.GetComponentStride(i) >= (nuint)info.Size);

			Assert.True(
				chunk.GetComponentColumnPointer(i) != null);
		}
	}


	[Fact]
	public void ComponentColumns_HaveAlignedBasesAndCompactStrides()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype);

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			ComponentRegistry.ComponentInfo info =
				archetype.GetComponentInfoAt(i);

			nuint alignment = (nuint)info.Alignment;
			nuint stride = chunk.GetComponentStride(i);

			byte* column = (byte*)chunk.GetComponentColumnPointer(i);

			Assert.Equal((nuint)0, (nuint)column % alignment);
			Assert.Equal((nuint)info.Size, stride);

			for (int row = 0; row < chunk.Capacity - 1; row++)
			{
				byte* current = column + (nuint)row * stride;
				byte* next = column + (nuint)(row + 1) * stride;

				Assert.Equal(stride, (nuint)(next - current));
			}
		}
	}

	[Fact]
	public void ComponentColumnPointers_MatchReportedOffsets()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType);

		using ArchetypeChunk chunk = new(archetype);
		nuint baseAddress = (nuint)chunk.GetEntityColumnPointer();

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			nuint actual =
				(nuint)chunk.GetComponentColumnPointer(i);

			Assert.Equal(
				baseAddress + chunk.GetComponentOffset(i),
				actual);
		}
	}

	[Fact]
	public void Capacity_IsMaximumThatFitsBudget()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype, 4096);
		Assert.True(chunk.AllocatedBytes <= (nuint)chunk.ChunkSize);

		nuint alignment = 16;

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			ComponentRegistry.ComponentInfo info =
				archetype.GetComponentInfoAt(i);

			alignment = Math.Max(
				alignment,
				(nuint)info.Alignment);
		}

		nuint nextCapacity = (nuint)chunk.Capacity + 1;
		nuint required = nextCapacity * (nuint)Unsafe.SizeOf<Entity>();

		for (int i = 0; i < chunk.ComponentCount; i++)
		{
			ComponentRegistry.ComponentInfo info =
				archetype.GetComponentInfoAt(i);

			nuint componentAlignment = Math.Max(
				(nuint)info.Alignment,
				16);

			required = AlignUp(required, componentAlignment);
			required += nextCapacity * chunk.GetComponentStride(i);
		}

		required = AlignUp(required, alignment);

		Assert.True(required > (nuint)chunk.ChunkSize);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void GetComponentOffset_InvalidIndex_Throws(int index)
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetComponentOffset(index));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void GetComponentStride_InvalidIndex_Throws(int index)
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetComponentStride(index));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1)]
	[InlineData(int.MaxValue)]
	public void GetComponentColumnPointer_InvalidIndex_Throws(int index)
	{
		Archetype archetype = CreateArchetype(PositionType);
		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetComponentColumnPointer(index));
	}

	[Fact]
	public void Dispose_MultipleCalls_AreSafe()
	{
		Archetype archetype = CreateArchetype(PositionType);
		ArchetypeChunk chunk = new(archetype);

		chunk.Dispose();
		chunk.Dispose();
		chunk.Dispose();
	}

	[Fact]
	public void Dispose_AllocatedBytes_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => _ = chunk.AllocatedBytes);
	}

	[Fact]
	public void Dispose_ComponentOffset_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetComponentOffset(0));
	}

	[Fact]
	public void Dispose_ComponentStride_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetComponentStride(0));
	}

	[Fact]
	public void Dispose_ComponentColumnPointer_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetComponentColumnPointer(0));
	}

	[Fact]
	public void Dispose_EntityColumnPointer_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetEntityColumnPointer());
	}


	[Fact]
	public void Add_FirstEntity_ReturnsRowZero()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);
		Entity entity = new(1, 0, 1);

		int row = chunk.Add(entity);

		Assert.Equal(0, row);
		Assert.Equal(1, chunk.Count);
		Assert.Equal(entity, chunk.GetEntity(row));
		Assert.False(chunk.IsEmpty);
	}

	[Fact]
	public void Add_MultipleEntities_ReturnsSequentialRows()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		for (int i = 0; i < 10; i++)
		{
			Entity entity = new(1, (uint)i, 1);

			int row = chunk.Add(entity);

			Assert.Equal(i, row);
			Assert.Equal(entity, chunk.GetEntity(row));
		}

		Assert.Equal(10, chunk.Count);
	}

	[Fact]
	public void Add_InvalidEntity_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentException>(
			() => chunk.Add(default));

		Assert.Equal(0, chunk.Count);
	}

	[Fact]
	public void Add_UntilCapacity_FillsChunk()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype, 256);

		for (int i = 0; i < chunk.Capacity; i++)
		{
			int row = chunk.Add(new Entity(1, (uint)i, 1));

			Assert.Equal(i, row);
		}

		Assert.Equal(chunk.Capacity, chunk.Count);
		Assert.True(chunk.IsFull);
		Assert.False(chunk.IsEmpty);
	}

	[Fact]
	public void Add_WhenFull_ThrowsWithoutChangingCount()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype, 256);

		for (int i = 0; i < chunk.Capacity; i++)
		{
			chunk.Add(new Entity(1, (uint)i, 1));
		}

		int countBefore = chunk.Count;

		Assert.Throws<InvalidOperationException>(
			() => chunk.Add(new Entity(1, 9999, 1)));

		Assert.Equal(countBefore, chunk.Count);
	}

	[Fact]
	public void Add_EmptyArchetype_StoresEntities()
	{
		Archetype archetype = CreateArchetype();

		using ArchetypeChunk chunk = new(archetype, 256);

		Entity first = new(1, 0, 1);
		Entity second = new(1, 1, 1);

		Assert.Equal(0, chunk.Add(first));
		Assert.Equal(1, chunk.Add(second));

		Assert.Equal(first, chunk.GetEntity(0));
		Assert.Equal(second, chunk.GetEntity(1));
		Assert.Equal(2, chunk.Count);
	}

	[Fact]
	public void GetEntity_InvalidRow_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetEntity(-1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetEntity(1));
	}

	[Fact]
	public void GetEntity_EmptyChunk_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetEntity(0));
	}

	[Fact]
	public void SetComponent_ThenGetComponent_RoundTripsValue()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		Position expected = new()
		{
			X = 1.5f,
			Y = 2.5f,
			Z = 3.5f
		};

		chunk.SetComponent(row, expected);

		ref Position actual = ref chunk.GetComponent<Position>(row);

		Assert.Equal(expected.X, actual.X);
		Assert.Equal(expected.Y, actual.Y);
		Assert.Equal(expected.Z, actual.Z);
	}

	[Fact]
	public void GetComponent_ReturnsWritableReference()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		ref Position position = ref chunk.GetComponent<Position>(row);

		position.X = 10;
		position.Y = 20;
		position.Z = 30;

		Position actual = chunk.GetComponent<Position>(row);

		Assert.Equal(10, actual.X);
		Assert.Equal(20, actual.Y);
		Assert.Equal(30, actual.Z);
	}

	[Fact]
	public void GetComponent_NewEntity_IsZeroInitialized()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		Position position = chunk.GetComponent<Position>(row);
		Health health = chunk.GetComponent<Health>(row);

		Assert.Equal(0, position.X);
		Assert.Equal(0, position.Y);
		Assert.Equal(0, position.Z);
		Assert.Equal(0, health.Value);
	}

	[Fact]
	public void SetComponent_MultipleTypes_RemainIndependent()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		chunk.SetComponent(row, new Position
		{
			X = 1,
			Y = 2,
			Z = 3
		});

		chunk.SetComponent(row, new Velocity
		{
			X = 4,
			Y = 5,
			Z = 6
		});

		chunk.SetComponent(row, new Health
		{
			Value = 100
		});

		Position position = chunk.GetComponent<Position>(row);
		Velocity velocity = chunk.GetComponent<Velocity>(row);
		Health health = chunk.GetComponent<Health>(row);

		Assert.Equal(1, position.X);
		Assert.Equal(2, position.Y);
		Assert.Equal(3, position.Z);

		Assert.Equal(4, velocity.X);
		Assert.Equal(5, velocity.Y);
		Assert.Equal(6, velocity.Z);

		Assert.Equal(100, health.Value);
	}

	[Fact]
	public void SetComponent_DifferentRows_RemainIndependent()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype);

		int first = chunk.Add(new Entity(1, 0, 1));
		int second = chunk.Add(new Entity(1, 1, 1));

		chunk.SetComponent(first, new Health { Value = 100 });
		chunk.SetComponent(second, new Health { Value = 25 });

		Assert.Equal(100, chunk.GetComponent<Health>(first).Value);
		Assert.Equal(25, chunk.GetComponent<Health>(second).Value);
	}

	[Fact]
	public void GetComponent_MissingType_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentException>(
			() => chunk.GetComponent<Health>(row));
	}

	[Fact]
	public void SetComponent_MissingType_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentException>(
			() => chunk.SetComponent(row, new Health { Value = 10 }));
	}

	[Fact]
	public void GetComponent_InvalidRow_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetComponent<Position>(-1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.GetComponent<Position>(1));
	}

	[Fact]
	public void SetComponent_InvalidRow_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.SetComponent(-1, new Position()));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.SetComponent(1, new Position()));
	}

	[Fact]
	public void RemoveAt_LastRow_DoesNotMoveAnotherEntity()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype);

		Entity first = new(1, 0, 1);
		Entity second = new(1, 1, 1);

		chunk.Add(first);
		chunk.Add(second);

		bool moved = chunk.RemoveAt(1, out Entity movedEntity);

		Assert.False(moved);
		Assert.Equal(default, movedEntity);
		Assert.Equal(1, chunk.Count);
		Assert.Equal(first, chunk.GetEntity(0));
	}

	[Fact]
	public void RemoveAt_MiddleRow_MovesLastEntity()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype);

		Entity first = new(1, 0, 1);
		Entity second = new(1, 1, 1);
		Entity third = new(1, 2, 1);

		chunk.Add(first);
		chunk.Add(second);
		chunk.Add(third);

		bool moved = chunk.RemoveAt(1, out Entity movedEntity);

		Assert.True(moved);
		Assert.Equal(third, movedEntity);
		Assert.Equal(2, chunk.Count);
		Assert.Equal(first, chunk.GetEntity(0));
		Assert.Equal(third, chunk.GetEntity(1));
	}

	[Fact]
	public void RemoveAt_MiddleRow_MovesAllComponentData()
	{
		Archetype archetype = CreateArchetype(
			PositionType,
			VelocityType,
			HealthType);

		using ArchetypeChunk chunk = new(archetype);

		Entity first = new(1, 0, 1);
		Entity second = new(1, 1, 1);
		Entity third = new(1, 2, 1);

		chunk.Add(first);
		chunk.Add(second);
		chunk.Add(third);

		chunk.SetComponent(2, new Position
		{
			X = 10,
			Y = 20,
			Z = 30
		});

		chunk.SetComponent(2, new Velocity
		{
			X = 40,
			Y = 50,
			Z = 60
		});

		chunk.SetComponent(2, new Health
		{
			Value = 75
		});

		bool moved = chunk.RemoveAt(1, out Entity movedEntity);

		Assert.True(moved);
		Assert.Equal(third, movedEntity);
		Assert.Equal(third, chunk.GetEntity(1));

		Position position = chunk.GetComponent<Position>(1);
		Velocity velocity = chunk.GetComponent<Velocity>(1);
		Health health = chunk.GetComponent<Health>(1);

		Assert.Equal(10, position.X);
		Assert.Equal(20, position.Y);
		Assert.Equal(30, position.Z);

		Assert.Equal(40, velocity.X);
		Assert.Equal(50, velocity.Y);
		Assert.Equal(60, velocity.Z);

		Assert.Equal(75, health.Value);
	}

	[Fact]
	public void RemoveAt_OnlyEntity_EmptiesChunk()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		bool moved = chunk.RemoveAt(0, out Entity movedEntity);

		Assert.False(moved);
		Assert.Equal(default, movedEntity);
		Assert.Equal(0, chunk.Count);
		Assert.True(chunk.IsEmpty);
		Assert.False(chunk.IsFull);
	}

	[Fact]
	public void RemoveAt_InvalidRow_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.RemoveAt(-1, out _));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.RemoveAt(1, out _));

		Assert.Equal(1, chunk.Count);
	}

	[Fact]
	public void RemoveAt_EmptyChunk_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => chunk.RemoveAt(0, out _));
	}

	[Fact]
	public void RemoveAt_ThenAdd_ReusesFreedRow()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));
		chunk.Add(new Entity(1, 1, 1));

		bool moved = chunk.RemoveAt(1, out Entity movedEntity);

		Assert.False(moved);
		Assert.Equal(default, movedEntity);

		Entity replacement = new(1, 2, 1);

		int row = chunk.Add(replacement);

		Assert.Equal(1, row);
		Assert.Equal(replacement, chunk.GetEntity(row));
		Assert.Equal(2, chunk.Count);
	}

	[Fact]
	public void RemoveAt_ThenAdd_ClearsPreviousComponentData()
	{
		Archetype archetype = CreateArchetype(HealthType);
		using ArchetypeChunk chunk = new(archetype);

		int row = chunk.Add(new Entity(1, 0, 1));

		chunk.SetComponent(row, new Health { Value = 999 });

		bool moved = chunk.RemoveAt(row, out Entity movedEntity);

		Assert.False(moved);
		Assert.Equal(default, movedEntity);

		int replacementRow = chunk.Add(new Entity(1, 1, 1));

		Assert.Equal(0, replacementRow);
		Assert.Equal(0, chunk.GetComponent<Health>(replacementRow).Value);
	}

	[Fact]
	public void RemoveAt_EmptyArchetype_MaintainsDenseEntityRows()
	{
		Archetype archetype = CreateArchetype();

		using ArchetypeChunk chunk = new(archetype);

		Entity first = new(1, 0, 1);
		Entity second = new(1, 1, 1);
		Entity third = new(1, 2, 1);

		chunk.Add(first);
		chunk.Add(second);
		chunk.Add(third);

		bool moved = chunk.RemoveAt(0, out Entity movedEntity);

		Assert.True(moved);
		Assert.Equal(third, movedEntity);
		Assert.Equal(third, chunk.GetEntity(0));
		Assert.Equal(second, chunk.GetEntity(1));
		Assert.Equal(2, chunk.Count);
	}

	[Fact]
	public void RemoveAt_FullChunk_MakesSpaceForAnotherEntity()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype, 256);

		for (int i = 0; i < chunk.Capacity; i++)
		{
			chunk.Add(new Entity(1, (uint)i, 1));
		}

		Assert.True(chunk.IsFull);

		bool moved = chunk.RemoveAt(0, out Entity movedEntity);

		Assert.True(moved);

		Assert.Equal(
			new Entity(1, (uint)(chunk.Capacity - 1), 1),
			movedEntity);

		Assert.Equal(movedEntity, chunk.GetEntity(0));
		Assert.False(chunk.IsFull);

		int row = chunk.Add(new Entity(1, 999, 1));

		Assert.Equal(chunk.Capacity - 1, row);
		Assert.Equal(new Entity(1, 999, 1), chunk.GetEntity(row));
		Assert.True(chunk.IsFull);
	}

	[Fact]
	public void AddRemoveCycles_PreserveEntityAndComponentData()
	{
		Archetype archetype = CreateArchetype(HealthType);

		using ArchetypeChunk chunk = new(archetype, 1024);

		for (int cycle = 0; cycle < 100; cycle++)
		{
			Entity entity = new(1, (uint)cycle, 1);

			int row = chunk.Add(entity);

			chunk.SetComponent(row, new Health
			{
				Value = cycle + 100
			});

			Assert.Equal(entity, chunk.GetEntity(row));
			Assert.Equal(
				cycle + 100,
				chunk.GetComponent<Health>(row).Value);

			chunk.RemoveAt(row, out _);

			Assert.True(chunk.IsEmpty);
		}
	}

	[Fact]
	public void Dispose_Add_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.Add(new Entity(1, 0, 1)));
	}

	[Fact]
	public void Dispose_GetEntity_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetEntity(0));
	}

	[Fact]
	public void Dispose_GetComponent_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.GetComponent<Position>(0));
	}

	[Fact]
	public void Dispose_SetComponent_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.SetComponent(0, new Position()));
	}

	[Fact]
	public void Dispose_RemoveAt_Throws()
	{
		Archetype archetype = CreateArchetype(PositionType);

		ArchetypeChunk chunk = new(archetype);
		chunk.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => chunk.RemoveAt(0, out _));
	}

	private static nuint AlignUp(nuint value, nuint alignment)
	{
		return checked(value + alignment - 1) & ~(alignment - 1);
	}
}
