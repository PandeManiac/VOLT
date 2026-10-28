namespace Volt.Editor.Tests;

/// <summary>
/// Covers the profiler's allocation window and the frame loop feeding
/// it, including the injectable counter that makes the accounting
/// deterministic.
/// </summary>
public sealed class MemoryTimelineTests
{
	[Fact]
	public void Record_ReportsLastAverageTotalAndPeak()
	{
		MemoryTimeline timeline = new();

		timeline.Record(1_000);
		timeline.Record(3_000);
		timeline.Record(2_000);

		Assert.Equal(3, timeline.Count);
		Assert.Equal(2_000, timeline.LastBytes);
		Assert.Equal(6_000, timeline.TotalBytesInWindow);
		Assert.Equal(2_000.0, timeline.AverageBytesPerFrame, 6);
		Assert.Equal(3_000, timeline.PeakBytesInWindow);
	}

	[Fact]
	public void Record_WrapsAndDropsEvictedBytes()
	{
		MemoryTimeline timeline = new(capacity: 3);

		timeline.Record(100);
		timeline.Record(200);
		timeline.Record(300);
		timeline.Record(400);

		Assert.Equal(3, timeline.Count);
		Assert.Equal(900, timeline.TotalBytesInWindow);
		Assert.Equal(300.0, timeline.AverageBytesPerFrame, 6);
		Assert.Equal(400, timeline.LastBytes);
		Assert.Equal(400, timeline.PeakBytesInWindow);
	}

	[Fact]
	public void CopySamples_ReturnsTheWindowOldestFirst()
	{
		MemoryTimeline timeline = new(capacity: 3);

		timeline.Record(100);
		timeline.Record(200);
		timeline.Record(300);
		timeline.Record(400);

		double[] samples = new double[4];

		Assert.Equal(3, timeline.CopySamples(samples));
		Assert.Equal(200.0, samples[0], 6);
		Assert.Equal(300.0, samples[1], 6);
		Assert.Equal(400.0, samples[2], 6);
	}

	[Fact]
	public void Record_RejectsNegativeAllocations()
	{
		MemoryTimeline timeline = new();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => timeline.Record(-1));
	}

	[Fact]
	public void Clear_EmptiesTheWindow()
	{
		MemoryTimeline timeline = new(capacity: 2);

		timeline.Record(512);
		timeline.Record(512);
		timeline.Clear();

		Assert.True(timeline.IsEmpty);
		Assert.Equal(0, timeline.TotalBytesInWindow);
		Assert.Equal(0.0, timeline.AverageBytesPerFrame);
	}

	[Fact]
	public void Session_ReportsPerFrameAllocationDeltas()
	{
		long allocated = 5_000;

		using EditorSession session = new(() => allocated);

		allocated = 6_000;
		session.RecordFrame(0.016);

		allocated = 9_000;
		session.RecordFrame(0.016);

		allocated = 9_000;
		session.RecordFrame(0.016);

		Assert.Equal(3, session.Allocations.Count);

		// The last frame allocated nothing; the 3 KB peak came from the
		// frame before it and still counts in the window total.
		Assert.Equal(0, session.Allocations.LastBytes);
		Assert.Equal(4_000, session.Allocations.TotalBytesInWindow);
		Assert.Equal(4_000.0 / 3.0, session.Allocations.AverageBytesPerFrame, 6);
		Assert.Equal(3_000, session.Allocations.PeakBytesInWindow);
	}

	[Fact]
	public void Shell_FeedsAllocationSamplesThroughTheSession()
	{
		long allocated = 0;

		using EditorSession session = new(() => allocated += 100);
		using EditorShell shell = new(session);

		shell.Update(0.016);
		shell.Update(0.016);

		// The constructor establishes the baseline, so each frame's
		// delta is one hundred bytes.
		Assert.Equal(2, session.Allocations.Count);
		Assert.Equal(200, session.Allocations.TotalBytesInWindow);
		Assert.Equal(100, session.Allocations.LastBytes);
	}
}
