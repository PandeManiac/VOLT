using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class TreeViewCaptureTests
{
	[Fact]
	public void FocusLoss_DoesNotCompleteStaleReparentDrag()
	{
		using UiTestHost host = new(320f, 180f);
		TreeView tree = host.Add(new TreeView
		{
			Width = UiLength.Points(240f),
			Height = UiLength.Points(150f),
			HorizontalAlignment = UiAlignment.Start,
			VerticalAlignment = UiAlignment.Start
		});
		TreeViewItem root = new("root", "Root");
		TreeViewItem target = new("target", "Target");
		tree.AddRoot(root);
		tree.AddRoot(target);
		int drops = 0;
		tree.ItemDropped += (_, _, _) => drops++;
		host.Layout();

		UiRect bounds = tree.GlobalContentBounds;
		UiPoint rootRow = new(bounds.X + 80f, bounds.Y + 12f);
		UiPoint targetRow = new(bounds.X + 80f, bounds.Y + 36f);
		host.Context.PointerDown(UiMouseButton.Left, rootRow);
		Assert.True(tree.IsPointerCaptured);
		host.Context.PointerMove(targetRow, new UiPoint(0f, 24f));

		host.Context.WindowFocusLost();
		host.Context.PointerUp(UiMouseButton.Left, targetRow);

		Assert.Equal(0, drops);
		Assert.Null(root.Parent);
		Assert.Null(target.Parent);
	}
}
