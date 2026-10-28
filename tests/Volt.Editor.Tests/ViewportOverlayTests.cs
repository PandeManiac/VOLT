using System.Numerics;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the scene view's floating chrome: the tool and display
/// clusters, the orientation compass, hover feedback, and the fact
/// that the chrome takes a click before the camera does.
/// </summary>
public sealed class ViewportOverlayTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-viewport-overlay-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ViewportOverlayTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Overlay");
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void Chrome_StaysInsideTheViewportAndOffersEveryControl()
	{
		SceneViewport viewport = BuildViewport(960f, 600f);
		UiRect bounds = viewport.GlobalVisualBounds;
		ViewportOverlay overlay = viewport.OverlayView;

		Assert.True(
			bounds.Width >= 900f && bounds.Height >= 560f,
			$"The viewport must fill the view it was given, got {bounds}.");

		// Four tools and five display controls, as one plate per
		// cluster.
		Assert.Equal(9, overlay.Items.Count);
		Assert.Equal(2, overlay.Groups.Count);

		Assert.All(
			overlay.Items,
			item => Assert.True(
				bounds.Contains(item.Bounds),
				$"'{item.Label}' escapes the viewport: {item.Bounds}."));

		Assert.All(
			overlay.Groups,
			group => Assert.True(
				bounds.Contains(group),
				$"A cluster plate escapes the viewport: {group}."));

		foreach (EditorTool tool in Enum.GetValues<EditorTool>())
		{
			ViewportOverlayItem item = Select(
				overlay,
				ViewportOverlayControl.Tool,
				tool);

			Assert.Equal(tool == _session.Tool, item.IsActive);
			Assert.True(item.IsEnabled);
			Assert.Contains(EditorToolInfo.Label(tool), item.Label);
			Assert.NotEqual(UiIcon.None, item.Icon);
		}

		// The display cluster reports the settings it edits.
		Assert.Equal(
			_session.LocalTransform,
			Select(overlay, ViewportOverlayControl.ToggleSpace).IsActive);

		Assert.Equal(
			_session.GridSnapping,
			Select(overlay, ViewportOverlayControl.ToggleSnap).IsActive);

		Assert.Equal(
			viewport.ShowGrid,
			Select(overlay, ViewportOverlayControl.ToggleGrid).IsActive);
	}

	[Fact]
	public void Chrome_WrapsIntoRowsInsteadOfOverflowingANarrowView()
	{
		SceneViewport viewport = BuildViewport(260f, 220f);
		UiRect bounds = viewport.GlobalVisualBounds;
		ViewportOverlay overlay = viewport.OverlayView;

		Assert.All(
			overlay.Items,
			item => Assert.True(
				bounds.Contains(item.Bounds),
				$"'{item.Label}' escapes the narrow viewport: {item.Bounds}."));

		int rows = overlay.Items
			.Select(item => (int)MathF.Round(item.Bounds.Y))
			.Distinct()
			.Count();

		Assert.True(rows >= 2, "The chrome must wrap when the view is narrow.");

		Assert.True(
			overlay.Groups.Count >= 3,
			"A wrapped cluster keeps one plate per row.");
	}

	[Fact]
	public void Chrome_DisablesTheToolsWhileTheSceneRuns()
	{
		// A running game owns its objects, so the authoring tools are
		// shown but inert; the display controls stay usable.
		UiTheme theme = UiTheme.CreateDark();
		ViewportOverlay overlay = new();

		overlay.Build(
			new UiRect(0f, 0f, 960f, 600f),
			_session,
			theme.Metrics,
			theme.Fonts.Body,
			showGrid: true,
			isPlaying: true);

		foreach (EditorTool tool in Enum.GetValues<EditorTool>())
		{
			ViewportOverlayItem item = Select(
				overlay,
				ViewportOverlayControl.Tool,
				tool);

			Assert.False(item.IsEnabled);

			Assert.False(
				overlay.Pick(item.Bounds.Center).IsEnabled,
				"A disabled control must report itself disabled.");
		}

		Assert.True(Select(overlay, ViewportOverlayControl.ToggleGrid).IsEnabled);
	}

	[Fact]
	public void Chrome_DrivesTheToolsAndTheDisplaySettings()
	{
		SceneViewport viewport = BuildViewport(960f, 600f);

		Click(Select(viewport.OverlayView, ViewportOverlayControl.Tool, EditorTool.Select).Bounds.Center);
		Assert.Equal(EditorTool.Select, _session.Tool);

		bool local = _session.LocalTransform;
		Click(Select(viewport.OverlayView, ViewportOverlayControl.ToggleSpace).Bounds.Center);
		Assert.NotEqual(local, _session.LocalTransform);

		bool snapping = _session.GridSnapping;
		Click(Select(viewport.OverlayView, ViewportOverlayControl.ToggleSnap).Bounds.Center);
		Assert.NotEqual(snapping, _session.GridSnapping);

		float step = _session.SnapStep;
		Click(Select(viewport.OverlayView, ViewportOverlayControl.CycleSnapStep).Bounds.Center);
		Assert.True(
			_session.SnapStep > step,
			"The snap increment must cycle upwards.");

		Assert.True(viewport.ShowGrid);
		Click(Select(viewport.OverlayView, ViewportOverlayControl.ToggleGrid).Bounds.Center);
		Assert.False(viewport.ShowGrid);

		Vector3 before = viewport.Camera.Position;
		Click(Select(viewport.OverlayView, ViewportOverlayControl.FrameSelection).Bounds.Center);

		Assert.NotEqual(before, viewport.Camera.Position);
	}

	[Fact]
	public void Chrome_ExplainsItsControlsAndPaintsThem()
	{
		SceneViewport viewport = BuildViewport(960f, 600f);
		ViewportOverlay overlay = viewport.OverlayView;
		ViewportOverlayItem snap = Select(overlay, ViewportOverlayControl.ToggleSnap);

		UiDrawList drawList = _context.Draw();

		foreach (ViewportOverlayItem item in overlay.Items)
		{
			Assert.True(
				Paints(drawList, item.Bounds),
				$"'{item.Label}' painted nothing inside {item.Bounds}.");
		}

		Assert.True(
			Paints(
				drawList,
				viewport.CompassView.BoundsOf(ViewportCompassControl.Home)),
			"The compass painted nothing.");

		// Nothing is explained until the pointer rests on a control.
		Assert.Null(viewport.HoverText);
		Assert.False(viewport.IsTooltipVisible);

		_context.PointerMove(snap.Bounds.Center);

		Assert.Equal("Grid snapping", viewport.HoverText);
		Assert.False(viewport.IsTooltipVisible);

		viewport.Tick(0.6, 1f);

		Assert.True(viewport.IsTooltipVisible);

		drawList = _context.Draw();

		Assert.True(
			PaintsText(
				drawList,
				new UiRect(
					snap.Bounds.X - 8f,
					snap.Bounds.Bottom,
					280f,
					40f)),
			"The tooltip must paint below its control.");

		// Leaving the control clears it again.
		_context.PointerMove(
			new UiPoint(
				viewport.GlobalVisualBounds.Center.X,
				viewport.GlobalVisualBounds.Bottom - 40f));

		Assert.Null(viewport.HoverText);
		Assert.False(viewport.IsTooltipVisible);
	}

	[Fact]
	public void Compass_SnapsTheCameraToAnAxisAndBackHome()
	{
		SceneViewport viewport = BuildViewport(960f, 600f);
		ViewportCompass compass = viewport.CompassView;

		Assert.True(compass.IsVisible);
		Assert.Equal(6, compass.Tips.Count);

		Assert.All(
			compass.Tips,
			tip => Assert.True(
				viewport.GlobalVisualBounds.Contains(
					new UiRect(
						tip.Position.X - 1f,
						tip.Position.Y - 1f,
						2f,
						2f)),
				$"The '{tip.Label}' marker escapes the viewport."));

		// The markers facing the viewer are the ones drawn in front.
		Assert.Contains(compass.Tips, tip => tip.TowardViewer);
		Assert.Contains(compass.Tips, tip => !tip.TowardViewer);

		// Framing the scene origin keeps the distance arithmetic
		// exact, so the snap can be shown to preserve the zoom.
		_session.ClearSelection();
		_context.Draw();

		float distance = viewport.Camera.Position.Length();

		Click(Select(compass, ViewportCompassControl.ViewFromY).Position);

		Assert.True(
			Vector3.Dot(viewport.Camera.Forward, Vector3.UnitY) < -0.98f,
			"The top view must look down at the scene.");

		Assert.True(viewport.Camera.Position.Y > 0f);
		Assert.Equal(distance, viewport.Camera.Position.Length(), 1);

		// The markers are re-projected from the new pose on the next
		// frame, which is the state a click always finds them in.
		_context.Draw();

		// Looking along an axis from that side of the scene.
		Click(Select(compass, ViewportCompassControl.ViewFromX).Position);

		Assert.True(
			Vector3.Dot(viewport.Camera.Forward, Vector3.UnitX) < -0.98f,
			"The X view must look along the X axis.");

		Assert.Equal(distance, viewport.Camera.Position.Length(), 1);

		// Home still restores the three-quarter angle, even though the X
		// marker now covers the middle of the compass: that spot belongs
		// to the home target, because snapping to the axis the camera is
		// already on would do nothing.
		Click(compass.BoundsOf(ViewportCompassControl.Home).Center);

		Assert.True(
			Vector3.Dot(
				viewport.Camera.Forward,
				Vector3.Normalize(-EditorCamera.DefaultPosition)) > 0.98f,
			"The home target must restore the default three-quarter angle.");

		Assert.Equal(distance, viewport.Camera.Position.Length(), 1);
	}

	[Fact]
	public void Compass_HidesWhenTheViewportIsTooSmall()
	{
		SceneViewport viewport = BuildViewport(240f, 200f);

		Assert.False(viewport.CompassView.IsVisible);
		Assert.Empty(viewport.CompassView.Tips);

		// The chrome is still laid out inside what little there is.
		Assert.NotEmpty(viewport.OverlayView.Items);

		Assert.All(
			viewport.OverlayView.Items,
			item => Assert.True(
				viewport.GlobalVisualBounds.Contains(item.Bounds)));
	}

	[Fact]
	public void Toolbar_FollowsSettingsChangedByAnotherSurface()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		List<Widget> toolbar = [];
		Collect(shell.ToolbarView, toolbar);

		IconButton moveButton = toolbar
			.OfType<IconButton>()
			.Single(button => button.Icon == UiIcon.Move);

		IconButton scaleButton = toolbar
			.OfType<IconButton>()
			.Single(button => button.Icon == UiIcon.Scale);

		ToolButton space = toolbar
			.OfType<ToolButton>()
			.Single(button => button.Text is "Local" or "World");

		NumericField step = toolbar.OfType<NumericField>().Single();

		// The session opens with the move tool active.
		Assert.True(moveButton.IsActive);
		Assert.False(scaleButton.IsActive);

		// A viewport's chrome edits the same settings; the toolbar has
		// to follow whichever surface made the change.
		_session.Tool = EditorTool.Scale;
		_session.LocalTransform = false;
		_session.GridSnapping = true;
		_session.SnapStep = 1f;

		Assert.True(scaleButton.IsActive);
		Assert.False(moveButton.IsActive);
		Assert.False(space.IsActive);
		Assert.Equal("World", space.Text);
		Assert.Equal(1f, step.Value, 3);
	}

	private SceneViewport BuildViewport(float width, float height)
	{
		SceneViewport viewport = new(_session);

		_context.Add(viewport);
		_context.BeginFrame(
			0.0,
			new UiSize(width, height),
			UiPoint.Zero);

		// The chrome is laid out while painting, exactly as it is in
		// the running editor, so a frame is drawn before it is read.
		_context.Draw();

		return viewport;
	}

	private void Click(UiPoint point)
	{
		_context.PointerDown(UiMouseButton.Left, point);
		_context.PointerUp(UiMouseButton.Left, point);
	}

	private static ViewportOverlayItem Select(
		ViewportOverlay overlay,
		ViewportOverlayControl control,
		EditorTool tool = EditorTool.Translate)
	{
		return overlay.Items.First(
			item => item.Control == control &&
				(control != ViewportOverlayControl.Tool || item.Tool == tool));
	}

	private static CompassTip Select(
		ViewportCompass compass,
		ViewportCompassControl control)
	{
		return compass.Tips.First(tip => tip.Control == control);
	}

	/// <summary>
	/// Reports whether anything the size of the area was painted into
	/// it. A cluster's backing plate covers its controls, so a command
	/// larger than the area does not count as the area painting itself.
	/// </summary>
	private static bool Paints(UiDrawList drawList, UiRect area)
	{
		if (area.IsEmpty)
		{
			return false;
		}

		for (int index = 0; index < drawList.Count; index++)
		{
			UiRect rect = Extent(drawList.GetCommand(index));

			if (rect.Intersect(area).IsEmpty)
			{
				continue;
			}

			if (rect.Width <= area.Width + 1f &&
				rect.Height <= area.Height + 1f)
			{
				return true;
			}
		}

		return false;
	}

	private static bool PaintsText(UiDrawList drawList, UiRect area)
	{
		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind != UiDrawCommandKind.Text)
			{
				continue;
			}

			if (!command.Rect.Intersect(area).IsEmpty)
			{
				return true;
			}
		}

		return false;
	}

	private static UiRect Extent(UiDrawCommand command)
	{
		if (command.Kind != UiDrawCommandKind.Line)
		{
			return command.Rect;
		}

		return UiRect.FromLtrb(
			MathF.Min(command.Start.X, command.End.X) - command.Thickness,
			MathF.Min(command.Start.Y, command.End.Y) - command.Thickness,
			MathF.Max(command.Start.X, command.End.X) + command.Thickness,
			MathF.Max(command.Start.Y, command.End.Y) + command.Thickness);
	}

	private static void Collect(Widget widget, List<Widget> destination)
	{
		foreach (Widget child in widget.Children)
		{
			destination.Add(child);
			Collect(child, destination);
		}
	}
}
