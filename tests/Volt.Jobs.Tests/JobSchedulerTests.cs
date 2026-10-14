
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Volt.Jobs;
using Xunit;

namespace Volt.Jobs.Tests;

public sealed class JobSchedulerTests
{
    [Fact]
    public void Constructor_DefaultParallelism_UsesProcessorCount()
    {
        JobScheduler scheduler = new();

        Assert.Equal(
            Math.Max(1, Environment.ProcessorCount),
            scheduler.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Constructor_ExplicitParallelism_IsPreserved()
    {
        JobScheduler scheduler = new(4);

        Assert.Equal(4, scheduler.MaxDegreeOfParallelism);
    }

    [Fact]
    public void Constructor_NegativeParallelism_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new JobScheduler(-1));
    }

    [Fact]
    public async Task Schedule_ExecutesJob()
    {
        JobScheduler scheduler = new();

        int executions = 0;

        await scheduler.Schedule(() =>
        {
            Interlocked.Increment(ref executions);
        });

        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Schedule_MultipleJobs_AllComplete()
    {
        JobScheduler scheduler = new();

        const int jobCount = 1000;

        int executions = 0;
        Task[] tasks = new Task[jobCount];

        for (int i = 0; i < jobCount; i++)
        {
            tasks[i] = scheduler.Schedule(() =>
            {
                Interlocked.Increment(ref executions);
            });
        }

        await Task.WhenAll(tasks);

        Assert.Equal(jobCount, executions);
    }

    [Fact]
    public void Schedule_NullJob_Throws()
    {
        JobScheduler scheduler = new();

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = scheduler.Schedule(null!);
        });
    }

    [Fact]
    public async Task Schedule_JobException_IsPropagated()
    {
        JobScheduler scheduler = new();

        Task task = scheduler.Schedule(() =>
        {
            throw new InvalidOperationException("Job failed.");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task);
    }

    [Fact]
    public async Task Schedule_PreCanceledToken_PreventsExecution()
    {
        JobScheduler scheduler = new();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        int executions = 0;

        Task task = scheduler.Schedule(() =>
        {
            Interlocked.Increment(ref executions);
        }, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task);

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ScheduleAfter_WaitsForDependency()
    {
        JobScheduler scheduler = new();

        TaskCompletionSource<bool> dependency = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        int executions = 0;

        Task dependent = scheduler.ScheduleAfter(
            dependency.Task,
            () =>
            {
                Interlocked.Increment(ref executions);
            });

        Assert.Equal(0, Volatile.Read(ref executions));

        dependency.SetResult(true);

        await dependent;

        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task ScheduleAfter_FaultedDependency_PreventsExecution()
    {
        JobScheduler scheduler = new();

        Task dependency = Task.FromException(
            new InvalidOperationException("Dependency failed."));

        int executions = 0;

        Task dependent = scheduler.ScheduleAfter(
            dependency,
            () =>
            {
                Interlocked.Increment(ref executions);
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dependent);

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ScheduleAfter_CanceledDependency_PreventsExecution()
    {
        JobScheduler scheduler = new();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Task dependency = Task.FromCanceled(cancellation.Token);

        int executions = 0;

        Task dependent = scheduler.ScheduleAfter(
            dependency,
            () =>
            {
                Interlocked.Increment(ref executions);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dependent);

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ScheduleAfter_CancellationWhileWaiting_PreventsExecution()
    {
        JobScheduler scheduler = new();

        TaskCompletionSource<bool> dependency = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using CancellationTokenSource cancellation = new();

        int executions = 0;

        Task dependent = scheduler.ScheduleAfter(
            dependency.Task,
            () =>
            {
                Interlocked.Increment(ref executions);
            },
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => dependent);

        Assert.Equal(0, executions);
    }

    [Fact]
    public void ScheduleAfter_NullArguments_Throw()
    {
        JobScheduler scheduler = new();

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = scheduler.ScheduleAfter(null!, () => { });
        });

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = scheduler.ScheduleAfter(
                Task.CompletedTask,
                null!);
        });
    }

    [Fact]
    public void RunBatches_CoversEveryItemExactlyOnce()
    {
        JobScheduler scheduler = new(4);

        const int itemCount = 1003;
        const int batchSize = 64;

        int[] visits = new int[itemCount];

        ConcurrentBag<(int Start, int End)> ranges = [];

        scheduler.RunBatches(
            itemCount,
            batchSize,
            (start, end) =>
            {
                ranges.Add((start, end));

                for (int i = start; i < end; i++)
                {
                    Interlocked.Increment(ref visits[i]);
                }
            });

        Assert.Equal(
            (itemCount + batchSize - 1) / batchSize,
            ranges.Count);

        foreach (int visitsForItem in visits)
        {
            Assert.Equal(1, visitsForItem);
        }

        (int Start, int End)[] ordered = ranges
            .OrderBy(range => range.Start)
            .ToArray();

        for (int i = 0; i < ordered.Length; i++)
        {
            int expectedStart = i * batchSize;
            int expectedEnd = Math.Min(
                itemCount,
                expectedStart + batchSize);

            Assert.Equal(expectedStart, ordered[i].Start);
            Assert.Equal(expectedEnd, ordered[i].End);
        }
    }

    [Fact]
    public void RunBatches_SingleWorker_ExecutesInOrder()
    {
        JobScheduler scheduler = new(1);

        List<int> starts = [];
        List<int> ends = [];

        scheduler.RunBatches(
            10,
            4,
            (start, end) =>
            {
                starts.Add(start);
                ends.Add(end);
            });

        Assert.Equal(new[] { 0, 4, 8 }, starts);
        Assert.Equal(new[] { 4, 8, 10 }, ends);
    }

    [Fact]
    public void RunBatches_EmptyRange_DoesNotExecute()
    {
        JobScheduler scheduler = new();

        int executions = 0;

        scheduler.RunBatches(
            0,
            16,
            (start, end) =>
            {
                Interlocked.Increment(ref executions);
            });

        Assert.Equal(0, executions);
    }

    [Fact]
    public void RunBatches_PartialBatch_UsesCorrectBounds()
    {
        JobScheduler scheduler = new();

        int executions = 0;
        int observedStart = -1;
        int observedEnd = -1;

        scheduler.RunBatches(
            3,
            16,
            (start, end) =>
            {
                executions++;
                observedStart = start;
                observedEnd = end;
            });

        Assert.Equal(1, executions);
        Assert.Equal(0, observedStart);
        Assert.Equal(3, observedEnd);
    }

    [Fact]
    public void RunBatches_RejectsInvalidArguments()
    {
        JobScheduler scheduler = new();

        Action<int, int> executeBatch = (_, _) => { };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduler.RunBatches(-1, 1, executeBatch));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduler.RunBatches(1, 0, executeBatch));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scheduler.RunBatches(1, -1, executeBatch));

        Assert.Throws<ArgumentNullException>(() =>
            scheduler.RunBatches(1, 1, null!));
    }

    [Fact]
    public void RunBatches_RespectsMaximumParallelism()
    {
        const int maxWorkers = 2;
        const int batchCount = 256;

        JobScheduler scheduler = new(maxWorkers);

        int activeWorkers = 0;
        int limitViolations = 0;
        int completedBatches = 0;

        scheduler.RunBatches(
            batchCount,
            1,
            (start, end) =>
            {
                int active = Interlocked.Increment(
                    ref activeWorkers);

                try
                {
                    if (active > maxWorkers)
                    {
                        Interlocked.Exchange(
                            ref limitViolations,
                            1);
                    }

                    Thread.SpinWait(20_000);

                    Interlocked.Increment(
                        ref completedBatches);
                }
                finally
                {
                    Interlocked.Decrement(
                        ref activeWorkers);
                }
            });

        Assert.Equal(0, limitViolations);
        Assert.Equal(0, activeWorkers);
        Assert.Equal(batchCount, completedBatches);
    }

    [Fact]
    public void RunBatches_SequentialException_IsPropagated()
    {
        JobScheduler scheduler = new(1);

        Assert.Throws<InvalidOperationException>(() =>
        {
            scheduler.RunBatches(
                10,
                2,
                (start, end) =>
                {
                    throw new InvalidOperationException(
                        "Batch failed.");
                });
        });
    }

    [Fact]
    public void RunBatches_ParallelException_IsPropagated()
    {
        JobScheduler scheduler = new(4);

        AggregateException exception =
            Assert.Throws<AggregateException>(() =>
            {
                scheduler.RunBatches(
                    32,
                    1,
                    (start, end) =>
                    {
                        if (start == 0)
                        {
                            throw new InvalidOperationException(
                                "Parallel batch failed.");
                        }
                    });
            });

        Assert.Contains(
            exception.Flatten().InnerExceptions,
            error => error is InvalidOperationException);
    }

    [Fact]
    public void RunBatches_CancellationBetweenBatches_StopsExecution()
    {
        JobScheduler scheduler = new(1);

        using CancellationTokenSource cancellation = new();

        int executions = 0;

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            scheduler.RunBatches(
                10,
                1,
                (start, end) =>
                {
                    executions++;
                    cancellation.Cancel();
                },
                cancellation.Token);
        });

        Assert.Equal(1, executions);
    }

    [Fact]
    public void RunBatches_PreCanceledToken_PreventsExecution()
    {
        JobScheduler scheduler = new();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        int executions = 0;

        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            scheduler.RunBatches(
                10,
                2,
                (start, end) =>
                {
                    executions++;
                },
                cancellation.Token);
        });

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ScheduleBatches_CoversEveryItemExactlyOnce()
    {
        JobScheduler scheduler = new(4);

        const int itemCount = 1001;
        const int batchSize = 37;

        int[] visits = new int[itemCount];

        await scheduler.ScheduleBatches(
            itemCount,
            batchSize,
            (start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    Interlocked.Increment(ref visits[i]);
                }
            });

        foreach (int visitsForItem in visits)
        {
            Assert.Equal(1, visitsForItem);
        }
    }

    [Fact]
    public async Task ScheduleBatches_EmptyRange_CompletesWithoutExecution()
    {
        JobScheduler scheduler = new();

        int executions = 0;

        Task task = scheduler.ScheduleBatches(
            0,
            16,
            (start, end) =>
            {
                Interlocked.Increment(ref executions);
            });

        await task;

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(0, executions);
    }

    [Fact]
    public void ScheduleBatches_RejectsInvalidArguments()
    {
        JobScheduler scheduler = new();

        Action<int, int> executeBatch = (_, _) => { };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = scheduler.ScheduleBatches(
                -1,
                1,
                executeBatch);
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = scheduler.ScheduleBatches(
                1,
                0,
                executeBatch);
        });

        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = scheduler.ScheduleBatches(
                1,
                1,
                null!);
        });
    }

    [Fact]
    public async Task ScheduleBatches_Exception_IsPropagated()
    {
        JobScheduler scheduler = new(1);

        Task task = scheduler.ScheduleBatches(
            10,
            2,
            (start, end) =>
            {
                throw new InvalidOperationException(
                    "Scheduled batch failed.");
            });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => task);
    }

    [Fact]
    public async Task ScheduleBatches_PreCanceledToken_PreventsExecution()
    {
        JobScheduler scheduler = new();

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        int executions = 0;

        Task task = scheduler.ScheduleBatches(
            100,
            10,
            (start, end) =>
            {
                Interlocked.Increment(ref executions);
            },
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task);

        Assert.Equal(0, executions);
    }

    [Fact]
    public async Task ScheduleBatches_CancellationBetweenBatches_StopsExecution()
    {
        JobScheduler scheduler = new(1);

        using CancellationTokenSource cancellation = new();

        int executions = 0;

        Task task = scheduler.ScheduleBatches(
            10,
            1,
            (start, end) =>
            {
                Interlocked.Increment(ref executions);
                cancellation.Cancel();
            },
            cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task);

        Assert.Equal(1, executions);
    }
}
