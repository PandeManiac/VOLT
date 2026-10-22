
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class WorldQueryChunkTests
{
#pragma warning disable CS0649

	private struct Position
	{
		public float X;
		public float Y;
	}

	private struct Velocity
	{
		public float X;
		public float Y;
	}

	private struct Health
	{
		public int Value;
	}

#pragma warning restore CS0649

	[Fact]
	public void Constructor_NullChunk_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new WorldQueryChunk(null!));
	}

	[Fact]
	public void Properties_ExposeUnderlyingChunk()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();

		WorldQueryChunk queryChunk = new(storage);

		Assert.Equal(0, queryChunk.Count);
		Assert.Same(storage.Archetype, queryChunk.Archetype);

		EntityAllocator allocator = new();
		Entity entity = allocator.Create();

		storage.Add(entity);

		Assert.Equal(1, queryChunk.Count);
	}

	[Fact]
	public void GetEntities_EmptyChunk_ReturnsEmptySpan()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		WorldQueryChunk queryChunk = new(storage);

		Assert.True(queryChunk.GetEntities().IsEmpty);
	}

	[Fact]
	public void GetEntities_ReturnsEntitiesInStorageOrder()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		Entity first = allocator.Create();
		Entity second = allocator.Create();
		Entity third = allocator.Create();

		storage.Add(first);
		storage.Add(second);
		storage.Add(third);

		WorldQueryChunk queryChunk = new(storage);
		ReadOnlySpan<Entity> entities = queryChunk.GetEntities();

		Assert.Equal(3, entities.Length);
		Assert.Equal(first, entities[0]);
		Assert.Equal(second, entities[1]);
		Assert.Equal(third, entities[2]);
	}

	[Fact]
	public void GetSpan_EmptyChunk_ReturnsEmptySpan()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		WorldQueryChunk queryChunk = new(storage);
		Span<Position> positions = queryChunk.GetSpan<Position>();

		Assert.True(positions.IsEmpty);
	}

	[Fact]
	public void GetSpan_ReturnsExistingComponentValues()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		Entity first = allocator.Create();
		Entity second = allocator.Create();

		storage.Add(first);
		storage.Add(second);

		storage.SetComponent(0, new Position { X = 10, Y = 20 });
		storage.SetComponent(1, new Position { X = 30, Y = 40 });

		WorldQueryChunk queryChunk = new(storage);
		Span<Position> positions = queryChunk.GetSpan<Position>();

		Assert.Equal(2, positions.Length);
		Assert.Equal(10, positions[0].X);
		Assert.Equal(20, positions[0].Y);
		Assert.Equal(30, positions[1].X);
		Assert.Equal(40, positions[1].Y);
	}

	[Fact]
	public void GetSpan_MutationsUpdateUnderlyingStorage()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		Entity entity = allocator.Create();
		storage.Add(entity);

		WorldQueryChunk queryChunk = new(storage);
		Span<Position> positions = queryChunk.GetSpan<Position>();

		positions[0] = new Position
		{
			X = 123,
			Y = 456
		};

		ref Position stored = ref storage.GetComponent<Position>(0);

		Assert.Equal(123, stored.X);
		Assert.Equal(456, stored.Y);
	}

	[Fact]
	public void GetSpan_MultipleComponents_ReturnCorrectColumns()
	{
		using ArchetypeChunk storage =
			CreateChunk<Position, Velocity>();

		EntityAllocator allocator = new();

		Entity entity = allocator.Create();
		storage.Add(entity);

		WorldQueryChunk queryChunk = new(storage);

		Span<Position> positions = queryChunk.GetSpan<Position>();
		Span<Velocity> velocities = queryChunk.GetSpan<Velocity>();

		positions[0] = new Position { X = 5, Y = 10 };
		velocities[0] = new Velocity { X = 2, Y = 3 };

		Assert.Equal(5, storage.GetComponent<Position>(0).X);
		Assert.Equal(10, storage.GetComponent<Position>(0).Y);

		Assert.Equal(2, storage.GetComponent<Velocity>(0).X);
		Assert.Equal(3, storage.GetComponent<Velocity>(0).Y);
	}

	[Fact]
	public void GetSpan_MissingComponent_Throws()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		WorldQueryChunk queryChunk = new(storage);

		Assert.Throws<ArgumentException>(() =>
		{
			queryChunk.GetSpan<Health>();
		});
	}

	[Fact]
	public void GetEntities_ReflectsSwapBackRemoval()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		Entity first = allocator.Create();
		Entity second = allocator.Create();
		Entity third = allocator.Create();

		storage.Add(first);
		storage.Add(second);
		storage.Add(third);

		storage.RemoveAt(0, out Entity moved);

		WorldQueryChunk queryChunk = new(storage);
		ReadOnlySpan<Entity> entities = queryChunk.GetEntities();

		Assert.Equal(third, moved);
		Assert.Equal(2, entities.Length);
		Assert.Equal(third, entities[0]);
		Assert.Equal(second, entities[1]);
	}

	[Fact]
	public void GetSpan_ReflectsSwapBackRemoval()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		storage.Add(allocator.Create());
		storage.Add(allocator.Create());
		storage.Add(allocator.Create());

		storage.SetComponent(0, new Position { X = 10 });
		storage.SetComponent(1, new Position { X = 20 });
		storage.SetComponent(2, new Position { X = 30 });

		storage.RemoveAt(0, out _);

		WorldQueryChunk queryChunk = new(storage);
		Span<Position> positions = queryChunk.GetSpan<Position>();

		Assert.Equal(2, positions.Length);
		Assert.Equal(30, positions[0].X);
		Assert.Equal(20, positions[1].X);
	}

	[Fact]
	public void GetSpan_ExposesContiguousStorage()
	{
		using ArchetypeChunk storage = CreateChunk<Position>();
		EntityAllocator allocator = new();

		storage.Add(allocator.Create());
		storage.Add(allocator.Create());

		WorldQueryChunk queryChunk = new(storage);
		Span<Position> positions = queryChunk.GetSpan<Position>();

		ref Position first = ref positions[0];
		ref Position second = ref positions[1];

		nint distance = System.Runtime.CompilerServices.Unsafe.ByteOffset(
			ref first,
			ref second);

		Assert.Equal(
			System.Runtime.CompilerServices.Unsafe.SizeOf<Position>(),
			(int)distance);
	}

	private static ArchetypeChunk CreateChunk<T>()
		where T : unmanaged
	{
		ComponentSignature signature = new(
			ComponentRegistry.Register<T>());

		return new ArchetypeChunk(new Archetype(signature));
	}

	private static ArchetypeChunk CreateChunk<T1, T2>()
		where T1 : unmanaged
		where T2 : unmanaged
	{
		ComponentSignature signature = new(
			ComponentRegistry.Register<T1>(),
			ComponentRegistry.Register<T2>());

		return new ArchetypeChunk(new Archetype(signature));
	}
}
