using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Volt.Jobs;

namespace Volt.Benchmarks;

/// <summary>
/// Separates callback cost, synchronous batch dispatch, and
/// asynchronous scheduling overhead for small ECS-style jobs.
/// </summary>
[MemoryDiagnoser]
public class JobSchedulerBenchmarks
{
    private JobScheduler _scheduler = null!;
    private Action<int, int> _executeBatch = null!;
    private Action _emptyJob = null!;

    [Params(1, 8)]
    public int WorkerCount { get; set; }

    [Params(1, 8, 128)]
    public int BatchCount { get; set; }

    [Params(0, 128)]
    public int WorkIterations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scheduler = new JobScheduler(WorkerCount);
        _executeBatch = ExecuteBatch;
        _emptyJob = EmptyJob;
    }

    /// <summary>
    /// Reference cost of invoking the same callbacks sequentially.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void DirectSequential()
    {
        for (int i = 0; i < BatchCount; i++)
        {
            _executeBatch(i, i + 1);
        }
    }

    /// <summary>
    /// Synchronous scheduling and dispatch, without Task.Run.
    /// </summary>
    [Benchmark]
    public void RunBatches()
    {
        _scheduler.RunBatches(
            BatchCount,
            batchSize: 1,
            executeBatch: _executeBatch);
    }

    /// <summary>
    /// The asynchronous batch path used by WorldParallelExecutor.
    /// BenchmarkDotNet waits for the returned task to finish.
    /// </summary>
    [Benchmark]
    public Task ScheduleBatches()
    {
        return _scheduler.ScheduleBatches(
            BatchCount,
            batchSize: 1,
            executeBatch: _executeBatch);
    }

    /// <summary>
    /// Lower-bound reference for one ThreadPool dispatch.
    /// This intentionally does not process BatchCount items.
    /// </summary>
    [Benchmark]
    public Task TaskRunOnly()
    {
        return Task.Run(_emptyJob);
    }

    private void ExecuteBatch(int start, int end)
    {
        if (WorkIterations != 0)
        {
            Thread.SpinWait(WorkIterations);
        }
    }

    private static void EmptyJob()
    {
    }
}
