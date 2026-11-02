using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class ScrollViewCaptureTests
{
	[Fact]
	public void FocusLoss_StopsScrollThumbDragAfterCaptureIsDropped()
	{
		using UiTestHost host = new(240f, 120f);
		ScrollView view = host.Add(new ScrollView
		{
			Width = UiLength.Points(220f),
			Height = UiLength.Points(100f),
			HorizontalAlignment = UiAlignment.Start,
			VerticalAlignment = UiAlignment.Start,
			Content = new Panel { Height = UiLength.Points(500f) }
		});
		host.Layout();

		Assert.True(view.MaximumScroll > 0f);
		UiRect content = view.GlobalContentBounds;
		UiPoint thumb = new(content.Right - 5f, content.Y + 5f);
		host.Context.PointerDown(UiMouseButton.Left, thumb);

		Assert.True(view.IsPointerCaptured);

		host.Context.WindowFocusLost();
		host.Context.PointerMove(
			new UiPoint(thumb.X, thumb.Y + 1f),
			new UiPoint(0f, 1f));

		Assert.Equal(0f, view.ScrollOffset.Y);
	}
}
