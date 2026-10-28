using System.Text;
using Volt.Ui.Docking;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Rendering;

/// <summary>
/// Verifies that layout, painting, text, docking, and hit testing all
/// describe positions in one coordinate system: the origin is the
/// top-left corner of the surface and Y increases downward.
/// </summary>
/// <remarks>
/// The Vulkan backend's conversion of that pixel space into clip space
/// is covered by <c>tests/Volt.Ui.Vulkan.Tests</c>. These tests pin the
/// framework side of the contract, where a disagreement would show up
/// as a hit at the wrong widget or text painted outside its bounds.
/// </remarks>
public sealed class CoordinateSystemTests
{
	[Fact]
	public void VerticalStack_PlacesLaterChildrenBelowEarlierOnes()
	{
		using UiTestHost host = new(400f, 300f);
		StackPanel stack = host.Add(new StackPanel());

		Button first = stack.Add(new Button("First"));
		Button second = stack.Add(new Button("Second"));

		host.Layout();

		Assert.True(
			first.GlobalVisualBounds.Center.Y <
			second.GlobalVisualBounds.Center.Y,
			"Y must grow downward for a vertical stack.");
	}

	[Fact]
	public void HitTest_ReportsTheWidgetArrangedAtThatPoint()
	{
		using UiTestHost host = new(400f, 300f);
		StackPanel stack = host.Add(new StackPanel());

		Button first = stack.Add(new Button("First"));
		Button second = stack.Add(new Button("Second"));

		host.Layout();

		Widget? firstHit =
			host.Context.FindWidgetAt(first.GlobalVisualBounds.Center);

		Widget? secondHit =
			host.Context.FindWidgetAt(second.GlobalVisualBounds.Center);

		Assert.NotNull(firstHit);
		Assert.NotNull(secondHit);

		Assert.True(
			IsWithin(firstHit, first),
			"Hit testing at the first button's center must reach it.");

		Assert.True(
			IsWithin(secondHit, second),
			"Hit testing at the second button's center must reach it.");

		Assert.False(
			IsWithin(secondHit, first),
			"Hit testing must not report the widget above the point.");
	}

	[Fact]
	public void PaintedGeometry_CoversThePointHitTestingReports()
	{
		using UiTestHost host = new(400f, 300f);
		Button button = host.Add(new Button("Press"));

		host.Layout();

		UiPoint center = button.GlobalVisualBounds.Center;

		Assert.True(IsWithin(
			host.Context.FindWidgetAt(center),
			button));

		Assert.True(
			IsPaintedAt(host.Context.Draw(), center),
			"Nothing was painted at the point hit testing reported.");
	}

	[Fact]
	public void LabelText_IsPaintedInsideItsOwnBounds()
	{
		using UiTestHost host = new(400f, 200f);
		Label label = host.Add(new Label("VOLT"));

		host.Layout();

		UiRect bounds = label.GlobalVisualBounds;
		bool painted = false;

		foreach (UiDrawCommand command in host.Context.Draw().Commands)
		{
			if (command.Kind != UiDrawCommandKind.Text)
			{
				continue;
			}

			painted |= !command.Rect.Intersect(bounds).IsEmpty;
		}

		Assert.True(
			painted,
			"The label's text must be painted inside the label's bounds.");
	}

	[Fact]
	public void LabelText_IsPaintedAtTheLabelsGlobalPosition()
	{
		// Widgets draw into a global draw list, so text must use the
		// global content rectangle. Using the local one only looked
		// right while the parent happened to be at the origin.
		using UiTestHost host = new(400f, 300f);
		StackPanel stack = host.Add(new StackPanel { Spacing = 0f });

		stack.Add(new Panel { Height = UiLength.Points(120f) });

		Label label = stack.Add(new Label("VOLT"));

		host.Layout();

		UiRect bounds = label.GlobalVisualBounds;
		bool painted = false;

		foreach (UiDrawCommand command in host.Context.Draw().Commands)
		{
			if (command.Kind != UiDrawCommandKind.Text)
			{
				continue;
			}

			painted |= !command.Rect.Intersect(bounds).IsEmpty;
		}

		Assert.True(
			painted,
			$"Text must be painted where the label is: label=" +
			$"{bounds.Y}..{bounds.Y + bounds.Height}.");
	}

	[Fact]
	public void HorizontalDockSplit_PlacesTheFirstRegionLeftOfTheSecond()
	{
		Dictionary<string, Panel> contents = BuildDock(
			new DockSplitNode(
				UiOrientation.Horizontal,
				new DockTabsNode(["first"]),
				new DockTabsNode(["second"])),
			["first", "second"]);

		UiPoint firstCenter = contents["first"].GlobalVisualBounds.Center;
		UiPoint secondCenter = contents["second"].GlobalVisualBounds.Center;

		Assert.True(
			firstCenter.X < secondCenter.X,
			"A horizontal dock split must place its first region " +
			"to the left.");
	}

	[Fact]
	public void VerticalDockSplit_PlacesTheFirstRegionAboveTheSecond()
	{
		Dictionary<string, Panel> contents = BuildDock(
			new DockSplitNode(
				UiOrientation.Vertical,
				new DockTabsNode(["first"]),
				new DockTabsNode(["second"])),
			["first", "second"]);

		UiPoint firstCenter = contents["first"].GlobalVisualBounds.Center;
		UiPoint secondCenter = contents["second"].GlobalVisualBounds.Center;

		Assert.True(
			firstCenter.Y < secondCenter.Y,
			"A vertical dock split must place its first region above " +
			"its second.");
	}

	[Fact]
	public void DockPanels_AreArrangedInsideTheSurface()
	{
		DockLayout layout = DockLayout.CreateEditorDefault();

		Dictionary<string, Panel> contents =
			new(StringComparer.Ordinal);

		DockHost dock = new(layout, id =>
		{
			Panel panel = new();
			contents[id] = panel;
			return panel;
		});

		using UiTestHost host = new(800f, 600f);
		host.Add(dock);
		host.Layout();

		Assert.Equal(layout.Panels.Count, contents.Count);

		UiRect surface = new(0f, 0f, 800f, 600f);
		StringBuilder report = new();
		int arranged = 0;

		foreach (KeyValuePair<string, Panel> entry in contents)
		{
			UiRect bounds = entry.Value.GlobalVisualBounds;

			bool isActiveTab =
				layout.FindTabs(entry.Key)?.ActivePanelId == entry.Key;

			report.Append(
				$"[{entry.Key} active={isActiveTab} " +
				$"empty={bounds.IsEmpty} " +
				$"center=({bounds.Center.X},{bounds.Center.Y})] ");

			if (!isActiveTab)
			{
				continue;
			}

			arranged++;

			Assert.True(
				!bounds.Intersect(surface).IsEmpty,
				$"Active panel '{entry.Key}' must be arranged inside " +
				$"the surface. {report}");
		}

		Assert.True(
			arranged > 0,
			$"At least one dock panel must be arranged. {report}");
	}

	[Fact]
	public void NestedContent_IsPlacedRelativeToItsParentOrigin()
	{
		// Arrange passes a parent-local rectangle to each child, so a
		// container must not forward its own position inside its
		// parent. Doing so places nested content outside the widget
		// that owns it and the error compounds with every level.
		using UiTestHost host = new(400f, 300f);
		StackPanel stack = host.Add(new StackPanel { Spacing = 0f });

		Panel first = stack.Add(new Panel
		{
			Height = UiLength.Points(120f)
		});

		Panel second = stack.Add(new Panel
		{
			Height = UiLength.Points(120f)
		});

		Panel nested = second.Add(new Panel
		{
			Height = UiLength.Points(20f),
			VerticalAlignment = UiAlignment.Start
		});

		host.Layout();

		UiRect firstBounds = first.GlobalVisualBounds;
		UiRect secondBounds = second.GlobalVisualBounds;
		UiRect nestedBounds = nested.GlobalVisualBounds;

		Assert.True(
			MathF.Abs(secondBounds.Y - firstBounds.Bottom) < 0.01f,
			$"The second child must start where the first ends: " +
			$"first bottom={firstBounds.Bottom}, second top=" +
			$"{secondBounds.Y}.");

		Assert.True(
			nestedBounds.Y >= secondBounds.Y - 0.01f &&
			nestedBounds.Y < secondBounds.Y + secondBounds.Height,
			$"Nested content must be arranged inside its parent: " +
			$"parent={secondBounds.Y}.." +
			$"{secondBounds.Y + secondBounds.Height}, nested=" +
			$"{nestedBounds.Y}.");

		Assert.True(
			nestedBounds.X >= secondBounds.X - 0.01f &&
			nestedBounds.X < secondBounds.X + secondBounds.Width,
			$"Nested content must be arranged inside its parent " +
			$"horizontally: parent={secondBounds.X}.." +
			$"{secondBounds.X + secondBounds.Width}, nested=" +
			$"{nestedBounds.X}.");
	}

	private static Dictionary<string, Panel> BuildDock(
		DockNode root,
		string[] panelIds)
	{
		DockPanel[] panels = new DockPanel[panelIds.Length];

		for (int index = 0; index < panelIds.Length; index++)
		{
			panels[index] = new DockPanel(panelIds[index], panelIds[index]);
		}

		DockLayout layout = new(root, panels);

		Dictionary<string, Panel> contents =
			new(StringComparer.Ordinal);

		DockHost dock = new(layout, id =>
		{
			Panel panel = new();
			contents[id] = panel;
			return panel;
		});

		using UiTestHost host = new(800f, 600f);
		host.Add(dock);
		host.Layout();

		// The host is disposed with the test, so the arranged bounds
		// captured above stay valid for the assertions.
		return contents;
	}

	private static bool IsPaintedAt(UiDrawList list, UiPoint point)
	{
		UiRect probe = new(point.X, point.Y, 1f, 1f);

		foreach (UiDrawCommand command in list.Commands)
		{
			if (command.Kind == UiDrawCommandKind.Line)
			{
				continue;
			}

			if (!command.Rect.Intersect(probe).IsEmpty)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsWithin(Widget? widget, Widget ancestor)
	{
		for (Widget? current = widget;
			 current is not null;
			 current = current.Parent)
		{
			if (ReferenceEquals(current, ancestor))
			{
				return true;
			}
		}

		return false;
	}
}
