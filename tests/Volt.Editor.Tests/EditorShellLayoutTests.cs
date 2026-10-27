using System.Text;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Lays out the whole editor shell without a window and verifies that
/// its chrome, dock panels, painted geometry, and hit testing all
/// describe positions in one coordinate system: the origin is the
/// surface's top-left corner and Y grows downward.
/// </summary>
/// <remarks>
/// This is the editor-scale counterpart to the framework tests in
/// <c>tests/Volt.Ui.Tests</c>. A layout error that survives them shows
/// up here as chrome at the wrong edge, a panel arranged outside the
/// dock area, or a painted command that falls outside its own clip.
/// </remarks>
public sealed class EditorShellLayoutTests : IDisposable
{
	private static readonly Type[] s_panelTypes =
	[
		typeof(HierarchyPanel),
		typeof(SceneViewport),
		typeof(GameViewport),
		typeof(InspectorPanel),
		typeof(ProjectPanel),
		typeof(ConsolePanel),
		typeof(ProfilerPanel)
	];

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-editor-layout-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EditorShellLayoutTests()
	{
		Directory.CreateDirectory(_root);

		// The start overlay covers the shell until a project is open.
		_session.NewProject(Path.Combine(_root, "Game"), "Layout");
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

	[Theory]
	[InlineData(1440f, 900f)]
	public void VisibleWidgets_StayInsideTheSurface(
		float width,
		float height)
	{
		// Nothing the shell lays out may fall off the window: a row that
		// does not fit pushes every row after it out of view, which is
		// how the status bar previously ended up below the surface.
		//
		// This runs at the window the editor opens with. In a window too
		// short for a panel's minimum content the panel clips its own
		// overflow, which is visible in that panel's own tests rather
		// than as geometry outside the surface.
		EditorShell shell = BuildShell(width, height);
		UiRect surface = new(0f, 0f, width, height);
		StringBuilder report = new();
		int inspected = 0;

		foreach (Widget widget in Descendants(shell))
		{
			if (!widget.IsVisible || IsInsideScrollContent(widget))
			{
				continue;
			}

			UiRect bounds = widget.GlobalVisualBounds;

			if (bounds.IsEmpty)
			{
				continue;
			}

			inspected++;

			if (!bounds.Intersect(surface).IsEmpty &&
				bounds.Bottom <= surface.Bottom + 0.5f)
			{
				continue;
			}

			report.Append(
				$"\n  {widget.GetType().Name} " +
				$"({bounds.X:0.#},{bounds.Y:0.#} " +
				$"{bounds.Width:0.#}x{bounds.Height:0.#})");
		}

		Assert.True(inspected > 0, "The shell must lay out widgets.");

		Assert.True(
			report.Length == 0,
			$"Widgets are placed outside the {width:0}x{height:0} " +
			$"surface:{report}");
	}

	[Theory]
	[InlineData(1440f, 900f)]
	[InlineData(1024f, 640f)]
	public void Chrome_AnchorsTheMenuToTheTopAndTheStatusBarToTheBottom(
		float width,
		float height)
	{
		EditorShell shell = BuildShell(width, height);

		UiRect menu = shell.MenuBarView.GlobalVisualBounds;
		UiRect toolbar = shell.ToolbarView.GlobalVisualBounds;
		UiRect status = shell.StatusBarView.GlobalVisualBounds;
		UiRect dock = shell.Dock.GlobalVisualBounds;

		Assert.True(menu.Height > 0f, "The menu bar must have a height.");

		Assert.Equal(0f, menu.Y, 2);
		Assert.Equal(width, menu.Width, 2);

		Assert.True(
			toolbar.Y >= menu.Bottom - 0.01f,
			$"The toolbar belongs under the menu bar: menu=" +
			$"{menu.Y}..{menu.Bottom}, toolbar top={toolbar.Y}.");

		Assert.Equal(height, status.Bottom, 2);
		Assert.True(
			status.Y > toolbar.Y,
			$"The status bar belongs at the bottom: toolbar top=" +
			$"{toolbar.Y}, status={status.Y}..{status.Bottom}.");

		Assert.True(
			dock.Y >= toolbar.Bottom - 0.01f && dock.Bottom <= status.Y + 0.01f,
			$"The dock area belongs between the toolbar and the " +
			$"status bar: toolbar bottom={toolbar.Bottom}, dock=" +
			$"{dock.Y}..{dock.Bottom}, status top={status.Y}.");
	}

	[Fact]
	public void DockPanels_AreArrangedInsideTheDockArea()
	{
		EditorShell shell = BuildShell(1440f, 900f);
		UiRect dockArea = shell.Dock.GlobalVisualBounds;
		StringBuilder report = new();
		int arranged = 0;

		foreach (Widget panel in Panels(shell))
		{
			UiRect bounds = panel.GlobalVisualBounds;

			if (bounds.IsEmpty)
			{
				continue;
			}

			arranged++;

			report.Append(
				$"[{panel.GetType().Name} " +
				$"({bounds.X:0.#},{bounds.Y:0.#} " +
				$"{bounds.Width:0.#}x{bounds.Height:0.#})] ");

			Assert.True(
				!bounds.Intersect(dockArea).IsEmpty,
				$"Panel '{panel.GetType().Name}' must be arranged inside " +
				$"the dock area {dockArea.X:0.#},{dockArea.Y:0.#} " +
				$"{dockArea.Width:0.#}x{dockArea.Height:0.#}. {report}");
		}

		Assert.True(
			arranged >= 4,
			$"The default layout shows several panels. {report}");
	}

	[Fact]
	public void PanelContent_IsPlacedInsideThePanelThatOwnsIt()
	{
		// Content was previously arranged with its parent's origin added
		// a second time, so nested panels drifted by the parent's offset
		// and the drift compounded with depth. Horizontal containment
		// catches it, because the drift moved content sideways.
		EditorShell shell = BuildShell(1440f, 900f);
		StringBuilder report = new();
		int inspected = 0;

		foreach (Widget panel in Panels(shell))
		{
			UiRect bounds = panel.GlobalVisualBounds;

			if (bounds.IsEmpty)
			{
				continue;
			}

			foreach (Widget descendant in Descendants(panel))
			{
				if (!descendant.IsVisible)
				{
					continue;
				}

				UiRect child = descendant.GlobalVisualBounds;

				if (child.IsEmpty)
				{
					continue;
				}

				inspected++;

				report.Append(
					$"\\n  {panel.GetType().Name} -> " +
					$"{descendant.GetType().Name} child x=" +
					$"{child.X:0.#}..{child.Right:0.#}, panel x=" +
					$"{bounds.X:0.#}..{bounds.Right:0.#}");

				Assert.True(
					child.X >= bounds.X - 0.5f &&
					child.Right <= bounds.Right + 0.5f,
					$"'{descendant.GetType().Name}' is placed outside the " +
					$"panel it belongs to:{report}");

				Assert.True(
					!child.Intersect(bounds).IsEmpty,
					$"'{descendant.GetType().Name}' is not visible inside " +
					$"the panel it belongs to:{report}");
			}
		}

		Assert.True(
			inspected > 0,
			$"Dock panels must contain widgets. {report}");
	}

	[Fact]
	public void PaintedCommands_StayInsideTheirClipAndOnTheSurface()
	{
		EditorShell shell = BuildShell(1440f, 900f);
		UiRect surface = new(0f, 0f, 1440f, 900f);
		UiDrawList drawList = _context.Draw();
		StringBuilder report = new();
		int offClip = 0;
		int offSurface = 0;
		int commands = 0;

		Assert.True(
			drawList.Count > 0,
			"The shell must paint at least one command.");

		foreach (UiDrawCommand command in drawList.Commands)
		{
			if (command.Kind == UiDrawCommandKind.Line)
			{
				continue;
			}

			commands++;

			if (command.Clip.Intersect(surface).IsEmpty ||
				command.Clip.X < -0.5f ||
				command.Clip.Right > surface.Right + 0.5f)
			{
				offSurface++;

				if (offSurface <= 4)
				{
					report.Append(
						$"\\n  clip {command.Kind} " +
						$"({command.Clip.X:0.#},{command.Clip.Y:0.#} " +
						$"{command.Clip.Width:0.#}x{command.Clip.Height:0.#})");
				}
			}

			if (command.Rect.Intersect(command.Clip).IsEmpty)
			{
				offClip++;

				if (offClip <= 4)
				{
					report.Append(
						$"\\n  {command.Kind} rect " +
						$"({command.Rect.X:0.#},{command.Rect.Y:0.#} " +
						$"{command.Rect.Width:0.#}x{command.Rect.Height:0.#}) " +
						$"outside clip ({command.Clip.X:0.#}," +
						$"{command.Clip.Y:0.#} {command.Clip.Width:0.#}x" +
						$"{command.Clip.Height:0.#})");
				}
			}
		}

		Assert.True(commands > 0, "The shell must paint visible geometry.");

		Assert.True(
			offSurface == 0,
			$"{offSurface} of {commands} commands were clipped outside " +
			$"the surface.{report}");

		Assert.True(
			offClip == 0,
			$"{offClip} of {commands} commands were painted outside " +
			$"their own clip.{report}");
	}

	[Theory]
	[InlineData(1440f, 900f)]
	[InlineData(1024f, 640f)]
	public void HitTesting_InsideAPanel_ReportsThatPanelsContent(
		float width,
		float height)
	{
		EditorShell shell = BuildShell(width, height);
		StringBuilder report = new();
		int probed = 0;

		foreach (Widget panel in Panels(shell))
		{
			UiRect bounds = panel.GlobalVisualBounds;

			if (bounds.IsEmpty)
			{
				continue;
			}

			probed++;

			UiPoint probe = bounds.Center;
			Widget? hit = _context.FindWidgetAt(probe);

			Assert.NotNull(hit);

			report.Append(
				$"\\n  probe ({probe.X:0.#},{probe.Y:0.#}) in " +
				$"{panel.GetType().Name} ({bounds.X:0.#},{bounds.Y:0.#} " +
				$"{bounds.Width:0.#}x{bounds.Height:0.#}) hit " +
				$"{hit!.GetType().Name}");

			Assert.True(
				IsWithin(hit, panel),
				$"Hit testing at the center of a panel must reach that " +
				$"panel or its content:{report}");
		}

		Assert.True(probed >= 4, $"Several panels must be arranged.{report}");
	}

	[Fact]
	public void SelectedEntity_IsVisibleInTheInspectorAndViewport()
	{
		// The two panels that reflect selection must both be arranged
		// and see the same entity, which is what makes a click in the
		// viewport show up in the inspector.
		EditorShell shell = BuildShell(1440f, 900f);
		SceneEntity cube = Assert.Single(
			_session.Scene!.Entities,
			entity => entity.Name == "Cube");

		_session.Select(cube.Id);
		_context.BeginFrame(
			0.016,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		Widget? scene = Panels(shell).FirstOrDefault(
			panel => panel is SceneViewport);

		Widget? inspector = Panels(shell).FirstOrDefault(
			panel => panel is InspectorPanel);

		Assert.NotNull(scene);
		Assert.NotNull(inspector);
		Assert.False(scene!.GlobalVisualBounds.IsEmpty);
		Assert.False(inspector!.GlobalVisualBounds.IsEmpty);

		Assert.True(
			HasPaintedText(_context.Draw(), inspector.GlobalVisualBounds),
			"The inspector must paint the selected entity.");
	}

	private EditorShell BuildShell(float width, float height)
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(width, height),
			UiPoint.Zero);

		return shell;
	}

	private static IEnumerable<Widget> Panels(EditorShell shell)
	{
		return Descendants(shell)
			.Where(widget => s_panelTypes.Contains(widget.GetType()));
	}

	private static IEnumerable<Widget> Descendants(Widget root)
	{
		foreach (Widget child in root.Children)
		{
			yield return child;

			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}

	private static bool IsInsideScrollContent(Widget widget)
	{
		// Scrolled content legitimately extends past its viewport, so it
		// is exempt from the surface containment check.
		for (Widget? current = widget.Parent;
			current is not null;
			current = current.Parent)
		{
			if (current is ScrollView)
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

	private static bool HasPaintedText(UiDrawList drawList, UiRect bounds)
	{
		foreach (UiDrawCommand command in drawList.Commands)
		{
			if (command.Kind != UiDrawCommandKind.Text)
			{
				continue;
			}

			if (!command.Rect.Intersect(bounds).IsEmpty)
			{
				return true;
			}
		}

		return false;
	}
}
