
using System.Collections;
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class WorldQueryTests
{
#pragma warning disable CS0649

	private struct Position
	{
		public float X;
	}

	private struct Velocity
	{
		public float X;
	}

	private struct Health
	{
		public int Value;
	}

#pragma warning restore CS0649

	[Fact]
	public void Query_NullQuery_Throws()
	{
		using World world = new();

		Assert.Throws<ArgumentNullException>(() =>
			world.Query(null!));
	}

	[Fact]
	public void Query_EmptyQuery_MatchesAllNonemptyChunks()
	{
		using World world = new();

		world.CreateEntity();
		world.CreateEntity();

		ComponentQuery query = new();

		Assert.Equal(2, CountEntities(world.Query(query)));
	}

	[Fact]
	public void Query_RequiredComponent_FiltersArchetypes()
	{
		using World world = new();

		Entity matching = world.CreateEntity();
		Entity other = world.CreateEntity();

		world.AddComponent(matching, new Position { X = 10 });
		world.AddComponent(other, new Velocity { X = 20 });

		ComponentQuery query = Require<Position>();
		Assert.Equal(1, CountEntities(world.Query(query)));
	}

	[Fact]
	public void Query_ExcludedComponent_FiltersArchetypes()
	{
		using World world = new();

		Entity matching = world.CreateEntity();
		Entity excluded = world.CreateEntity();

		world.AddComponent(matching, new Position { X = 10 });
		world.AddComponent(excluded, new Velocity { X = 20 });

		ComponentQuery query = new(
			excluded: Signature<Velocity>());

		Assert.Equal(1, CountEntities(world.Query(query)));
	}

	[Fact]
	public void Query_AnyComponent_MatchesEitherType()
	{
		using World world = new();

		Entity position = world.CreateEntity();
		Entity velocity = world.CreateEntity();
		Entity health = world.CreateEntity();

		world.AddComponent(position, new Position());
		world.AddComponent(velocity, new Velocity());
		world.AddComponent(health, new Health());

		ComponentQuery query = new(
			any: new ComponentSignature(
				ComponentRegistry.Get<Position>(),
				ComponentRegistry.Get<Velocity>()));

		Assert.Equal(2, CountEntities(world.Query(query)));
	}

	[Fact]
	public void Query_CombinesRequiredExcludedAndAny()
	{
		using World world = new();

		Entity matching = world.CreateEntity();
		Entity excluded = world.CreateEntity();
		Entity missingAny = world.CreateEntity();

		world.AddComponent(matching, new Position());
		world.AddComponent(matching, new Velocity());

		world.AddComponent(excluded, new Position());
		world.AddComponent(excluded, new Velocity());
		world.AddComponent(excluded, new Health());

		world.AddComponent(missingAny, new Position());

		ComponentQuery query = new(
			required: Signature<Position>(),
			excluded: Signature<Health>(),
			any: Signature<Velocity>());

		Assert.Equal(1, CountEntities(world.Query(query)));
	}

	[Fact]
	public void Query_CachedResultIncludesNewArchetypes()
	{
		using World world = new();

		ComponentQuery query = Require<Position>();
		WorldQuery worldQuery = world.Query(query);

		Assert.Equal(0, CountEntities(worldQuery));

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position { X = 42 });

		Assert.Equal(1, CountEntities(worldQuery));
	}

	[Fact]
	public void Query_CachedResultIncludesLaterMatchingArchetypes()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		world.AddComponent(first, new Position());

		WorldQuery worldQuery = world.Query(Require<Position>());

		Assert.Equal(1, CountEntities(worldQuery));

		Entity second = world.CreateEntity();
		world.AddComponent(second, new Position());
		world.AddComponent(second, new Velocity());

		Assert.Equal(2, CountEntities(worldQuery));
	}

	[Fact]
	public void Query_EntityMigrationIntoMatch_UpdatesResults()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		WorldQuery query = world.Query(Require<Position>());

		Assert.Equal(0, CountEntities(query));
		world.AddComponent(entity, new Position());
		Assert.Equal(1, CountEntities(query));
	}

	[Fact]
	public void Query_EntityMigrationOutOfMatch_UpdatesResults()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery query = world.Query(Require<Position>());
		Assert.Equal(1, CountEntities(query));

		world.RemoveComponent<Position>(entity);
		Assert.Equal(0, CountEntities(query));
	}

	[Fact]
	public void Query_DestroyedEntity_IsNotReturned()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery query = world.Query(Require<Position>());
		Assert.Equal(1, CountEntities(query));

		world.DestroyEntity(entity);
		Assert.Equal(0, CountEntities(query));
	}

	[Fact]
	public void Query_EnumeratorDetectsCreationAfterStart()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery.Enumerator enumerator =
			world.Query(Require<Position>()).GetEnumerator();

		Assert.True(enumerator.MoveNext());

		world.CreateEntity();

		Assert.Throws<InvalidOperationException>(() =>
			enumerator.MoveNext());
	}

	[Fact]
	public void Query_EnumeratorDetectsChangeBeforeFirstMove()
	{
		using World world = new();

		WorldQuery.Enumerator enumerator =
			world.Query(new ComponentQuery()).GetEnumerator();

		world.CreateEntity();

		Assert.Throws<InvalidOperationException>(() =>
			enumerator.MoveNext());
	}

	[Fact]
	public void Query_EnumeratorDetectsComponentMigration()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery.Enumerator enumerator =
			world.Query(Require<Position>()).GetEnumerator();

		Assert.True(enumerator.MoveNext());

		world.AddComponent(entity, new Velocity());

		Assert.Throws<InvalidOperationException>(() =>
			enumerator.MoveNext());
	}

	[Fact]
	public void Query_EnumeratorDetectsDestruction()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery.Enumerator enumerator =
			world.Query(Require<Position>()).GetEnumerator();

		Assert.True(enumerator.MoveNext());

		world.DestroyEntity(entity);

		Assert.Throws<InvalidOperationException>(() =>
			enumerator.MoveNext());
	}

	[Fact]
	public void Query_EnumeratorCurrentDetectsStructuralChange()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		WorldQuery.Enumerator enumerator =
			world.Query(Require<Position>()).GetEnumerator();

		Assert.True(enumerator.MoveNext());

		world.CreateEntity();

		Assert.Throws<InvalidOperationException>(() =>
		{
			_ = enumerator.Current;
		});
	}

	[Fact]
	public void Query_EnumeratorResetDetectsStructuralChange()
	{
		using World world = new();

		WorldQuery.Enumerator enumerator =
			world.Query(new ComponentQuery()).GetEnumerator();

		world.CreateEntity();

		Assert.Throws<InvalidOperationException>(() =>
			enumerator.Reset());
	}

	[Fact]
	public void Query_ComponentValueMutation_DoesNotInvalidateIteration()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position { X = 10 });

		WorldQuery.Enumerator enumerator =
			world.Query(Require<Position>()).GetEnumerator();

		Assert.True(enumerator.MoveNext());

		WorldQueryChunk chunk = enumerator.Current;
		Span<Position> positions = chunk.GetSpan<Position>();

		positions[0].X = 99;

		Assert.Equal(99, world.GetComponent<Position>(entity).X);
		Assert.False(enumerator.MoveNext());
	}

	[Fact]
	public void Query_DisposedWorld_RejectsEnumeration()
	{
		World world = new();
		WorldQuery query = world.Query(new ComponentQuery());
		world.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			query.GetEnumerator());
	}

	[Fact]
	public void Query_DisposalDuringIteration_IsDetected()
	{
		World world = new();

		WorldQuery.Enumerator enumerator =
			world.Query(new ComponentQuery()).GetEnumerator();

		world.Dispose();

		Assert.Throws<ObjectDisposedException>(() =>
			enumerator.MoveNext());
	}

	[Fact]
	public void Query_GenericInterface_EnumeratesChunks()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		IEnumerable<WorldQueryChunk> query =
			world.Query(Require<Position>());

		int count = 0;

		foreach (WorldQueryChunk chunk in query)
		{
			count += chunk.Count;
		}

		Assert.Equal(1, count);
	}

	[Fact]
	public void Query_NonGenericInterface_EnumeratesChunks()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		world.AddComponent(entity, new Position());

		IEnumerable query = world.Query(Require<Position>());

		int count = 0;

		foreach (object item in query)
		{
			WorldQueryChunk chunk = Assert.IsType<WorldQueryChunk>(item);
			count += chunk.Count;
		}

		Assert.Equal(1, count);
	}

	private static int CountEntities(WorldQuery query)
	{
		int count = 0;

		foreach (WorldQueryChunk chunk in query)
		{
			Assert.True(chunk.Count > 0);
			count += chunk.Count;
		}

		return count;
	}

	private static ComponentQuery Require<T>()
		where T : unmanaged
	{
		return new ComponentQuery(
			required: Signature<T>());
	}

	private static ComponentSignature Signature<T>()
		where T : unmanaged
	{
		return new ComponentSignature(
			ComponentRegistry.Register<T>());
	}
}
