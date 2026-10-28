using System.Numerics;
using System.Text;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the editor chrome as a product surface: the toolbar fits,
/// the menus describe real commands, the interface scale rebuilds the
/// workspace, resetting the layout restores panels and ratios, and the
/// project browser shows catalog content.
/// </summary>
public sealed class EditorChromeTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-editor-chrome-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EditorChromeTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Chrome");
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
	[InlineData(1280f, 720f)]
	[InlineData(1600f, 900f)]
	[InlineData(1920f, 1080f)]
	public void Chrome_StaysInsideTheSurface(float width, float height)
	{
		EditorShell shell = BuildShell(width, height);

		Assert.True(
			Inspect(shell, width, height) > 0,
			"The shell must lay out widgets.");
	}

	[Theory]
	[InlineData(0.75f)]
	[InlineData(1.25f)]
	[InlineData(1.5f)]
	[InlineData(2f)]
	public void UiScale_RebuildsTheWorkspaceInsideTheSurface(float scale)
	{
		EditorShell shell = BuildShell(1440f, 900f);
		DockHost before = shell.Dock;
		float toolbarBefore = shell.ToolbarView.GlobalVisualBounds.Height;

		shell.ApplyUiScale(scale);
		_context.BeginFrame(
			0.1,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		Assert.Equal(scale, shell.UiScale, 3);

		Assert.NotSame(
			before,
			shell.Dock);

		Assert.True(
			Inspect(shell, 1440f, 900f) > 0,
			"The rebuilt shell must lay out widgets.");

		if (scale > 1f)
		{
			Assert.True(
				shell.ToolbarView.GlobalVisualBounds.Height >
					toolbarBefore,
				"A larger interface must produce a taller toolbar.");
		}
	}

	[Fact]
	public void Toolbar_KeepsEveryControlInsideTheWindow()
	{
		// A grouped tool cluster used to claim the whole row, which
		// pushed the transport and context controls past the right
		// edge of a 1280-wide window.
		EditorShell shell = BuildShell(1280f, 720f);
		UiRect toolbar = shell.ToolbarView.GlobalVisualBounds;
		StringBuilder report = new();

		foreach (Widget widget in Descendants(shell.ToolbarView))
		{
			UiRect bounds = widget.GlobalVisualBounds;

			if (bounds.IsEmpty)
			{
				continue;
			}

			report.Append(
				$"\n  {widget.GetType().Name} " +
				$"x={bounds.X:0.#}..{bounds.Right:0.#}");

			Assert.True(
				bounds.X >= toolbar.X - 0.5f &&
				bounds.Right <= toolbar.Right + 0.5f,
				$"'{widget.GetType().Name}' is placed outside the toolbar " +
				$"({toolbar.X:0.#}..{toolbar.Right:0.#}):{report}");
		}

		Assert.True(report.Length > 0, "The toolbar must contain controls.");
	}

	[Fact]
	public void MenuBar_OffersTheEditorMenusAndMarksUnavailableCommands()
	{
		EditorShell shell = BuildShell(1440f, 900f);

		string[] menus = [.. shell.MenuBarView.Menus.Select(menu => menu.Label)];

		Assert.Equal(
			new[]
			{
				"File",
				"Edit",
				"Assets",
				"GameObject",
				"Component",
				"Window",
				"Help"
			},
			menus);

		MenuItem window = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "Window");

		Assert.Contains(
			window.Children,
			item => item.Label == "Reset Layout" && item.Action is not null);

		Assert.Contains(
			window.Children,
			item => item.Label == "Interface Scale 150%" &&
				item.Action is not null);

		// Panels are toggled, and their check state mirrors the layout.
		Assert.Contains(
			window.Children,
			item => item.Label == "Inspector" &&
				item.Action is not null &&
				item.IsChecked);

		// A command the engine cannot implement yet is present but
		// disabled rather than silently decorative.
		MenuItem create = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "GameObject");

		MenuItem light = create.Children
			.Single(item => item.Label == "Create Light");

		Assert.True(light.IsEnabled);
		Assert.NotNull(light.Action);

		MenuItem edit = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "Edit");

		Assert.Contains(
			edit.Children,
			item => item.Label == "Preferences…" && item.Action is not null);

		Assert.Contains(
			edit.Children,
			item => item.Label == "Paste" && !item.IsEnabled);
	}

	[Fact]
	public void GameObjectMenu_CreatesAPrimitiveAsOneUndoStep()
	{
		EditorShell shell = BuildShell(1440f, 900f);
		SceneDocument scene = _session.Scene!;
		int before = scene.Entities.Count;
		int undoBefore = _session.Undo.UndoCount;

		MenuItem create = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "GameObject");

		create.Children
			.Single(item => item.Label == "Create Cube")
			.Action!
			.Invoke();

		Assert.Equal(before + 1, scene.Entities.Count);
		Assert.Equal(undoBefore + 1, _session.Undo.UndoCount);

		SceneEntity created = _session.ActiveEntity!;

		Assert.Equal("Cube", created.Name);
		Assert.Equal(
			"builtin:cube",
			created.GetComponent<MeshRendererData>()?.Mesh);

		_session.UndoLast();

		Assert.Equal(before, scene.Entities.Count);
	}

	[Fact]
	public void GameObjectMenu_CreatesADirectionalLightAsOneUndoStep()
	{
		EditorShell shell = BuildShell(1440f, 900f);
		SceneDocument scene = _session.Scene!;
		int before = scene.Entities.Count;
		int undoBefore = _session.Undo.UndoCount;

		MenuItem create = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "GameObject");

		create.Children
			.Single(item => item.Label == "Create Light")
			.Action!
			.Invoke();

		Assert.Equal(before + 1, scene.Entities.Count);
		Assert.Equal(undoBefore + 1, _session.Undo.UndoCount);

		SceneEntity created = _session.ActiveEntity!;
		LightData light = Assert.IsType<LightData>(
			created.GetComponent<LightData>());

		Assert.True(light.Enabled);
		Assert.Equal(1f, light.Intensity);
		Assert.Equal(Vector4.One, light.Color);
		Assert.Equal(LightData.DefaultEulerAngles, created.Transform.EulerAngles);

		_session.UndoLast();

		Assert.Equal(before, scene.Entities.Count);

		_session.RedoLast();

		Assert.Equal(before + 1, scene.Entities.Count);
		Assert.NotNull(scene.Find(created.Id)?.GetComponent<LightData>());
	}

	[Fact]
	public void ResetLayout_ReopensPanelsAndRestoresRatios()
	{
		EditorShell shell = BuildShell(1440f, 900f);

		shell.Layout.ClosePanel("console");
		shell.Layout.ClosePanel("project");

		DockSplitNode split = Assert.IsType<DockSplitNode>(shell.Layout.Root);
		shell.Layout.SetRatio(split, 0.9f);

		Assert.False(shell.Layout.IsOpen("console"));
		Assert.False(shell.Layout.IsOpen("project"));

		shell.ResetLayout();

		Assert.True(shell.Layout.IsOpen("console"));
		Assert.True(shell.Layout.IsOpen("project"));
		Assert.True(shell.Layout.IsOpen("hierarchy"));
		Assert.True(shell.Layout.IsOpen("inspector"));
		Assert.True(shell.Layout.IsOpen("scene"));

		DockSplitNode restored = Assert.IsType<DockSplitNode>(shell.Layout.Root);

		Assert.Equal(0.78f, restored.Ratio, 3);

		// The rebuilt chrome still describes the same panels.
		MenuItem window = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "Window");

		string[] panelTitles =
		[
			"Hierarchy",
			"Scene",
			"Game",
			"Inspector",
			"Project",
			"Console",
			"Profiler"
		];

		foreach (string title in panelTitles)
		{
			MenuItem item = window.Children.Single(child => child.Label == title);

			Assert.True(item.IsChecked, $"'{title}' must be open after a reset.");
		}
	}

	[Fact]
	public void ProjectBrowser_ShowsRealAssetsAfterRefreshing()
	{
		EditorShell shell = BuildShell(1440f, 900f);

		// Saving writes the scene to disk; the catalog then reports it.
		_session.SaveScene();

		ProjectPanel project = Descendants(shell)
			.OfType<ProjectPanel>()
			.Single();

		IconButton refresh = Descendants(project)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Rescan the project folder");

		UiPoint center = refresh.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
		_context.BeginFrame(
			0.2,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		IconTileView[] tiles = [.. Descendants(project).OfType<IconTileView>()];

		Assert.NotEmpty(tiles);

		Assert.Contains(
			tiles,
			tile => tile.Text == "Scenes" || tile.Text == "Assets");

		Assert.True(
			tiles.All(tile => !tile.GlobalVisualBounds.IsEmpty),
			"Every tile in the grid must be arranged.");
	}

	[Fact]
	public void Hierarchy_ShowsEntityIconsAndAnEmptyState()
	{
		EditorShell shell = BuildShell(1440f, 900f);

		HierarchyPanel hierarchy = Descendants(shell)
			.OfType<HierarchyPanel>()
			.Single();

		TreeView tree = Descendants(hierarchy).OfType<TreeView>().Single();

		Assert.NotEmpty(tree.Roots);

		Assert.All(
			tree.Roots,
			item => Assert.NotEqual(Volt.Ui.Rendering.UiIcon.None, item.Icon));

		// Emptying the scene swaps the tree for a deliberate empty state.
		SceneEntity[] entities = [.. _session.Scene!.Entities];

		foreach (SceneEntity entity in entities)
		{
			_session.Select(entity.Id);
			_session.DeleteActiveEntity();
		}

		_context.BeginFrame(
			0.3,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		Assert.Empty(tree.Roots);

		Assert.Contains(
			Descendants(hierarchy).OfType<EmptyStateView>(),
			view => view.IsVisible && view.Title.Length > 0);
	}

	[Fact]
	public void VisiblePanels_EachPaintGeometry()
	{
		// A panel that paints nothing is a blank rectangle; the whole
		// dock area is expected to describe itself on screen.
		EditorShell shell = BuildShell(1280f, 720f);
		UiDrawList drawList = _context.Draw();

		foreach (Panel panel in Descendants(shell)
			.Where(widget => widget is HierarchyPanel or
				InspectorPanel or
				ProjectPanel or
				ConsolePanel))
		{
			UiRect bounds = panel.GlobalVisualBounds;

			if (bounds.IsEmpty || !panel.IsVisible)
			{
				continue;
			}

			bool painted = false;

			for (int index = 0; index < drawList.Count; index++)
			{
				UiDrawCommand command = drawList.GetCommand(index);

				if (command.Kind == UiDrawCommandKind.Line ||
					command.Kind == UiDrawCommandKind.Text)
				{
					if (!command.Rect.Intersect(bounds).IsEmpty)
					{
						painted = true;
						break;
					}
				}
			}

			Assert.True(
				painted,
				$"Panel '{panel.GetType().Name}' painted no text or " +
				$"geometry inside {bounds.X:0.#},{bounds.Y:0.#} " +
				$"{bounds.Width:0.#}x{bounds.Height:0.#}.");
		}
	}

	[Fact]
	public void EditorShortcuts_DoNotFireWhileATextFieldIsFocused()
	{
		EditorShell shell = BuildShell(1440f, 900f);
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);

		_session.Tool = EditorTool.Select;
		_session.Select(cube.Id);

		_context.BeginFrame(
			0.1,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();

		TextField name = Descendants(inspector)
			.OfType<TextField>()
			.Single(field => field.Text == cube.Name);

		name.Focus();
		name.SetSelection(name.Text.Length, name.Text.Length);

		_context.KeyDown(UiKey.W);
		Assert.Equal(EditorTool.Select, _session.Tool);

		_context.TextInput("W");

		Assert.Equal($"{cube.Name}W", name.Text);

		int entities = scene.EntityCount;
		int history = _session.Undo.UndoCount;

		_context.KeyDown(UiKey.D, UiKeyModifiers.Control);
		_context.KeyDown(UiKey.Delete);
		_context.KeyDown(UiKey.A, UiKeyModifiers.Control | UiKeyModifiers.Shift);
		_context.KeyDown(UiKey.I, UiKeyModifiers.Control);

		Assert.Equal(entities, scene.EntityCount);
		Assert.Equal(history, _session.Undo.UndoCount);
		Assert.Contains(cube.Id, _session.Selection);

		// The shortcuts still work once the field no longer owns the
		// keyboard.
		_context.ClearFocus();
		_session.Tool = EditorTool.Select;

		Assert.True(_context.KeyDown(UiKey.W));
		Assert.Equal(EditorTool.Translate, _session.Tool);

		Assert.True(_context.KeyDown(UiKey.D, UiKeyModifiers.Control));
		Assert.Equal(entities + 1, scene.EntityCount);
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

	/// <summary>
	/// Verifies that every arranged widget sits inside the window and
	/// returns how many were inspected.
	/// </summary>
	private static int Inspect(Widget root, float width, float height)
	{
		UiRect surface = new(0f, 0f, width, height);
		StringBuilder report = new();
		int inspected = 0;

		foreach (Widget widget in Descendants(root))
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
				bounds.Bottom <= surface.Bottom + 0.5f &&
				bounds.Right <= surface.Right + 0.5f)
			{
				continue;
			}

			report.Append(
				$"\n  {widget.GetType().Name} " +
				$"({bounds.X:0.#},{bounds.Y:0.#} " +
				$"{bounds.Width:0.#}x{bounds.Height:0.#})");
		}

		Assert.True(
			report.Length == 0,
			$"Widgets are placed outside the {width:0}x{height:0} " +
			$"surface:{report}");

		return inspected;
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
}
