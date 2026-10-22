using BenchmarkDotNet.Attributes;
using Volt.Jobs;

namespace Volt.Benchmarks;

/// <summary>
/// Focused scheduler cases for MediumRun confirmation.
/// </summary>
[MemoryDiagnoser]
public class JobSchedulerFocusedBenchmarks
{
	private JobScheduler _oneWorker = null!;
	private JobScheduler _eightWorkers = null!;
	private static readonly Action<int, int> s_empty = static (_, _) => { };
	private static readonly Action<int, int> s_cpuWork = CpuWork;
	private static readonly Action s_emptyJob = static () => { };

	[GlobalSetup]
	public void Setup()
	{
		_oneWorker = new JobScheduler(1);
		_eightWorkers = new JobScheduler(8);
	}

	[Benchmark]
	public Task SingleBatchOneWorker() =>
		_oneWorker.ScheduleBatches(1, 1, s_empty);

	[Benchmark]
	public Task ScheduleOneJob() =>
		_oneWorker.Schedule(s_emptyJob);

	[Benchmark]
	public Task TaskRunOneJob() =>
		Task.Run(s_emptyJob);

	[Benchmark]
	public Task ManyBatchesOneWorker() =>
		_oneWorker.ScheduleBatches(128, 1, s_empty);

	[Benchmark]
	public Task ManyBatchesEightWorkers() =>
		_eightWorkers.ScheduleBatches(128, 1, s_empty);

	[Benchmark]
	public Task CpuWorkEightWorkers() =>
		_eightWorkers.ScheduleBatches(128, 1, s_cpuWork);

	private static void CpuWork(int start, int end)
	{
		Thread.SpinWait(128);
	}
}
