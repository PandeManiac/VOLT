using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests;

/// <summary>
/// A headless UI host for layout and input tests.
/// </summary>
internal sealed class UiTestHost : IDisposable
{
	public UiTestHost(float width = 640f, float height = 480f)
	{
		Context = new UiContext();
		Width = width;
		Height = height;
		Context.BeginFrame(
			0.0,
			new UiSize(width, height),
			UiPoint.Zero);
	}

	public UiContext Context { get; }

	public float Width { get; }

	public float Height { get; }

	public T Add<T>(T widget)
		where T : Widget
	{
		Context.Add(widget);
		return widget;
	}

	public void Layout(
		float? width = null,
		float? height = null,
		double time = 0.0)
	{
		Context.BeginFrame(
			time,
			new UiSize(width ?? Width, height ?? Height),
			Context.PointerPosition);
	}

	public void Dispose()
	{
		Context.Dispose();
	}
}
