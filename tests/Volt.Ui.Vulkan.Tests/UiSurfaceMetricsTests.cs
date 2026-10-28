using Volt.Platform.Windows;
using Volt.Ui.Vulkan;

namespace Volt.Ui.Vulkan.Tests;

/// <summary>
/// Covers the conversion between window logical units and physical
/// pixels that layout, the swapchain, and startup diagnostics share.
/// </summary>
public sealed class UiSurfaceMetricsTests
{
	[Fact]
	public void StandardDensity_MapsLogicalUnitsOneToOne()
	{
		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			new WindowSize(1600, 960),
			new WindowSize(1600, 960));

		Assert.Equal(1600, metrics.LogicalWidth);
		Assert.Equal(960, metrics.LogicalHeight);
		Assert.Equal(1600, metrics.PhysicalWidth);
		Assert.Equal(960, metrics.PhysicalHeight);
		Assert.Equal(1f, metrics.DisplayScale);
		Assert.True(metrics.HasPhysicalSurface);
		Assert.Equal(new UiSize(1600f, 960f), metrics.LogicalSize);
	}

	[Theory]
	[InlineData(800, 480, 1600, 960, 2f)]
	[InlineData(1280, 720, 1920, 1080, 1.5f)]
	[InlineData(1600, 960, 1600, 960, 1f)]
	public void HighDensity_ReportsLogicalAndPhysicalGeometry(
		int logicalWidth,
		int logicalHeight,
		int physicalWidth,
		int physicalHeight,
		float expectedScale)
	{
		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			new WindowSize(logicalWidth, logicalHeight),
			new WindowSize(physicalWidth, physicalHeight));

		Assert.Equal(logicalWidth, metrics.LogicalWidth);
		Assert.Equal(logicalHeight, metrics.LogicalHeight);
		Assert.Equal(physicalWidth, metrics.PhysicalWidth);
		Assert.Equal(physicalHeight, metrics.PhysicalHeight);
		Assert.Equal(expectedScale, metrics.DisplayScale, 3);
		Assert.True(metrics.HasPhysicalSurface);
	}

	[Fact]
	public void MinimizedWindow_HasNoPhysicalSurface_ButKeepsLogicalLayout()
	{
		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			new WindowSize(1600, 960),
			new WindowSize(0, 0));

		Assert.False(metrics.HasPhysicalSurface);
		Assert.Equal(1600, metrics.LogicalWidth);
		Assert.Equal(960, metrics.LogicalHeight);
		Assert.True(metrics.DisplayScale > 0f);
	}

	[Fact]
	public void MissingLogicalSize_FallsBackToPhysicalPixels()
	{
		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			default,
			new WindowSize(1280, 720));

		Assert.Equal(1280, metrics.LogicalWidth);
		Assert.Equal(720, metrics.LogicalHeight);
		Assert.Equal(1f, metrics.DisplayScale);
	}

	[Fact]
	public void NegativeSizes_AreClamped()
	{
		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			new WindowSize(-10, -20),
			new WindowSize(-30, -40));

		Assert.Equal(0, metrics.PhysicalWidth);
		Assert.Equal(0, metrics.PhysicalHeight);
		Assert.True(metrics.LogicalWidth >= 1);
		Assert.True(metrics.LogicalHeight >= 1);
		Assert.False(metrics.HasPhysicalSurface);
	}
}
