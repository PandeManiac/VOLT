using Volt.Ui.Docking;
using Volt.Ui.Layout;

namespace Volt.Ui.Tests.Docking;

public sealed class DockLayoutTests
{
	[Fact]
	public void DefaultEditorLayout_HasExpectedPanelsAndActiveTabs()
	{
		DockLayout layout = DockLayout.CreateEditorDefault();

		Assert.Equal(7, layout.Panels.Count);
		Assert.True(layout.IsOpen("hierarchy"));
		Assert.True(layout.IsOpen("scene"));
		Assert.True(layout.IsOpen("game"));
		Assert.True(layout.IsOpen("inspector"));
		Assert.True(layout.IsOpen("project"));
		Assert.True(layout.IsOpen("console"));
		Assert.True(layout.IsOpen("profiler"));

		DockTabsNode center = Assert.IsType<DockTabsNode>(
			layout.FindTabs("scene"));

		Assert.Equal("scene", center.ActivePanelId);
		Assert.Equal(["scene", "game"], center.PanelIds);
	}

	[Fact]
	public void Serialize_RoundTripsTreeRatiosAndActivePanel()
	{
		DockLayout layout = DockLayout.CreateEditorDefault();
		DockSplitNode root = Assert.IsType<DockSplitNode>(layout.Root);
		layout.SetRatio(root, 0.63f);
		layout.ActivatePanel("game");

		string json = layout.Serialize();
		DockLayout restored = DockLayout.Deserialize(json);

		DockSplitNode restoredRoot =
			Assert.IsType<DockSplitNode>(restored.Root);

		Assert.Equal(0.63f, restoredRoot.Ratio, 3);
		Assert.Equal("game", restored.FindTabs("game")?.ActivePanelId);
		Assert.Equal(
			layout.Panels.Count,
			restored.Panels.Count);

		foreach (DockPanel panel in layout.Panels.Values)
		{
			Assert.True(restored.Panels.ContainsKey(panel.Id));
			Assert.Equal(panel.Title, restored.Panels[panel.Id].Title);
			Assert.Equal(panel.IsOpen, restored.Panels[panel.Id].IsOpen);
		}
	}

	[Fact]
	public void ClosePanel_CollapsesEmptySplitAndCanReopen()
	{
		DockPanel left = new("left", "Left");
		DockPanel right = new("right", "Right");
		DockLayout layout = new(
			new DockSplitNode(
				UiOrientation.Horizontal,
				new DockTabsNode(["left"]),
				new DockTabsNode(["right"])),
			[left, right]);

		Assert.True(layout.ClosePanel("left"));
		Assert.False(left.IsOpen);
		Assert.IsType<DockTabsNode>(layout.Root);
		Assert.Same(right, layout.Panels["right"]);

		Assert.True(layout.OpenPanel("left"));
		Assert.True(left.IsOpen);
		Assert.NotNull(layout.FindTabs("left"));
		Assert.Same(layout.FindTabs("left"), layout.FindTabs("right"));

		Assert.True(layout.ClosePanel("left"));
		Assert.False(layout.ClosePanel("left"));
	}

	[Fact]
	public void MovePanel_TransfersBetweenGroupsAndCollapsesSource()
	{
		DockPanel left = new("left", "Left");
		DockPanel middle = new("middle", "Middle");
		DockPanel right = new("right", "Right");
		DockTabsNode leftTabs = new(["left"]);
		DockTabsNode rightTabs = new(["right"]);
		DockLayout layout = new(
			new DockSplitNode(
				UiOrientation.Horizontal,
				leftTabs,
				rightTabs),
			[left, middle, right]);

		Assert.True(layout.MovePanel("left", rightTabs));

		Assert.Empty(leftTabs.PanelIds);
		Assert.Equal(["right", "left"], rightTabs.PanelIds);
		Assert.Equal("left", rightTabs.ActivePanelId);
		Assert.IsType<DockTabsNode>(layout.Root);

		Assert.True(layout.MovePanel("left", rightTabs, 0));
		Assert.Equal(["left", "right"], rightTabs.PanelIds);
	}

	[Fact]
	public void Deserialize_RejectsMalformedAndUnknownContent()
	{
		Assert.Throws<InvalidDataException>(
			() => DockLayout.Deserialize("{not json}"));

		Assert.Throws<InvalidDataException>(
			() => DockLayout.Deserialize(
				"""{"version":99,"panels":[],"root":null}"""));

		Assert.Throws<InvalidDataException>(
			() => DockLayout.Deserialize(
				"""{"version":1,"panels":[{"id":"a","isOpen":true}],"root":{"kind":"tabs","panels":["missing"]}}"""));

		Assert.Throws<InvalidDataException>(
			() => DockLayout.Deserialize(
				"""{"version":1,"panels":[{"id":"a"},{"id":"a"}],"root":null}"""));
	}

	[Fact]
	public void Host_RebuildPreservesContentAndDisposesPublishedState()
	{
		DockPanel left = new("left", "Left");
		DockPanel right = new("right", "Right");
		DockLayout layout = new(
			new DockSplitNode(
				UiOrientation.Horizontal,
				new DockTabsNode(["left"]),
				new DockTabsNode(["right"])),
			[left, right]);

		Dictionary<string, Label> contents =
			new(StringComparer.Ordinal);

		DockHost host = new(layout, id =>
		{
			Label label = new() { Text = id };
			contents[id] = label;
			return label;
		});

		using UiTestHost ui = new(800f, 600f);
		ui.Add(host);
		ui.Layout();

		Assert.Same(contents["left"], contents["left"]);
		Assert.NotNull(contents["left"].Parent);

		layout.ClosePanel("left");
		ui.Layout();

		Assert.False(contents["left"].IsDisposed);
		Assert.Null(contents["left"].Parent);

		layout.OpenPanel("left");
		ui.Layout();

		Assert.NotNull(contents["left"].Parent);
		Assert.False(contents["left"].IsDisposed);

		layout.ClosePanel("left");
		ui.Layout();
		host.Dispose();

		Assert.True(contents["left"].IsDisposed);
		Assert.True(contents["right"].IsDisposed);
	}

	private sealed class Label : Volt.Ui.Widgets.Widget
	{
		public string Text { get; set; } = string.Empty;
	}
}
