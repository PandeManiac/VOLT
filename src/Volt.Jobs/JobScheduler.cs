
using System.Threading;
using System.Threading.Tasks;

namespace Volt.Jobs;

/// <summary>
/// Schedules jobs using the .NET ThreadPool and executes
/// bounded batches of work in parallel.
/// </summary>
/// <remarks>
/// This scheduler does not own worker threads.
///
/// MaxDegreeOfParallelism limits workers within an individual
/// batch execution, not the total number of jobs submitted
/// independently through Schedule.
///
/// Callers must await or otherwise complete scheduled jobs
/// before releasing resources accessed by those jobs.
/// </remarks>
public sealed class JobScheduler
{
    /// <summary>
    /// Gets the maximum number of workers permitted within
    /// an individual parallel batch execution.
    /// </summary>
    public int MaxDegreeOfParallelism { get; }

    /// <summary>
    /// Creates a scheduler with a configurable parallelism limit.
    /// Zero selects the processor count reported by the runtime.
    /// </summary>
    public JobScheduler(int maxDegreeOfParallelism = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            maxDegreeOfParallelism);

        MaxDegreeOfParallelism = maxDegreeOfParallelism == 0
            ? Math.Max(1, Environment.ProcessorCount)
            : maxDegreeOfParallelism;
    }

    /// <summary>
    /// Schedules an individual job on the ThreadPool.
    /// </summary>
    /// <remarks>
    /// Cancellation prevents execution if the job has not
    /// started. Running jobs must cooperate with cancellation
    /// themselves.
    /// </remarks>
    public Task Schedule(
        Action job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            job();
        }, cancellationToken);
    }

    /// <summary>
    /// Schedules a job after another task completes successfully.
    /// </summary>
    /// <remarks>
    /// A faulted or canceled dependency prevents the dependent
    /// job from executing.
    /// </remarks>
    public Task ScheduleAfter(
        Task dependency,
        Action job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(job);

        return ScheduleAfterCore(
            dependency,
            job,
            cancellationToken);
    }

    /// <summary>
    /// Schedules a batched parallel operation on the ThreadPool.
    /// Each batch receives an exclusive index range [start, end).
    /// </summary>
    public Task ScheduleBatches(
        int itemCount,
        int batchSize,
        Action<int, int> executeBatch,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchArguments(
            itemCount,
            batchSize,
            executeBatch);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        if (itemCount == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            RunBatches(
                itemCount,
                batchSize,
                executeBatch,
                cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Executes batches in parallel and returns after all
    /// batches have finished.
    /// </summary>
    /// <remarks>
    /// Batches cover [0, itemCount) without overlap.
    /// The final batch may contain fewer than batchSize items.
    ///
    /// Exceptions from parallel batches are propagated to
    /// the caller. Cancellation cannot interrupt a batch
    /// already executing user code.
    /// </remarks>
    public void RunBatches(
        int itemCount,
        int batchSize,
        Action<int, int> executeBatch,
        CancellationToken cancellationToken = default)
    {
        ValidateBatchArguments(
            itemCount,
            batchSize,
            executeBatch);

        cancellationToken.ThrowIfCancellationRequested();

        if (itemCount == 0)
        {
            return;
        }

        int batchCount = 1 + (itemCount - 1) / batchSize;

        if (MaxDegreeOfParallelism == 1 || batchCount == 1)
        {
            for (int i = 0; i < batchCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ExecuteBatch(
                    i,
                    itemCount,
                    batchSize,
                    executeBatch);
            }

            return;
        }

        ParallelOptions options = new()
        {
            MaxDegreeOfParallelism = MaxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        Parallel.For(0, batchCount, options, batchIndex =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            ExecuteBatch(
                batchIndex,
                itemCount,
                batchSize,
                executeBatch);
        });
    }

    private async Task ScheduleAfterCore(
        Task dependency,
        Action job,
        CancellationToken cancellationToken)
    {
        await dependency.WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        await Schedule(job, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ExecuteBatch(
        int batchIndex,
        int itemCount,
        int batchSize,
        Action<int, int> executeBatch)
    {
        int start = checked(batchIndex * batchSize);
        int count = Math.Min(batchSize, itemCount - start);
        int end = start + count;

        executeBatch(start, end);
    }

    private static void ValidateBatchArguments(
        int itemCount,
        int batchSize,
        Action<int, int> executeBatch)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentNullException.ThrowIfNull(executeBatch);
    }
}
