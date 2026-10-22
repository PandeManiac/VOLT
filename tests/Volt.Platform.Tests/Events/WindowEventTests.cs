
using Volt.Platform.Events;
using Volt.Platform.Windows;

namespace Volt.Platform.Tests.Events;

public sealed class WindowEventTests
{
	[Fact]
	public void DefaultEvent_IsUnknown()
	{
		WindowEvent windowEvent = default;

		Assert.Equal(
			WindowEventType.Unknown,
			windowEvent.Type);
	}

	[Fact]
	public void CloseRequest_DoesNotRequireSize()
	{
		WindowEvent windowEvent = new(
			WindowEventType.CloseRequested);

		Assert.Equal(
			WindowEventType.CloseRequested,
			windowEvent.Type);

		Assert.Equal(default, windowEvent.Size);
	}

	[Fact]
	public void Resize_CarriesLogicalDimensions()
	{
		WindowEvent windowEvent = new(
			WindowEventType.Resized,
			new WindowSize(900, 700));

		Assert.Equal(
			new WindowSize(900, 700),
			windowEvent.Size);
	}

	[Fact]
	public void PixelSizeChange_CarriesFramebufferDimensions()
	{
		WindowEvent windowEvent = new(
			WindowEventType.PixelSizeChanged,
			new WindowSize(1800, 1400));

		Assert.Equal(
			WindowEventType.PixelSizeChanged,
			windowEvent.Type);

		Assert.Equal(
			new WindowSize(1800, 1400),
			windowEvent.Size);
	}

	[Fact]
	public void EqualEvents_CompareEqual()
	{
		WindowEvent first = new(
			WindowEventType.Resized,
			new WindowSize(800, 600));

		WindowEvent second = new(
			WindowEventType.Resized,
			new WindowSize(800, 600));

		Assert.Equal(first, second);
	}

	[Fact]
	public void DifferentEvents_CompareUnequal()
	{
		WindowSize size = new(800, 600);

		Assert.NotEqual(
			new WindowEvent(WindowEventType.Resized, size),
			new WindowEvent(WindowEventType.PixelSizeChanged, size));
	}
}
