namespace Volt.Editor.Tests;

/// <summary>
/// Covers the profiler's frame window: the statistics it reports, how
/// it wraps, and the shell feeding it from the real frame loop.
/// </summary>
public sealed class FrameTimelineTests
{
	[Fact]
	public void Record_ReportsAverageMinimumMaximumAndFps()
	{
		FrameTimeline timeline = new();

		timeline.Record(0.016);
		timeline.Record(0.020);
		timeline.Record(0.012);

		Assert.Equal(3, timeline.Count);
		Assert.Equal(16.0, timeline.AverageMilliseconds, 6);
		Assert.Equal(12.0, timeline.MinimumMilliseconds, 6);
		Assert.Equal(20.0, timeline.MaximumMilliseconds, 6);
		Assert.Equal(12.0, timeline.LatestMilliseconds, 6);
		Assert.Equal(62.5, timeline.FramesPerSecond, 6);
	}

	[Fact]
	public void Record_WrapsAndKeepsOnlyTheRecentWindowInOrder()
	{
		FrameTimeline timeline = new(capacity: 3);

		for (int index = 1; index <= 5; index++)
		{
			timeline.Record(index / 1000.0);
		}

		Assert.Equal(3, timeline.Count);

		double[] samples = new double[8];

		Assert.Equal(3, timeline.CopySamples(samples));

		// The window holds the last three intervals, oldest first.
		Assert.Equal(3.0, samples[0], 6);
		Assert.Equal(4.0, samples[1], 6);
		Assert.Equal(5.0, samples[2], 6);

		Assert.Equal(4.0, timeline.AverageMilliseconds, 6);
		Assert.Equal(3.0, timeline.MinimumMilliseconds, 6);
		Assert.Equal(5.0, timeline.MaximumMilliseconds, 6);
	}

	[Fact]
	public void Record_IgnoresIntervalsThatDescribeNoFrame()
	{
		FrameTimeline timeline = new();

		timeline.Record(0.0);
		timeline.Record(-0.016);
		timeline.Record(double.NaN);
		timeline.Record(double.PositiveInfinity);

		Assert.True(timeline.IsEmpty);
		Assert.Equal(0.0, timeline.AverageMilliseconds);
		Assert.Equal(0.0, timeline.FramesPerSecond);

		timeline.Record(0.010);

		Assert.Equal(1, timeline.Count);
		Assert.Equal(100.0, timeline.FramesPerSecond, 6);
	}

	[Fact]
	public void CopySamples_RespectsAShorterDestination()
	{
		FrameTimeline timeline = new(capacity: 4);

		timeline.Record(0.001);
		timeline.Record(0.002);
		timeline.Record(0.003);

		double[] samples = new double[2];

		Assert.Equal(2, timeline.CopySamples(samples));
		Assert.Equal(2.0, samples[0], 6);
		Assert.Equal(3.0, samples[1], 6);
	}

	[Fact]
	public void Clear_EmptiesTheWindow()
	{
		FrameTimeline timeline = new(capacity: 2);

		timeline.Record(0.010);
		timeline.Record(0.010);
		timeline.Clear();

		Assert.True(timeline.IsEmpty);
		Assert.Equal(0, timeline.CopySamples(new double[2]));
	}

	[Fact]
	public void Shell_FeedsTheTimelineFromItsFrameLoop()
	{
		using EditorSession session = new();
		using EditorShell shell = new(session);

		shell.Update(0.016);
		shell.Update(0.016);
		shell.Update(0.016);

		Assert.Equal(3, session.Frames.Count);
		Assert.Equal(16.0, session.Frames.AverageMilliseconds, 3);
		Assert.Equal(16.0, session.Frames.LatestMilliseconds, 3);
	}
}
