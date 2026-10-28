using System.Globalization;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Rendering;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor;

/// <summary>
/// The complete editor chrome, built exclusively from Volt.Ui widgets.
/// </summary>
/// <remarks>
/// <para>
/// The shell owns the menu bar, the tool strip, the dock workspace, and
/// the status readout. Panels come from <see cref="DockHost"/>, so the
/// workspace keeps its arrangement, ratios, and panel state across
/// window resizes and restarts.
/// </para>
/// <para>
/// The interface scale is a real setting rather than a startup flag:
/// changing it replaces the theme and rebuilds panel widgets so fonts
/// and metrics are rasterized at the new size instead of being
/// stretched.
/// </para>
/// </remarks>
public sealed class EditorShell : Panel
{
	private static readonly float[] s_uiScales = [0.75f, 1f, 1.25f, 1.5f, 2f];

	private readonly EditorSession _session;
	private readonly IViewportRenderer? _viewportRenderer;
	private readonly List<IFrameTick> _frameTicks = [];
	private readonly List<UiShortcut> _installedShortcuts = [];
	private readonly DockLayout _layout;
	private readonly string _defaultProjectPath;

	private readonly Dictionary<EditorTool, IconButton> _toolButtons = [];
	private readonly Dictionary<PanelTab, MenuItem> _windowMenuItems = [];
	private readonly Dictionary<float, MenuItem> _scaleMenuItems = [];

	private UiTheme _theme = UiTheme.CreateDark();

	private DockHost? _dock;
	private MenuBar? _menuBar;
	private Panel? _toolbarBar;
	private Panel? _statusBar;
	private Label? _statusScene;
	private Label? _status;
	private Label? _statusRight;
	private Panel? _startOverlay;
	private TextField? _projectPath;
	private TextField? _projectName;
	private Label? _startError;

	private IconButton? _playButton;
	private IconButton? _pauseButton;
	private IconButton? _stepButton;
	private IconButton? _stopButton;
	private IconButton? _snapButton;
	private ToolButton? _spaceButton;
	private NumericField? _snapStep;

	private float _uiScale = 1f;
	private double _frameSeconds;
	private int _frameCount;
	private float _fps;
	private bool _shortcutsInstalled;
	private bool _disposed;

	private enum PanelTab
	{
		Hierarchy,
		Scene,
		Game,
		Inspector,
		Project,
		Console,
		Profiler
	}

	/// <summary>
	/// Builds the shell without a viewport renderer.
	/// </summary>
	/// <remarks>
	/// This is the headless shape: the viewports still lay out,
	/// navigate, pick, and draw their overlays, but nothing is rendered
	/// into an image.
	/// </remarks>
	public EditorShell(
		EditorSession session,
		string? defaultProjectFolder = null,
		DockLayout? layout = null)
		: this(session, defaultProjectFolder, layout, viewportRenderer: null)
	{
	}

	internal EditorShell(
		EditorSession session,
		string? defaultProjectFolder,
		DockLayout? layout,
		IViewportRenderer? viewportRenderer)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;
		_viewportRenderer = viewportRenderer;

		ClipToBounds = true;
		Background = _theme.Palette.WindowBackground;

		_layout = layout ?? DockLayout.CreateEditorDefault();
		_defaultProjectPath = defaultProjectFolder ??
			Path.Combine(Environment.CurrentDirectory, "VoltGame");

		Build();

		_session.ProjectChanged += OnProjectChanged;
		_session.SceneChanged += OnSceneChanged;
		_session.SelectionChanged += OnSceneChanged;
		_session.PlayStateChanged += OnPlayStateChanged;
		_session.StatusChanged += OnStatusChanged;
		_session.RuntimeUpdated += OnRuntimeUpdated;
		_session.SettingsChanged += OnSettingsChanged;

		UpdateStartVisibility();
		UpdatePlayButtons();
		UpdateStatus();
	}

	public EditorSession Session => _session;

	public DockLayout Layout => _layout;

	public DockHost Dock => _dock ??
		throw new InvalidOperationException("The shell is not built.");

	/// <summary>
	/// Gets the current interface scale.
	/// </summary>
	public float UiScale => _uiScale;

	/// <summary>
	/// Gets the menu bar. Layout tests assert the chrome edges with it.
	/// </summary>
	internal MenuBar MenuBarView => _menuBar ??
		throw new InvalidOperationException("The shell is not built.");

	/// <summary>
	/// Gets the tool strip that sits under the menu bar.
	/// </summary>
	internal Widget ToolbarView => _toolbarBar ??
		throw new InvalidOperationException("The shell is not built.");

	/// <summary>
	/// Gets the status bar that hugs the bottom edge.
	/// </summary>
	internal Widget StatusBarView => _statusBar ??
		throw new InvalidOperationException("The shell is not built.");

	public event Action? ExitRequested;

	protected override void OnAttached()
	{
		// The shell owns the theme its fonts and metrics were built from.
		// Publishing it to the context keeps the fonts widgets captured
		// at construction and the context theme in one font set, which is
		// what the Vulkan renderer maps to atlas layers. Without this a
		// second font set exists that has no layers, and its glyphs sample
		// the opaque white layer instead of the letters.
		Context?.SetTheme(_theme);

		InstallShortcuts();
	}

	protected override void OnDetached()
	{
		RemoveShortcuts();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		RemoveShortcuts();

		_session.ProjectChanged -= OnProjectChanged;
		_session.SceneChanged -= OnSceneChanged;
		_session.SelectionChanged -= OnSceneChanged;
		_session.PlayStateChanged -= OnPlayStateChanged;
		_session.StatusChanged -= OnStatusChanged;
		_session.RuntimeUpdated -= OnRuntimeUpdated;
		_session.SettingsChanged -= OnSettingsChanged;

		base.Dispose();
	}

	/// <summary>
	/// Advances the editor by one frame.
	/// </summary>
	/// <param name="deltaSeconds">The real frame interval.</param>
	/// <param name="dpiScale">
	/// Physical pixels per logical unit. Viewports size their render
	/// targets from it; a headless caller leaves it at one.
	/// </param>
	public void Update(double deltaSeconds, float dpiScale = 1f)
	{
		ApplyDpiScale(dpiScale);

		_session.Update(deltaSeconds);

		for (int index = _frameTicks.Count - 1;
			index >= 0;
			index--)
		{
			IFrameTick tick = _frameTicks[index];

			if (tick.IsDisposed)
			{
				_frameTicks.RemoveAt(index);
				continue;
			}

			tick.Tick(deltaSeconds, dpiScale);
		}

		_frameSeconds += deltaSeconds;
		_frameCount++;

		if (_frameSeconds >= 0.5)
		{
			_fps = (float)(_frameCount / _frameSeconds);
			_frameSeconds = 0.0;
			_frameCount = 0;
		}
	}

	/// <summary>
	/// Applies an interface scale to the whole editor.
	/// </summary>
	/// <remarks>
	/// Fonts are rasterized at the scaled size, so text stays sharp
	/// instead of being magnified. Panel widgets capture fonts and
	/// metrics when they are created, so the dock's contents are
	/// rebuilt; authored state does not live in widgets, so nothing the
	/// user made is lost.
	/// </remarks>
	public void ApplyUiScale(float uiScale)
	{
		if (!float.IsFinite(uiScale) || uiScale <= 0f)
		{
			return;
		}

		if (MathF.Abs(uiScale - _uiScale) < 0.001f)
		{
			return;
		}

		_uiScale = uiScale;

		float dpiScale = Context?.Theme.DpiScale ?? _theme.DpiScale;
		_theme = UiTheme.CreateDark(dpiScale).WithUiScale(uiScale);

		Context?.SetTheme(_theme);

		RebuildChrome();
		_session.SetStatus($"Interface scale {uiScale * 100f:0}%");
	}

	/// <summary>
	/// Rebuilds the chrome when the display scale changes.
	/// </summary>
	/// <remarks>
	/// A monitor change replaces the number of physical pixels per
	/// logical unit, so fonts are rasterized again at the new product and
	/// every widget that captured a font is rebuilt. The new font set is
	/// published to the context, which is what makes the renderer rebuild
	/// its atlas layers instead of sampling a set it cannot map.
	/// </remarks>
	private void ApplyDpiScale(float dpiScale)
	{
		if (!float.IsFinite(dpiScale) || dpiScale <= 0f)
		{
			return;
		}

		if (MathF.Abs(dpiScale - _theme.DpiScale) < 0.001f)
		{
			return;
		}

		_theme = UiTheme.CreateDark(dpiScale).WithUiScale(_uiScale);

		Context?.SetTheme(_theme);

		RebuildChrome();
	}

	/// <summary>
	/// Restores the default workspace arrangement.
	/// </summary>
	public void ResetLayout()
	{
		_layout.ResetToEditorDefault();
		_dock?.Refresh();

		UpdateWindowMenuChecks();
		_session.SetStatus("Workspace layout reset");
	}

	private void Build()
	{
		ThemeApply();

		_menuBar = BuildMenuBar();
		_toolbarBar = BuildToolbar();
		_dock = new DockHost(_layout, CreatePanel)
		{
			FlexGrow = 1f
		};

		_statusBar = BuildStatusBar();

		StackPanel workspaceStack = new()
		{
			Spacing = 0f
		};

		workspaceStack.Add(_toolbarBar);
		workspaceStack.Add(_dock);

		Panel workspace = new()
		{
			DrawBackground = false,

			// The workspace is the flexible row between the fixed-height
			// menu bar and status bar. A stack only hands leftover space
			// to children that declare it, so without this the workspace
			// would claim every pixel the stack offers, overflow the
			// surface, and push the status bar below the window.
			FlexGrow = 1f
		};

		workspace.Add(workspaceStack);

		StackPanel root = new()
		{
			Spacing = 0f
		};

		root.Add(_menuBar);
		root.Add(workspace);
		root.Add(_statusBar);

		Add(root);

		_projectPath = new TextField(_defaultProjectPath)
		{
			Height = UiLength.Points(28f)
		};

		_projectName = new TextField("Sample Game")
		{
			Height = UiLength.Points(28f)
		};

		_startError = new Label(string.Empty)
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.Danger,
			Wrap = UiTextWrap.Wrap
		};

		_startOverlay = BuildStartOverlay();
		Add(_startOverlay);
	}

	private void ThemeApply()
	{
		float dpiScale = Context?.Theme.DpiScale ?? 1f;
		_theme = UiTheme.CreateDark(dpiScale).WithUiScale(_uiScale);
	}

	private void RebuildChrome()
	{
		for (int index = Children.Count - 1; index >= 0; index--)
		{
			Widget child = Children[index];

			Remove(child);
			child.Dispose();
		}

		_dock = null;
		_menuBar = null;
		_toolbarBar = null;
		_statusBar = null;
		_statusScene = null;
		_status = null;
		_statusRight = null;
		_startOverlay = null;
		_startError = null;
		_projectPath = null;
		_projectName = null;
		_playButton = null;
		_pauseButton = null;
		_stepButton = null;
		_stopButton = null;
		_snapButton = null;
		_spaceButton = null;
		_snapStep = null;

		_toolButtons.Clear();
		_windowMenuItems.Clear();
		_scaleMenuItems.Clear();
		_frameTicks.Clear();

		Build();

		UpdateStartVisibility();
		UpdatePlayButtons();
		UpdateStatus();
	}

	private Widget? CreatePanel(string id)
	{
		Widget? panel = id switch
		{
			"hierarchy" => new HierarchyPanel(_session),
			"scene" => new SceneViewport(_session, _viewportRenderer),
			"game" => new GameViewport(_session, _viewportRenderer),
			"inspector" => new InspectorPanel(_session),
			"project" => new ProjectPanel(_session),
			"console" => new ConsolePanel(_session),
			"profiler" => new ProfilerPanel(_session),
			_ => null
		};

		// A viewport advances its own camera once per frame, so the shell
		// drives it rather than waiting for a repaint.
		if (panel is IFrameTick tick)
		{
			_frameTicks.Add(tick);
		}

		return panel;
	}

	private MenuBar BuildMenuBar()
	{
		MenuBar bar = new();

		MenuItem file = bar.AddMenu("File");

		file.AddChild(new MenuItem("New Project…", ShowStartScreen));
		file.AddChild(new MenuItem("Open Project…", ShowStartScreen));
		file.AddChild(MenuItem.Separator);
		file.AddChild(new MenuItem(
			"New Scene",
			() => Guard("New scene", CreateNewScene)));
		file.AddChild(new MenuItem(
			"Save Scene",
			() => Guard("Save scene", _session.SaveScene))
		{
			Shortcut = "Ctrl+S"
		});
		file.AddChild(MenuItem.Separator);
		file.AddChild(new MenuItem(
			"Exit",
			() => ExitRequested?.Invoke()));

		MenuItem edit = bar.AddMenu("Edit");

		edit.AddChild(new MenuItem(
			"Undo",
			() => Guard("Undo", _session.UndoLast))
		{
			Shortcut = "Ctrl+Z"
		});

		edit.AddChild(new MenuItem(
			"Redo",
			() => Guard("Redo", _session.RedoLast))
		{
			Shortcut = "Ctrl+Y"
		});

		edit.AddChild(MenuItem.Separator);

		edit.AddChild(new MenuItem(
			"Duplicate Selection",
			() => Guard("Duplicate", () => _session.DuplicateSelection()))
		{
			Shortcut = "Ctrl+D"
		});

		edit.AddChild(new MenuItem(
			"Delete Selection",
			() => Guard("Delete", () => _session.DeleteSelection()))
		{
			Shortcut = "Del"
		});

		edit.AddChild(MenuItem.Separator);

		edit.AddChild(new MenuItem(
			"Select All",
			() => Guard("Select all", () => _session.SelectAll()))
		{
			Shortcut = "Ctrl+A"
		});

		edit.AddChild(new MenuItem(
			"Select None",
			() => Guard("Select none", _session.SelectNone))
		{
			Shortcut = "Ctrl+Shift+A"
		});

		edit.AddChild(new MenuItem(
			"Invert Selection",
			() => Guard("Invert selection", () => _session.InvertSelection()))
		{
			Shortcut = "Ctrl+I"
		});

		edit.AddChild(MenuItem.Separator);

		// Entity clipboard operations are not implemented: the editor
		// has a duplicate command, but no cross-scene clipboard yet.
		edit.AddChild(new MenuItem("Cut") { IsEnabled = false });
		edit.AddChild(new MenuItem("Copy") { IsEnabled = false });
		edit.AddChild(new MenuItem("Paste") { IsEnabled = false });

		edit.AddChild(MenuItem.Separator);
		edit.AddChild(new MenuItem("Preferences…", ShowPreferences));

		MenuItem assets = bar.AddMenu("Assets");

		assets.AddChild(new MenuItem(
			"Refresh Asset Catalog",
			() => Guard("Refresh assets", () =>
			{
				_session.Project?.AssetCatalog.Refresh();
				_session.SetStatus("Asset catalog refreshed");
			})));

		assets.AddChild(new MenuItem(
			"New Scene",
			() => Guard("New scene", CreateNewScene)));

		MenuItem create = bar.AddMenu("GameObject");

		create.AddChild(new MenuItem(
			"Create Empty",
			() => Guard("Create object", () =>
				_session.CreateEntity("GameObject"))));

		create.AddChild(MenuItem.Separator);

		create.AddChild(new MenuItem(
			"Create Cube",
			() => CreatePrimitive("Cube", "builtin:cube", withSpin: false)));

		create.AddChild(new MenuItem(
			"Create Sphere",
			() => CreatePrimitive("Sphere", "builtin:sphere", withSpin: false)));

		create.AddChild(new MenuItem(
			"Create Plane",
			() => CreatePrimitive("Plane", "builtin:plane", withSpin: false)));

		create.AddChild(new MenuItem("Create Camera", CreateCamera));

		create.AddChild(MenuItem.Separator);

		// There is no light component in authored data yet, so the
		// command is present but unavailable rather than decorative.
		create.AddChild(new MenuItem("Create Light") { IsEnabled = false });

		MenuItem component = bar.AddMenu("Component");

		foreach (EditorComponentDescriptor descriptor in
			ComponentCatalog.Default.All)
		{
			EditorComponentDescriptor captured = descriptor;

			component.AddChild(new MenuItem(
				$"Add {descriptor.DisplayName}",
				() => Guard("Add component", () =>
				{
					if (_session.ActiveEntityId is not Guid id)
					{
						throw new InvalidOperationException(
							"Select an object first.");
					}

					_session.AddComponent(id, captured.TypeId);
				})));
		}

		MenuItem window = bar.AddMenu("Window");

		foreach (PanelTab tab in Enum.GetValues<PanelTab>())
		{
			PanelTab captured = tab;
			string panelId = PanelId(captured);
			MenuItem item = new(
				_layout.Panels[panelId].Title,
				() => TogglePanel(captured))
			{
				IsChecked = _layout.IsOpen(panelId)
			};

			_windowMenuItems[captured] = item;
			window.AddChild(item);
		}

		window.AddChild(MenuItem.Separator);
		window.AddChild(new MenuItem("Reset Layout", ResetLayout));
		window.AddChild(MenuItem.Separator);

		foreach (float scale in s_uiScales)
		{
			float captured = scale;

			MenuItem item = new(
				$"Interface Scale {captured * 100f:0}%",
				() => ApplyUiScale(captured))
			{
				IsChecked = MathF.Abs(captured - _uiScale) < 0.001f
			};

			_scaleMenuItems[captured] = item;
			window.AddChild(item);
		}

		window.AddChild(MenuItem.Separator);
		window.AddChild(new MenuItem("Start Screen", ShowStartScreen));

		MenuItem help = bar.AddMenu("Help");

		help.AddChild(new MenuItem(
			"About VOLT",
			() => _session.Log(
				"VOLT editor — a native C# game engine. The interface " +
				"and the editor are built on Volt.Ui; the viewports are " +
				"rendered with Vulkan.")));

		help.AddChild(new MenuItem(
			"Show Documentation",
			() => _session.Log(
				"See docs/editor-guide.md and docs/ui-framework.md " +
				"in the repository.")));

		help.AddChild(new MenuItem(
			"Keyboard Shortcuts",
			() => _session.Log(
				"Q/W/E/R select, Ctrl+S save, Ctrl+Z undo, Ctrl+Y redo, " +
				"Ctrl+D duplicate, Del delete. In a viewport: hold the " +
				"right mouse button to look, WASD/E/Q to fly, Shift to " +
				"sprint, middle drag to pan, wheel to dolly, F to frame.")));

		return bar;
	}

	private Panel BuildToolbar()
	{
		Panel bar = new()
		{
			Height = UiLength.Points(_theme.Metrics.ToolbarHeight),
			DrawBackground = true,
			Background = _theme.Palette.Chrome,
			DrawBorder = true,
			BorderColor = _theme.Palette.ChromeBorder
		};

		StackPanel toolbar = new(UiOrientation.Horizontal)
		{
			Spacing = 6f,
			Padding = new UiThickness(10f, 5f, 10f, 5f)
		};

		toolbar.Add(BuildBrand());

		toolbar.Add(new Separator(UiOrientation.Vertical)
		{
			Margin = new UiThickness(2f, 2f)
		});

		toolbar.Add(BuildToolGroup());
		toolbar.Add(new Separator(UiOrientation.Vertical)
		{
			Margin = new UiThickness(2f, 2f)
		});
		toolbar.Add(BuildTransportGroup());
		toolbar.Add(new Spacer());
		toolbar.Add(BuildContextGroup());

		bar.Add(toolbar);
		return bar;
	}

	private Widget BuildBrand()
	{
		StackPanel brand = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		brand.Add(new IconView(UiIcon.Layers)
		{
			Width = UiLength.Points(20f),
			Height = UiLength.Points(20f),
			VerticalAlignment = UiAlignment.Center
		});

		brand.Add(new Label("VOLT")
		{
			Font = Context?.Theme.Fonts.Large,
			VerticalAlignment = UiAlignment.Center
		});

		return brand;
	}

	private Widget BuildToolGroup()
	{
		// A panel fills the space it is given, so a grouped cluster in a
		// horizontal strip must declare its width. Without this the
		// group claims the whole row and pushes the rest of the toolbar
		// past the edge of the window.
		const float buttonWidth = 30f;
		const float buttonSpacing = 2f;
		const float groupPadding = 3f;
		int toolCount = Enum.GetValues<EditorTool>().Length;

		Panel group = new()
		{
			Width = UiLength.Points(
				(toolCount * buttonWidth) +
				((toolCount - 1) * buttonSpacing) +
				(groupPadding * 2f) +
				2f),
			DrawBackground = true,
			Background = _theme.Palette.Well,
			DrawBorder = true,
			BorderColor = _theme.Palette.ChromeBorder,
			Padding = new UiThickness(groupPadding),
			VerticalAlignment = UiAlignment.Center
		};

		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 2f
		};

		foreach (EditorTool tool in Enum.GetValues<EditorTool>())
		{
			EditorTool captured = tool;

			IconButton button = new(
				ToolIcon(tool),
				$"{ToolLabel(tool)} tool  ({ToolShortcut(tool)})")
			{
				IsActive = tool == _session.Tool,
				Width = UiLength.Points(buttonWidth),
				Height = UiLength.Points(28f)
			};

			button.Clicked += _ => SelectTool(captured);

			_toolButtons[tool] = button;
			row.Add(button);
		}

		group.Add(row);
		return group;
	}

	private Widget BuildTransportGroup()
	{
		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 2f
		};

		_playButton = CreateTransport(
			UiIcon.Play,
			"Play the scene",
			_theme.Palette.Success,
			Play);

		_pauseButton = CreateTransport(
			UiIcon.Pause,
			"Pause the simulation",
			null,
			Pause);

		_stepButton = CreateTransport(
			UiIcon.Step,
			"Advance one fixed step",
			null,
			Step);

		_stopButton = CreateTransport(
			UiIcon.Stop,
			"Stop and restore the authored scene",
			_theme.Palette.Danger,
			Stop);

		row.Add(_playButton);
		row.Add(_pauseButton);
		row.Add(_stepButton);
		row.Add(_stopButton);

		return row;
	}

	private IconButton CreateTransport(
		UiIcon icon,
		string tooltip,
		UiColor? color,
		Action action)
	{
		IconButton button = new(icon, tooltip)
		{
			Width = UiLength.Points(32f),
			Height = UiLength.Points(28f),
			IconColor = color
		};

		button.Clicked += _ => action();
		return button;
	}

	private Widget BuildContextGroup()
	{
		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_spaceButton = new ToolButton(
			_session.LocalTransform ? UiIcon.Local : UiIcon.World,
			_session.LocalTransform ? "Local" : "World")
		{
			TooltipText = "Switch between local and world gizmo space",
			IsActive = _session.LocalTransform,
			MinHeight = 28f
		};

		_spaceButton.Clicked += _ => ToggleSpace();

		_snapButton = new IconButton(UiIcon.Snap, "Toggle grid snapping")
		{
			IsActive = _session.GridSnapping,
			Width = UiLength.Points(30f),
			Height = UiLength.Points(28f)
		};

		_snapButton.Clicked += _ => ToggleSnap();

		_snapStep = new NumericField(_session.SnapStep)
		{
			Width = UiLength.Points(64f),
			MinWidth = 52f,
			Height = UiLength.Points(28f),
			Format = "0.###",
			Minimum = 0.01f,
			Maximum = 10f,
			Step = 0.25f,
			TooltipText = "Grid snap increment"
		};

		_snapStep.ValueChanged += _ =>
			_session.SnapStep = MathF.Max(0.01f, _snapStep.Value);

		row.Add(_spaceButton);
		row.Add(_snapButton);
		row.Add(_snapStep);

		return row;
	}

	private Panel BuildStatusBar()
	{
		Panel bar = new()
		{
			Height = UiLength.Points(_theme.Metrics.StatusBarHeight),
			DrawBackground = true,
			Background = _theme.Palette.Chrome,
			DrawBorder = true,
			BorderColor = _theme.Palette.ChromeBorder,
			Padding = new UiThickness(10f, 0f)
		};

		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 10f
		};

		_statusScene = new Label("No scene")
		{
			TextColor = _theme.Palette.Text,
			VerticalAlignment = UiAlignment.Center
		};

		_status = new Label("Ready")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted,
			FlexGrow = 1f,
			VerticalAlignment = UiAlignment.Center
		};

		_statusRight = new Label(string.Empty)
		{
			Font = _theme.Fonts.MonoSmall,
			TextColor = _theme.Palette.TextMuted,
			HorizontalAlignment = UiAlignment.End,
			VerticalAlignment = UiAlignment.Center
		};

		row.Add(_statusScene);
		row.Add(new Separator(UiOrientation.Vertical)
		{
			Margin = new UiThickness(0f, 5f)
		});
		row.Add(_status);
		row.Add(_statusRight);

		bar.Add(row);
		return bar;
	}

	private Panel BuildStartOverlay()
	{
		Panel overlay = new()
		{
			Background = _theme.Palette.WindowBackground,
			DrawBorder = false
		};

		Panel card = new()
		{
			Width = UiLength.Points(500f),
			Height = UiLength.Points(380f),
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center,
			DrawBackground = true,
			Background = _theme.Palette.Surface,
			DrawBorder = true,
			BorderColor = _theme.Palette.Border,
			Padding = new UiThickness(24f)
		};

		TextField projectPath = _projectPath!;
		TextField projectName = _projectName!;
		Label startError = _startError!;

		StackPanel content = new()
		{
			Spacing = 8f
		};

		StackPanel titleRow = new(UiOrientation.Horizontal)
		{
			Spacing = 10f
		};

		titleRow.Add(new IconView(UiIcon.Layers)
		{
			Width = UiLength.Points(34f),
			Height = UiLength.Points(34f),
			VerticalAlignment = UiAlignment.Center
		});

		StackPanel titles = new()
		{
			Spacing = 2f,
			FlexGrow = 1f
		};

		titles.Add(new Label("VOLT Editor")
		{
			Font = _theme.Fonts.Title
		});

		titles.Add(new Label(
			"A native C# game engine and editor built on Volt.Ui and Vulkan.")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted,
			Wrap = UiTextWrap.Wrap
		});

		titleRow.Add(titles);
		content.Add(titleRow);
		content.Add(new Separator());
		content.Add(new Label("Project folder")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted
		});
		content.Add(projectPath);
		content.Add(new Label("Project name")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted
		});
		content.Add(projectName);

		StackPanel buttons = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		Button create = new("Create Project")
		{
			IsPrimary = true,
			MinHeight = 28f
		};

		create.Clicked += _ => CreateProjectFromStart();

		Button open = new("Open Project")
		{
			MinHeight = 28f
		};

		open.Clicked += _ => OpenProjectFromStart();

		buttons.Add(create);
		buttons.Add(open);
		content.Add(buttons);
		content.Add(startError);

		content.Add(new Label(
			"A new project contains a scene with a camera, a cube, " +
			"and an editable spin behaviour.")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		card.Add(content);
		overlay.Add(card);

		return overlay;
	}

	private void InstallShortcuts()
	{
		if (_shortcutsInstalled || Context is not UiContext context)
		{
			return;
		}

		_shortcutsInstalled = true;

		AddShortcut(new UiShortcut(
			UiKey.S,
			UiKeyModifiers.Control,
			() => Guard("Save scene", _session.SaveScene),
			() => _session.Scene is not null));

		AddShortcut(new UiShortcut(
			UiKey.Z,
			UiKeyModifiers.Control,
			() => Guard("Undo", _session.UndoLast),
			() => _session.Undo.CanUndo));

		AddShortcut(new UiShortcut(
			UiKey.Z,
			UiKeyModifiers.Control | UiKeyModifiers.Shift,
			() => Guard("Redo", _session.RedoLast),
			() => _session.Undo.CanRedo));

		AddShortcut(new UiShortcut(
			UiKey.Y,
			UiKeyModifiers.Control,
			() => Guard("Redo", _session.RedoLast),
			() => _session.Undo.CanRedo));

		AddShortcut(new UiShortcut(
			UiKey.D,
			UiKeyModifiers.Control,
			() => Guard("Duplicate", () => _session.DuplicateSelection()),
			() => _session.Selection.Count > 0));

		AddShortcut(new UiShortcut(
			UiKey.Delete,
			UiKeyModifiers.None,
			() => Guard("Delete", () => _session.DeleteSelection()),
			() => _session.Selection.Count > 0));

		// Selection commands follow the familiar editor layout. A
		// focused text field gets these keys first, so select-all still
		// means select-all inside a name field.
		AddShortcut(new UiShortcut(
			UiKey.A,
			UiKeyModifiers.Control,
			() => Guard("Select all", () => _session.SelectAll()),
			() => _session.Scene is not null));

		AddShortcut(new UiShortcut(
			UiKey.A,
			UiKeyModifiers.Control | UiKeyModifiers.Shift,
			() => Guard("Select none", _session.SelectNone),
			() => _session.Selection.Count > 0));

		AddShortcut(new UiShortcut(
			UiKey.I,
			UiKeyModifiers.Control,
			() => Guard("Invert selection", () => _session.InvertSelection()),
			() => _session.Scene is not null));

		// Tool hotkeys follow the familiar editor layout. They stand
		// down while a viewport is navigating, because the same letters
		// fly the scene camera.
		AddShortcut(new UiShortcut(
			UiKey.Q,
			UiKeyModifiers.None,
			() => SelectTool(EditorTool.Select),
			CanUseToolHotkey));

		AddShortcut(new UiShortcut(
			UiKey.W,
			UiKeyModifiers.None,
			() => SelectTool(EditorTool.Translate),
			CanUseToolHotkey));

		AddShortcut(new UiShortcut(
			UiKey.E,
			UiKeyModifiers.None,
			() => SelectTool(EditorTool.Rotate),
			CanUseToolHotkey));

		AddShortcut(new UiShortcut(
			UiKey.R,
			UiKeyModifiers.None,
			() => SelectTool(EditorTool.Scale),
			CanUseToolHotkey));
	}

	private bool CanUseToolHotkey()
	{
		return !_session.ViewportNavigating;
	}

	/// <summary>
	/// Selects a tool and keeps the toolbar in step with it.
	/// </summary>
	/// <summary>
	/// Selects a tool.
	/// </summary>
	/// <remarks>
	/// Changing the tool raises the session's settings event, which is
	/// what refreshes the toolbar, so this only makes the change and
	/// says so. The same holds for the space and snap toggles below.
	/// </remarks>
	private void SelectTool(EditorTool tool)
	{
		if (_session.Tool == tool)
		{
			return;
		}

		_session.Tool = tool;
		_session.SetStatus($"{ToolLabel(tool)} tool");
	}

	private void ToggleSpace()
	{
		_session.LocalTransform = !_session.LocalTransform;

		_session.SetStatus(
			_session.LocalTransform ? "Local gizmo space" : "World gizmo space");
	}

	private void ToggleSnap()
	{
		_session.GridSnapping = !_session.GridSnapping;

		_session.SetStatus(
			_session.GridSnapping
				? $"Grid snapping on ({_session.SnapStep:0.###})"
				: "Grid snapping off");
	}

	/// <summary>
	/// Brings the toolbar back in step with the session's interaction
	/// settings, whichever surface changed them.
	/// </summary>
	/// <remarks>
	/// The toolbar and a viewport's floating chrome edit the same tool,
	/// gizmo space, and snap settings, so both listen instead of each
	/// assuming it is the only one the user touches.
	/// </remarks>
	private void OnSettingsChanged(EditorSession session)
	{
		foreach (KeyValuePair<EditorTool, IconButton> pair in _toolButtons)
		{
			pair.Value.IsActive = pair.Key == session.Tool;
		}

		if (_spaceButton is ToolButton space)
		{
			space.IsActive = session.LocalTransform;
			space.Icon = session.LocalTransform ? UiIcon.Local : UiIcon.World;
			space.Text = session.LocalTransform ? "Local" : "World";
		}

		if (_snapButton is IconButton snap)
		{
			snap.IsActive = session.GridSnapping;
		}

		if (_snapStep is NumericField step &&
			MathF.Abs(step.Value - session.SnapStep) > 0.0001f)
		{
			// Notifying would write the same value straight back; the
			// field only has to show what the session holds.
			step.SetValue(session.SnapStep, notify: false);
		}
	}

	private void RemoveShortcuts()
	{
		if (!_shortcutsInstalled || Context is not UiContext context)
		{
			return;
		}

		_shortcutsInstalled = false;

		foreach (UiShortcut shortcut in _installedShortcuts)
		{
			context.Shortcuts.Remove(shortcut);
		}

		_installedShortcuts.Clear();
	}

	private void AddShortcut(UiShortcut shortcut)
	{
		if (Context is UiContext context)
		{
			context.Shortcuts.Add(shortcut);
			_installedShortcuts.Add(shortcut);
		}
	}

	private void Play()
	{
		Guard("Play", _session.StartPlay);
	}

	private void Pause()
	{
		_session.Pause();
	}

	private void Step()
	{
		_session.Step();
	}

	private void Stop()
	{
		_session.Stop();
	}

	private void CreatePrimitive(
		string name,
		string mesh,
		bool withSpin)
	{
		Guard($"Create {name}", () =>
		{
			SceneDocument scene = _session.Scene ??
				throw new InvalidOperationException("No scene is open.");

			AddEntityCommand add = new(scene, name);

			AddComponentCommand renderer = new(
				scene,
				add.Entity.Id,
				new MeshRendererData
				{
					Mesh = mesh,
					Material = "builtin:default"
				});

			if (!withSpin)
			{
				_session.Undo.Execute(new CompositeCommand(
					$"Create {name}",
					[add, renderer]));

				_session.Select(add.Entity.Id);
				return;
			}

			AddComponentCommand spin = new(
				scene,
				add.Entity.Id,
				new SpinBehaviourData
				{
					Axis = System.Numerics.Vector3.UnitY,
					DegreesPerSecond = 45f
				});

			_session.Undo.Execute(new CompositeCommand(
				$"Create {name}",
				[add, renderer, spin]));

			_session.Select(add.Entity.Id);
		});
	}

	private void CreateCamera()
	{
		Guard("Create Camera", () =>
		{
			SceneDocument scene = _session.Scene ??
				throw new InvalidOperationException("No scene is open.");

			AddEntityCommand add = new(scene, "Camera");

			AddComponentCommand camera = new(
				scene,
				add.Entity.Id,
				new CameraData());

			SetTransformCommand transform = new(
				scene,
				add.Entity.Id,
				new TransformData
				{
					Position = new System.Numerics.Vector3(0f, 1.5f, -6f)
				});

			_session.Undo.Execute(new CompositeCommand(
				"Create Camera",
				[add, camera, transform]));

			_session.Select(add.Entity.Id);
		});
	}

	private void CreateNewScene()
	{
		EditorProject project = _session.Project ??
			throw new InvalidOperationException("No project is open.");

		int index = 1;
		string relative;

		do
		{
			relative = $"Scenes/Scene{index}.volt.scene.json";
			index++;
		}
		while (File.Exists(project.ResolvePath(relative)));

		_session.CreateScene(
			relative,
			$"Scene {index - 1}");
	}

	private void ShowPreferences()
	{
		if (Context is not UiContext context)
		{
			return;
		}

		UiTheme theme = context.Theme;

		Panel scrim = new()
		{
			Background = UiColor.FromArgb(0xAA0B0E13),
			DrawBorder = false
		};

		Panel card = new()
		{
			Width = UiLength.Points(380f),
			Height = UiLength.Points(210f),
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center,
			DrawBackground = true,
			Background = theme.Palette.SurfaceRaised,
			DrawBorder = true,
			BorderColor = theme.Palette.BorderStrong,
			Padding = new UiThickness(20f)
		};

		StackPanel content = new()
		{
			Spacing = 10f
		};

		content.Add(new Label("Preferences")
		{
			Font = theme.Fonts.Heading
		});

		content.Add(new Separator());

		content.Add(new Label("Interface scale")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted
		});

		StackPanel scales = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		foreach (float scale in s_uiScales)
		{
			float captured = scale;

			Button button = new($"{captured * 100f:0}%")
			{
				IsSelected = MathF.Abs(captured - _uiScale) < 0.001f,
				MinHeight = 28f
			};

			button.Clicked += _ =>
			{
				context.CloseModal();
				ApplyUiScale(captured);
			};

			scales.Add(button);
		}

		content.Add(scales);

		content.Add(new Label(
			"Scaling rebuilds panel widgets so text is rasterized at " +
			"the new size. Project, scene, and workspace state are kept.")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap,
			FlexGrow = 1f
		});

		Button close = new("Close")
		{
			HorizontalAlignment = UiAlignment.End,
			MinHeight = 28f
		};

		close.Clicked += _ => context.CloseModal();
		content.Add(close);

		card.Add(content);
		scrim.Add(card);

		context.ShowModal(scrim);
	}

	private void TogglePanel(PanelTab tab)
	{
		string id = PanelId(tab);

		if (_layout.IsOpen(id))
		{
			_layout.ClosePanel(id);
		}

		else
		{
			_layout.OpenPanel(id);
		}

		_dock?.Refresh();
		UpdateWindowMenuChecks();
	}

	private void UpdateWindowMenuChecks()
	{
		foreach (KeyValuePair<PanelTab, MenuItem> pair in _windowMenuItems)
		{
			pair.Value.IsChecked = _layout.IsOpen(PanelId(pair.Key));
		}
	}

	private void ShowStartScreen()
	{
		if (_startError is Label error)
		{
			error.Text = string.Empty;
		}

		if (_startOverlay is Panel overlay)
		{
			overlay.IsVisible = true;
		}
	}

	private void CreateProjectFromStart()
	{
		if (_projectPath is not TextField path ||
			_projectName is not TextField name)
		{
			return;
		}

		try
		{
			_session.NewProject(path.Text, name.Text);
			HideStartScreen();
		}

		catch (Exception exception)
		{
			ReportStartError(exception);
		}
	}

	private void OpenProjectFromStart()
	{
		if (_projectPath is not TextField path)
		{
			return;
		}

		try
		{
			_session.OpenProject(path.Text);
			HideStartScreen();
		}

		catch (Exception exception)
		{
			ReportStartError(exception);
		}
	}

	private void ReportStartError(Exception exception)
	{
		if (_startError is Label error)
		{
			error.Text = exception.Message;
		}

		_session.Log(exception.Message, EditorLogLevel.Error);
	}

	private void HideStartScreen()
	{
		if (_startOverlay is Panel overlay)
		{
			overlay.IsVisible = false;
		}

		if (_startError is Label error)
		{
			error.Text = string.Empty;
		}
	}

	private void UpdateStartVisibility()
	{
		if (_startOverlay is Panel overlay)
		{
			overlay.IsVisible = _session.Project is null;
		}
	}

	private void OnProjectChanged(EditorSession session)
	{
		UpdateStartVisibility();
		UpdateStatus();
	}

	private void OnSceneChanged(EditorSession session)
	{
		UpdateStatus();
	}

	private void OnPlayStateChanged(EditorSession session)
	{
		UpdatePlayButtons();
		UpdateStatus();
	}

	private void OnStatusChanged(EditorSession session)
	{
		UpdateStatus();
	}

	private void OnRuntimeUpdated(EditorSession session)
	{
		UpdateStatus();
	}

	private void UpdatePlayButtons()
	{
		PlayState state = _session.PlayState;
		bool stopped = state == PlayState.Stopped;
		bool playing = state == PlayState.Playing;
		bool paused = state == PlayState.Paused;

		if (_playButton is IconButton play)
		{
			play.IsEnabled = stopped;
			play.IsActive = playing;
		}

		if (_pauseButton is IconButton pause)
		{
			pause.IsEnabled = playing;
			pause.IsActive = paused;
		}

		if (_stepButton is IconButton step)
		{
			step.IsEnabled = paused;
		}

		if (_stopButton is IconButton stop)
		{
			stop.IsEnabled = playing || paused;
		}
	}

	private void UpdateStatus()
	{
		if (_statusScene is Label scene)
		{
			string name = _session.Scene?.Name ?? "No scene";
			string dirty = _session.IsSceneDirty ? " *" : string.Empty;

			scene.Text = $"{name}{dirty}";
		}

		if (_status is Label status)
		{
			status.Text = _session.Status;
		}

		if (_statusRight is Label right)
		{
			RuntimeScene? runtime = _session.Play?.Runtime;
			int objects = runtime?.World.Count ?? _session.Scene?.EntityCount ?? 0;

			right.Text = string.Join(
				"  ·  ",
				_session.PlayState.ToString(),
				$"{objects.ToString(CultureInfo.InvariantCulture)} objects",
				$"{_fps:0} fps");
		}
	}

	private void Guard(string action, Action body)
	{
		try
		{
			body();
		}

		catch (Exception exception)
		{
			_session.Log($"{action}: {exception.Message}", EditorLogLevel.Error);
			_session.SetStatus($"{action} failed");
		}
	}

	private static string PanelId(PanelTab tab)
	{
		return tab switch
		{
			PanelTab.Hierarchy => "hierarchy",
			PanelTab.Scene => "scene",
			PanelTab.Game => "game",
			PanelTab.Inspector => "inspector",
			PanelTab.Project => "project",
			PanelTab.Console => "console",
			PanelTab.Profiler => "profiler",
			_ => throw new ArgumentOutOfRangeException(nameof(tab))
		};
	}

	// The toolbar and a viewport's floating chrome present the same
	// tools, so the names, icons, and hotkeys come from one table.

	private static UiIcon ToolIcon(EditorTool tool)
	{
		return EditorToolInfo.Icon(tool);
	}

	private static string ToolLabel(EditorTool tool)
	{
		return EditorToolInfo.Label(tool);
	}

	private static string ToolShortcut(EditorTool tool)
	{
		return EditorToolInfo.Shortcut(tool);
	}
}
