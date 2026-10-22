
using System.Threading;
using System.Threading.Tasks;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Jobs;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class WorldParallelExecutorTests
{
	private struct Position
	{
		public int Value;
	}

	private struct Velocity
	{
		public int Value;
	}

	private static readonly TimeSpan s_testTimeout =
		TimeSpan.FromSeconds(10);

	[Fact]
	public void Constructor_NullScheduler_Throws()
	{
		Assert.Throws<ArgumentNullException>(() =>
			new WorldParallelExecutor(null!));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void Constructor_InvalidBatchSize_Throws(int batchSize)
	{
		JobScheduler scheduler = new();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new WorldParallelExecutor(scheduler, batchSize));
	}

	[Fact]
	public void Constructor_StoresBatchSize()
	{
		JobScheduler scheduler = new();
		WorldParallelExecutor executor = new(scheduler, 8);

		Assert.Equal(8, executor.ChunksPerBatch);
	}

	[Fact]
	public void Constructor_DefaultBatchSize_IsOne()
	{
		JobScheduler scheduler = new();
		WorldParallelExecutor executor = new(scheduler);

		Assert.Equal(1, executor.ChunksPerBatch);
	}

	[Fact]
	public void ExecuteAsync_NullArguments_Throw()
	{
		JobScheduler scheduler = new();
		WorldParallelExecutor executor = new(scheduler);

		using World world = new();

		ComponentQuery query = CreatePositionQuery();

		Action<WorldQueryChunk> callback = _ => { };

		Assert.Throws<ArgumentNullException>(() =>
		{
			_ = executor.ExecuteAsync(null!, query, callback);
		});

		Assert.Throws<ArgumentNullException>(() =>
		{
			_ = executor.ExecuteAsync(world, null!, callback);
		});

		Assert.Throws<ArgumentNullException>(() =>
		{
			_ = executor.ExecuteAsync(world, query, null!);
		});
	}

	[Fact]
	public async Task ExecuteAsync_EmptyWorld_DoesNotExecute()
	{
		using World world = new();

		WorldParallelExecutor executor = new(
			new JobScheduler(4));

		int executions = 0;

		await executor.ExecuteAsync(
			world,
			CreatePositionQuery(),
			_ =>
			{
				Interlocked.Increment(ref executions);
			});

		Assert.Equal(0, executions);
		Assert.Equal(0, world.Count);
	}

	[Fact]
	public async Task ExecuteAsync_NoMatchingArchetypes_DoesNotExecute()
	{
		using World world = CreateWorldWithPositions(100);

		WorldParallelExecutor executor = new(
			new JobScheduler(4));

		ComponentQuery query = new(
			required: new ComponentSignature(
				ComponentRegistry.Get<Velocity>()));

		int executions = 0;

		await executor.ExecuteAsync(
			world,
			query,
			_ =>
			{
				Interlocked.Increment(ref executions);
			});

		Assert.Equal(0, executions);
		Assert.Equal(100, world.Count);
	}

	[Fact]
	public async Task ExecuteAsync_ProcessesEveryMatchingEntityExactlyOnce()
	{
		const int entityCount = 2500;
		using World world = new();
		Entity[] entities = new Entity[entityCount];

		for (int i = 0; i < entityCount; i++)
		{
			Entity entity = world.CreateEntity();

			world.AddComponent(
				entity,
				new Position { Value = i });

			if (i % 7 == 0)
			{
				world.AddComponent(
					entity,
					new Velocity { Value = 1 });
			}

			entities[i] = entity;
		}

		ComponentQuery query = new(
			required: new ComponentSignature(
				ComponentRegistry.Get<Position>()),
			excluded: new ComponentSignature(
				ComponentRegistry.Get<Velocity>()));

		WorldParallelExecutor executor = new(
			new JobScheduler(4),
			chunksPerBatch: 2);

		int processedEntities = 0;

		await executor.ExecuteAsync(
			world,
			query,
			chunk =>
			{
				Span<Position> positions =
					chunk.GetSpan<Position>();

				for (int i = 0; i < positions.Length; i++)
				{
					positions[i].Value++;
				}

				Interlocked.Add(
					ref processedEntities,
					positions.Length);
			});

		int expectedProcessed = 0;

		for (int i = 0; i < entityCount; i++)
		{
			bool shouldMatch = i % 7 != 0;

			if (shouldMatch)
			{
				expectedProcessed++;
			}

			int expectedValue = i + (shouldMatch ? 1 : 0);

			Assert.Equal(
				expectedValue,
				world.GetComponent<Position>(entities[i]).Value);
		}

		Assert.Equal(expectedProcessed, processedEntities);
		Assert.Equal(entityCount, world.Count);
	}

	[Fact]
	public async Task ExecuteAsync_ProcessesEachChunkOnce()
	{
		using World world = CreateWorldWithPositions(3000);
		ComponentQuery query = CreatePositionQuery();

		int expectedChunks = 0;

		foreach (WorldQueryChunk chunk in world.Query(query))
		{
			expectedChunks++;
		}

		Assert.True(expectedChunks > 1);

		WorldParallelExecutor executor = new(
			new JobScheduler(4),
			chunksPerBatch: 2);

		int executedChunks = 0;
		int processedEntities = 0;

		await executor.ExecuteAsync(
			world,
			query,
			chunk =>
			{
				Interlocked.Increment(ref executedChunks);

				Interlocked.Add(
					ref processedEntities,
					chunk.Count);
			});

		Assert.Equal(expectedChunks, executedChunks);
		Assert.Equal(3000, processedEntities);
	}

	[Fact]
	public async Task ExecuteAsync_AllowsComponentValueMutations()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(
			entity,
			new Position { Value = 10 });

		WorldParallelExecutor executor = new(
			new JobScheduler(2));

		await executor.ExecuteAsync(
			world,
			CreatePositionQuery(),
			chunk =>
			{
				Span<Position> positions =
					chunk.GetSpan<Position>();

				positions[0].Value += 15;
			});

		Assert.Equal(
			25,
			world.GetComponent<Position>(entity).Value);
	}

	[Fact]
	public async Task ExecuteAsync_RejectsStructuralChangesDuringCallback()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(
			entity,
			new Position { Value = 42 });

		WorldParallelExecutor executor = new(
			new JobScheduler(2));

		int executions = 0;

		await executor.ExecuteAsync(
			world,
			CreatePositionQuery(),
			chunk =>
			{
				Interlocked.Increment(ref executions);

				Assert.Throws<InvalidOperationException>(
					() => world.CreateEntity());

				Assert.Throws<InvalidOperationException>(
					() => world.DestroyEntity(entity));

				Assert.Throws<InvalidOperationException>(
					() => world.AddComponent(
						entity,
						new Velocity()));

				Assert.Throws<InvalidOperationException>(
					() => world.RemoveComponent<Position>(entity));

				Assert.Throws<InvalidOperationException>(
					() => world.Dispose());

				Assert.True(world.IsAlive(entity));

				Assert.Equal(
					42,
					chunk.GetSpan<Position>()[0].Value);
			});

		Assert.Equal(1, executions);
		Assert.True(world.IsAlive(entity));

		Assert.Equal(
			42,
			world.GetComponent<Position>(entity).Value);
	}

	[Fact]
	public async Task ExecuteAsync_ReleasesStructuralFreezeAfterSuccess()
	{
		using World world = CreateWorldWithPositions(1);

		WorldParallelExecutor executor = new(
			new JobScheduler(2));

		await executor.ExecuteAsync(
			world,
			CreatePositionQuery(),
			_ => { });

		Entity entity = world.CreateEntity();

		world.AddComponent(
			entity,
			new Position { Value = 123 });

		Assert.True(world.IsAlive(entity));

		Assert.Equal(
			123,
			world.GetComponent<Position>(entity).Value);
	}

	[Fact]
	public async Task ExecuteAsync_JobFailure_ReleasesFreezeAndGate()
	{
		using World world = CreateWorldWithPositions(1);

		WorldParallelExecutor executor = new(
			new JobScheduler(2));

		ComponentQuery query = CreatePositionQuery();

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => executor.ExecuteAsync(
				world,
				query,
				_ =>
				{
					throw new InvalidOperationException(
						"Expected job failure.");
				}));

		Entity created = world.CreateEntity();
		Assert.True(world.IsAlive(created));

		int executions = 0;

		await executor.ExecuteAsync(
			world,
			query,
			_ =>
			{
				Interlocked.Increment(ref executions);
			});

		Assert.Equal(1, executions);
	}

	[Fact]
	public async Task ExecuteAsync_ParallelJobFailure_ReleasesFreeze()
	{
		using World world = CreateWorldWithPositions(3000);

		WorldParallelExecutor executor = new(
			new JobScheduler(4));

		ComponentQuery query = CreatePositionQuery();

		Exception? exception = await Record.ExceptionAsync(
			() => executor.ExecuteAsync(
				world,
				query,
				_ =>
				{
					throw new InvalidOperationException(
						"Expected parallel failure.");
				}));

		Assert.NotNull(exception);

		Assert.True(
			exception is InvalidOperationException
				or AggregateException);

		Entity created = world.CreateEntity();

		Assert.True(world.IsAlive(created));
	}

	[Fact]
	public async Task ExecuteAsync_PreCanceledToken_PreventsExecution()
	{
		using World world = CreateWorldWithPositions(100);

		WorldParallelExecutor executor = new(
			new JobScheduler(4));

		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		int executions = 0;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => executor.ExecuteAsync(
				world,
				CreatePositionQuery(),
				_ =>
				{
					Interlocked.Increment(ref executions);
				},
				cancellation.Token));

		Assert.Equal(0, executions);

		Assert.True(world.IsAlive(world.CreateEntity()));
	}

	[Fact]
	public async Task ExecuteAsync_CancellationDuringWork_ReleasesFreeze()
	{
		using World world = CreateWorldWithPositions(3000);

		WorldParallelExecutor executor = new(
			new JobScheduler(1));

		ComponentQuery query = CreatePositionQuery();

		using CancellationTokenSource cancellation = new();

		int executions = 0;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => executor.ExecuteAsync(
				world,
				query,
				_ =>
				{
					Interlocked.Increment(ref executions);
					cancellation.Cancel();
				},
				cancellation.Token));

		Assert.Equal(1, executions);
		Entity created = world.CreateEntity();
		Assert.True(world.IsAlive(created));
	}

	[Fact]
	public async Task ExecuteAsync_SeparateExecutors_SerializeSameWorld()
	{
		using World world = CreateWorldWithPositions(1);
		ComponentQuery query = CreatePositionQuery();

		WorldParallelExecutor firstExecutor = new(
			new JobScheduler(2));

		WorldParallelExecutor secondExecutor = new(
			new JobScheduler(2));

		TaskCompletionSource<bool> firstEntered = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		using ManualResetEventSlim releaseFirst = new(false);

		int firstActive = 0;
		int overlappingExecution = 0;
		int secondExecutions = 0;

		Task firstTask = firstExecutor.ExecuteAsync(
			world,
			query,
			_ =>
			{
				Interlocked.Exchange(ref firstActive, 1);

				firstEntered.TrySetResult(true);

				try
				{
					if (!releaseFirst.Wait(s_testTimeout))
					{
						throw new TimeoutException(
							"The first execution was not released.");
					}
				}

				finally
				{
					Interlocked.Exchange(ref firstActive, 0);
				}
			});

		Task secondTask = Task.CompletedTask;

		try
		{
			await firstEntered.Task.WaitAsync(s_testTimeout);

			secondTask = secondExecutor.ExecuteAsync(
				world,
				query,
				_ =>
				{
					if (Volatile.Read(ref firstActive) != 0)
					{
						Interlocked.Exchange(
							ref overlappingExecution,
							1);
					}

					Interlocked.Increment(ref secondExecutions);
				});

			Assert.False(secondTask.IsCompleted);

			Assert.Equal(
				0,
				Volatile.Read(ref secondExecutions));
		}

		finally
		{
			releaseFirst.Set();
		}

		await firstTask.WaitAsync(s_testTimeout);
		await secondTask.WaitAsync(s_testTimeout);

		Assert.Equal(0, overlappingExecution);
		Assert.Equal(1, secondExecutions);

		Assert.True(world.IsAlive(world.CreateEntity()));
	}

	[Fact]
	public async Task ExecuteAsync_DisposeDuringExecution_IsRejected()
	{
		using World world = CreateWorldWithPositions(1);

		WorldParallelExecutor executor = new(
			new JobScheduler(2));

		TaskCompletionSource<bool> entered = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		using ManualResetEventSlim release = new(false);

		Task execution = executor.ExecuteAsync(
			world,
			CreatePositionQuery(),
			_ =>
			{
				entered.TrySetResult(true);

				if (!release.Wait(s_testTimeout))
				{
					throw new TimeoutException(
						"The execution was not released.");
				}
			});

		try
		{
			await entered.Task.WaitAsync(s_testTimeout);
			Assert.Throws<InvalidOperationException>(world.Dispose);
		}

		finally
		{
			release.Set();
		}

		await execution.WaitAsync(s_testTimeout);
		Assert.Equal(1, world.Count);
		world.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => world.CreateEntity());
	}

	private static ComponentQuery CreatePositionQuery()
	{
		return new ComponentQuery(
			required: new ComponentSignature(
				ComponentRegistry.Get<Position>()));
	}

	private static World CreateWorldWithPositions(int entityCount)
	{
		World world = new();

		try
		{
			for (int i = 0; i < entityCount; i++)
			{
				Entity entity = world.CreateEntity();
				world.AddComponent(entity, new Position { Value = i });
			}

			return world;
		}

		catch
		{
			world.Dispose();
			throw;
		}
	}
}
