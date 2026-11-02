using Volt.Ui.Docking;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Docking;

public sealed class DockTabsCaptureTests
{
	[Fact]
	public void FocusLoss_DoesNotCompleteStaleTabDrag()
	{
		using UiTestHost host = new(320f, 180f);
		DockTabsNode node = new(["one", "two"]);
		int completed = 0;
		DockTabsView view = host.Add(new DockTabsView(
			node,
			[
				new KeyValuePair<string, Widget>("one", new Panel()),
				new KeyValuePair<string, Widget>("two", new Panel())
			],
			id => id,
			_ => { },
			_ => { },
			(_, _, _) => completed++));
		host.Layout();
		host.Context.Draw();

		UiRect bounds = view.GlobalVisualBounds;
		UiPoint firstTab = new(bounds.X + 12f, bounds.Y + 8f);
		UiPoint secondTab = new(bounds.X + 90f, bounds.Y + 8f);
		host.Context.PointerDown(UiMouseButton.Left, firstTab);
		Assert.True(view.IsPointerCaptured);
		host.Context.PointerMove(
			secondTab,
			new UiPoint(secondTab.X - firstTab.X, 0f));

		host.Context.WindowFocusLost();
		host.Context.PointerUp(UiMouseButton.Left, secondTab);

		Assert.Equal(0, completed);
	}
}
