using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class SplitterCaptureTests
{
	[Fact]
	public void FocusLoss_StopsResizingAfterCaptureIsDropped()
	{
		using UiTestHost host = new(240f, 120f);
		Splitter splitter = host.Add(new Splitter(UiOrientation.Vertical));
		int drags = 0;
		splitter.Dragged += (_, _) => drags++;
		host.Layout();

		UiPoint start = splitter.GlobalVisualBounds.Center;
		host.Context.PointerDown(UiMouseButton.Left, start);

		Assert.True(splitter.IsPointerCaptured);

		host.Context.WindowFocusLost();
		Assert.False(splitter.IsPointerCaptured);

		host.Context.PointerMove(
			new UiPoint(start.X + 1f, start.Y),
			new UiPoint(1f, 0f));

		Assert.Equal(0, drags);
	}
}
