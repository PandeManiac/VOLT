using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeTitleTests
{
	[Fact]
	public void FormatTargetRangeTitle_ReportsTheInitialScore()
	{
		Assert.Equal(
			"VOLT Target Range - 0/500 - 5 targets remaining",
			Program.FormatTargetRangeTitle(
				score: 0,
				remaining: TargetRangeScene.TargetCount,
				complete: false));
	}

	[Fact]
	public void FormatTargetRangeTitle_ReportsProgress()
	{
		Assert.Equal(
			"VOLT Target Range - 200/500 - 3 targets remaining",
			Program.FormatTargetRangeTitle(
				score: 200,
				remaining: 3,
				complete: false));
	}

	[Fact]
	public void FormatTargetRangeTitle_UsesTheSingularForTheLastTarget()
	{
		Assert.Equal(
			"VOLT Target Range - 400/500 - 1 target remaining",
			Program.FormatTargetRangeTitle(
				score: 400,
				remaining: 1,
				complete: false));
	}

	[Fact]
	public void FormatTargetRangeTitle_ReportsCompletion()
	{
		string title = Program.FormatTargetRangeTitle(
			score: TargetRangeScene.MaximumScore,
			remaining: 0,
			complete: true);

		Assert.Contains("COMPLETE 500/500", title);
		Assert.Contains("press R", title);
	}
}
