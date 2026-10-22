
using Volt.Platform.Windows;

namespace Volt.Platform.Tests.Windows;

public sealed class WindowSizeTests
{
	[Fact]
	public void Constructor_StoresDimensions()
	{
		WindowSize size = new(1280, 720);
		Assert.Equal(1280, size.Width);
		Assert.Equal(720, size.Height);
	}

	[Fact]
	public void PositiveDimensions_AreRecognized()
	{
		Assert.True(new WindowSize(1, 1).IsPositive);
	}

	[Theory]
	[InlineData(0, 720)]
	[InlineData(1280, 0)]
	[InlineData(-1, 720)]
	[InlineData(1280, -1)]
	public void NonpositiveDimensions_AreNotPositive(
		int width,
		int height)
	{
		Assert.False(
			new WindowSize(width, height).IsPositive);
	}

	[Fact]
	public void EqualSizes_CompareEqual()
	{
		Assert.Equal(
			new WindowSize(1280, 720),
			new WindowSize(1280, 720));
	}

	[Fact]
	public void DifferentSizes_CompareUnequal()
	{
		Assert.NotEqual(
			new WindowSize(1280, 720),
			new WindowSize(1920, 1080));
	}
}
