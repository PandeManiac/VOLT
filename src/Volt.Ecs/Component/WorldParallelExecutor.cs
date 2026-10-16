
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Volt.Ecs.Component;
using Volt.Jobs;

namespace Volt.Ecs;

/// <summary>
/// Executes ECS chunk workloads in parallel using a job scheduler.
/// </summary>
/// <remarks>
/// The world's structure remains frozen for the complete
/// execution, including when a job fails or is canceled.
///
/// Each chunk is processed by at most one invocation of the
/// supplied callback during an execution.
///
/// Executions targeting the same world are serialized,
/// including executions using different executor instances.
///
/// Callers must not concurrently access component storage
/// outside this executor in ways that conflict with its jobs.
///
/// Callbacks must finish using their chunk storage before
/// returning. They must not retain spans or references beyond
/// the execution's lifetime.
///
/// Nested execution targeting the same world must not be
/// synchronously awaited from within a chunk callback.
/// </remarks>
public sealed class WorldParallelExecutor
{
    private const int ReusableChunkBufferEntityThreshold = 1_000_000;

    private static readonly ConditionalWeakTable<World, SemaphoreSlim>
        s_executionGates = [];

    private static readonly ConditionalWeakTable<World, List<WorldQueryChunk>>
        s_reusableChunkBuffers = [];

    private readonly JobScheduler _scheduler;

    /// <summary>
    /// Gets the number of consecutive chunks assigned to
    /// each scheduled batch.
    /// </summary>
    public int ChunksPerBatch { get; }

    /// <summary>
    /// Creates a parallel ECS executor.
    /// </summary>
    /// <param name="scheduler">
    /// The general-purpose job scheduler used for execution.
    /// </param>
    /// <param name="chunksPerBatch">
    /// The number of chunks processed in each batch.
    /// Must be greater than zero.
    /// </param>
    public WorldParallelExecutor(
        JobScheduler scheduler,
        int chunksPerBatch = 1)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            chunksPerBatch);

        _scheduler = scheduler;
        ChunksPerBatch = chunksPerBatch;
    }

    /// <summary>
    /// Executes a callback once for each nonempty chunk
    /// matching the supplied component query.
    /// </summary>
    /// <remarks>
    /// Different chunk callbacks may execute concurrently
    /// and in any order.
    ///
    /// The world's structure is frozen before matching chunks
    /// are collected and remains frozen until all scheduled
    /// work completes.
    ///
    /// Calls targeting the same world are serialized to avoid
    /// overlapping executor-managed component access.
    ///
    /// This method does not make arbitrary direct World
    /// component access thread-safe.
    ///
    /// Cancellation is cooperative. A callback already
    /// executing is allowed to finish.
    /// </remarks>
    public Task ExecuteAsync(
        World world,
        ComponentQuery query,
        Action<WorldQueryChunk> executeChunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(executeChunk);

        return ExecuteCoreAsync(
            world,
            query,
            executeChunk,
            cancellationToken);
    }

    private async Task ExecuteCoreAsync(
        World world,
        ComponentQuery query,
        Action<WorldQueryChunk> executeChunk,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim executionGate = s_executionGates.GetValue(
            world,
            static _ => new SemaphoreSlim(1, 1));

        await executionGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            using World.StructuralFreezeScope freeze =
                world.FreezeStructure();

            bool reuseChunks =
                world.Count >= ReusableChunkBufferEntityThreshold;

            List<WorldQueryChunk> chunks = reuseChunks
                ? s_reusableChunkBuffers.GetValue(
                    world,
                    static _ => [])
                : [];

            try
            {
                foreach (WorldQueryChunk chunk in world.Query(query))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    chunks.Add(chunk);
                }

                await _scheduler.ScheduleBatches(
                    chunks.Count,
                    ChunksPerBatch,
                    (start, end) =>
                    {
                        for (int i = start; i < end; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            executeChunk(chunks[i]);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (reuseChunks)
                {
                    chunks.Clear();
                }
            }
        }

        finally
        {
            executionGate.Release();
        }
    }
}
