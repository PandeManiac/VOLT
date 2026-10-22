using Volt.Ecs;

namespace Volt.Ecs.Tests;

public sealed class WorldEntityDestroyedTests
{
	[Fact]
	public void DestroyEntity_RaisesEventOnce()
	{
		using World world = new();

		Entity entity = world.CreateEntity();
		List<Entity> destroyed = [];

		world.EntityDestroyed += destroyed.Add;

		Assert.True(world.DestroyEntity(entity));
		Assert.False(world.DestroyEntity(entity));

		Entity raised = Assert.Single(destroyed);

		Assert.Equal(entity, raised);
	}

	[Fact]
	public void DestroyEntity_EventSeesCommittedDestruction()
	{
		using World world = new();

		Entity entity = world.CreateEntity();

		bool aliveDuringEvent = true;

		world.EntityDestroyed += raised =>
		{
			aliveDuringEvent = world.IsAlive(raised);
		};

		world.DestroyEntity(entity);

		Assert.False(aliveDuringEvent);
	}

	[Fact]
	public void DestroyEntity_HandlerCanMutateTheWorld()
	{
		using World world = new();

		Entity first = world.CreateEntity();
		Entity? created = null;

		world.EntityDestroyed += _ =>
		{
			// The structural lock has been released; creating an
			// entity here must not deadlock or throw.
			created = world.CreateEntity();
		};

		world.DestroyEntity(first);

		Assert.NotNull(created);
		Assert.True(world.IsAlive(created!.Value));
		Assert.Equal(1, world.Count);
	}

	[Fact]
	public void DestroyEntity_HandlerFailurePropagatesAfterCommit()
	{
		using World world = new();

		Entity entity = world.CreateEntity();

		world.EntityDestroyed += _ =>
			throw new InvalidOperationException("handler failed");

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(
				() => world.DestroyEntity(entity));

		Assert.Equal("handler failed", exception.Message);
		Assert.False(world.IsAlive(entity));
		Assert.Equal(0, world.Count);
	}

	[Fact]
	public void AllocatorId_IsStableAndDistinctPerWorld()
	{
		using World first = new();
		using World second = new();

		Assert.NotEqual(0u, first.AllocatorId);
		Assert.Equal(first.AllocatorId, first.AllocatorId);
		Assert.NotEqual(first.AllocatorId, second.AllocatorId);
	}
}
