using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Input;

public sealed class InputRoutingTests
{
	[Fact]
	public void PointerPress_FocusesTextFieldAndDeliversUnicodeText()
	{
		using UiTestHost host = new(400f, 200f);
		StackPanel stack = host.Add(new StackPanel());
		TextField field = stack.Add(new TextField());

		host.Layout();
		Assert.True(host.Context.PointerDown(
			UiMouseButton.Left,
			field.GlobalContentBounds.Center));
		Assert.Same(field, host.Context.FocusedWidget);

		Assert.True(host.Context.TextInput("hé😀"));
		Assert.Equal("hé😀", field.Text);
	}

	[Fact]
	public void PointerClick_InvokesButtonOnce()
	{
		using UiTestHost host = new(300f, 100f);
		Button button = host.Add(new Button("Press"));
		int clicks = 0;
		button.Clicked += _ => clicks++;

		host.Layout();
		UiPoint center = button.GlobalVisualBounds.Center;

		host.Context.PointerDown(UiMouseButton.Left, center);
		host.Context.PointerUp(UiMouseButton.Left, center);

		Assert.Equal(1, clicks);
	}

	[Fact]
	public void DisabledWidget_ConsumesPointerButDoesNotActivate()
	{
		using UiTestHost host = new(300f, 100f);
		Button button = host.Add(
			new Button("Disabled")
			{
				IsEnabled = false
			});
		int clicks = 0;
		button.Clicked += _ => clicks++;

		host.Layout();
		UiPoint center = button.GlobalVisualBounds.Center;

		Assert.True(host.Context.PointerDown(UiMouseButton.Left, center));
		host.Context.PointerUp(UiMouseButton.Left, center);

		Assert.Equal(0, clicks);
		Assert.Null(host.Context.FocusedWidget);
	}

	[Fact]
	public void RemovingWidgetDuringClick_ClearsFocusAndCapture()
	{
		using UiTestHost host = new(300f, 100f);
		Button button = host.Add(new Button("Remove"));
		button.Clicked += clicked => clicked.RemoveFromParent();

		host.Layout();
		host.Context.PointerDown(
			UiMouseButton.Left,
			button.GlobalVisualBounds.Center);

		Assert.True(button.HasFocus);

		host.Context.PointerUp(
			UiMouseButton.Left,
			button.GlobalVisualBounds.Center);

		Assert.False(button.IsAttached);
		Assert.Null(host.Context.FocusedWidget);
		Assert.Null(host.Context.HoveredWidget);

		// The context remains usable after the callback removed its
		// own target.
		host.Layout();
		Assert.True(host.Context.Draw().Count >= 0);
	}

	[Fact]
	public void SliderCapture_ContinuesAfterPointerLeavesBounds()
	{
		using UiTestHost host = new(300f, 100f);
		Slider slider = host.Add(
			new Slider
			{
				Minimum = 0f,
				Maximum = 1f,
				Width = UiLength.Points(200f),
				HorizontalAlignment = UiAlignment.Start
			});

		host.Layout();
		UiPoint start = new(
			slider.GlobalContentBounds.X + 10f,
			slider.GlobalContentBounds.Center.Y);

		host.Context.PointerDown(UiMouseButton.Left, start);
		Assert.True(slider.IsPointerCaptured);

		host.Context.PointerMove(
			new UiPoint(slider.GlobalContentBounds.Right + 200f, start.Y),
			new UiPoint(150f, 0f));

		Assert.Equal(1f, slider.Value, 3);
		Assert.True(slider.IsPointerCaptured);

		host.Context.PointerUp(
			UiMouseButton.Left,
			new UiPoint(slider.GlobalContentBounds.Right + 200f, start.Y));
		Assert.False(slider.IsPointerCaptured);
	}

	[Fact]
	public void KeyShortcut_RunsWhenFocusedWidgetDoesNotConsumeIt()
	{
		using UiTestHost host = new(300f, 100f);
		TextField field = host.Add(new TextField());
		bool saved = false;

		host.Context.Shortcuts.Add(
			new UiShortcut(
				UiKey.S,
				UiKeyModifiers.Control,
				() => saved = true));

		host.Layout();
		field.Focus();

		Assert.True(host.Context.KeyDown(
			UiKey.S,
			UiKeyModifiers.Control));
		Assert.True(saved);
	}

	[Fact]
	public void Tab_MovesFocusThroughFocusableWidgets()
	{
		using UiTestHost host = new(300f, 200f);
		StackPanel stack = host.Add(new StackPanel());

		TextField first = stack.Add(new TextField());
		TextField second = stack.Add(new TextField());

		host.Layout();
		first.Focus();

		Assert.True(host.Context.KeyDown(UiKey.Tab));
		Assert.Same(second, host.Context.FocusedWidget);

		host.Context.KeyDown(
			UiKey.Tab,
			UiKeyModifiers.Shift);
		Assert.Same(first, host.Context.FocusedWidget);
	}
}
