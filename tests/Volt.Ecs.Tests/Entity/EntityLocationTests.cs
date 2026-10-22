
using Volt.Ecs.Component;

namespace Volt.Ecs.Tests;

public sealed class EntityLocationTests
{
#pragma warning disable CS0649

	private struct Health
	{
		public int Value;
	}

#pragma warning restore CS0649

	private static readonly ComponentType HealthType = ComponentRegistry.Register<Health>();

	[Fact]
	public void Constructor_StoresChunkAndRow()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		EntityLocation location = new(chunk, 0);

		Assert.Same(chunk, location.Chunk);
		Assert.Equal(0, location.Row);
	}

	[Fact]
	public void Constructor_AcceptsLastOccupiedRow()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));
		chunk.Add(new Entity(1, 1, 1));
		chunk.Add(new Entity(1, 2, 1));

		EntityLocation location = new(chunk, 2);

		Assert.Same(chunk, location.Chunk);
		Assert.Equal(2, location.Row);
	}

	[Fact]
	public void Constructor_RejectsNullChunk()
	{
		Assert.Throws<ArgumentNullException>(
			() => new EntityLocation(null!, 0));
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(-2)]
	[InlineData(int.MinValue)]
	public void Constructor_RejectsNegativeRow(int row)
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new EntityLocation(chunk, row));
	}

	[Fact]
	public void Constructor_RejectsRowInEmptyChunk()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new EntityLocation(chunk, 0));
	}

	[Fact]
	public void Constructor_RejectsRowEqualToCount()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));
		chunk.Add(new Entity(1, 1, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new EntityLocation(chunk, chunk.Count));
	}

	[Fact]
	public void Constructor_RejectsRowBeyondCount()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new EntityLocation(chunk, 100));
	}

	[Fact]
	public void Location_IsNotAutomaticallyUpdatedAfterSwapBack()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));
		chunk.Add(new Entity(1, 1, 1));

		EntityLocation location = new(chunk, 1);

		bool moved = chunk.RemoveAt(0, out Entity movedEntity);

		Assert.True(moved);
		Assert.Equal(new Entity(1, 1, 1), movedEntity);
		Assert.Equal(1, location.Row);
		Assert.Equal(movedEntity, chunk.GetEntity(0));
	}

	[Fact]
	public void Locations_CanReferenceDifferentRowsInSameChunk()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));
		chunk.Add(new Entity(1, 1, 1));

		EntityLocation first = new(chunk, 0);
		EntityLocation second = new(chunk, 1);

		Assert.Same(first.Chunk, second.Chunk);
		Assert.NotEqual(first.Row, second.Row);
	}

	[Fact]
	public void Locations_CanReferenceDifferentChunks()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk firstChunk = new(archetype);
		using ArchetypeChunk secondChunk = new(archetype);

		firstChunk.Add(new Entity(1, 0, 1));
		secondChunk.Add(new Entity(1, 1, 1));

		EntityLocation first = new(firstChunk, 0);
		EntityLocation second = new(secondChunk, 0);

		Assert.NotSame(first.Chunk, second.Chunk);
		Assert.Equal(first.Row, second.Row);
	}

	[Fact]
	public void Copy_PreservesChunkAndRow()
	{
		Archetype archetype = CreateArchetype();
		using ArchetypeChunk chunk = new(archetype);

		chunk.Add(new Entity(1, 0, 1));

		EntityLocation original = new(chunk, 0);
		EntityLocation copy = original;

		Assert.Same(original.Chunk, copy.Chunk);
		Assert.Equal(original.Row, copy.Row);
	}

	[Fact]
	public void Default_HasNullChunkAndZeroRow()
	{
		EntityLocation location = default;

		Assert.Null(location.Chunk);
		Assert.Equal(0, location.Row);
	}

	private static Archetype CreateArchetype()
	{
		ComponentSignature signature = ComponentSignature.Empty.With(HealthType);
		return new Archetype(signature);
	}
}
