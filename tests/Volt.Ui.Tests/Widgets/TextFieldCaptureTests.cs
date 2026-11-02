using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class TextFieldCaptureTests
{
	[Fact]
	public void FocusLoss_StopsPointerFromChangingTextSelection()
	{
		using UiTestHost host = new(260f, 100f);
		TextField field = host.Add(new TextField("abcdef")
		{
			Width = UiLength.Points(220f),
			Height = UiLength.Points(32f),
			HorizontalAlignment = UiAlignment.Start,
			VerticalAlignment = UiAlignment.Start
		});
		host.Layout();

		UiRect bounds = field.GlobalContentBounds;
		host.Context.PointerDown(
			UiMouseButton.Left,
			new UiPoint(bounds.X + 1f, bounds.Center.Y));
		Assert.True(field.IsPointerCaptured);
		field.SetSelection(0, 0);

		host.Context.WindowFocusLost();
		host.Context.PointerMove(
			new UiPoint(bounds.Right - 1f, bounds.Center.Y),
			new UiPoint(bounds.Width - 2f, 0f));

		Assert.Equal(0, field.SelectionStart);
		Assert.Equal(0, field.SelectionEnd);
	}
}
