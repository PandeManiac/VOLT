using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor;

/// <summary>
/// The complete editor chrome, built exclusively from Volt.Ui widgets.
/// </summary>
public sealed class EditorShell : Panel
{
	private readonly EditorSession _session;
	private readonly DockLayout _layout;
	private readonly DockHost _dock;
	private readonly MenuBar _menuBar;
	private readonly StackPanel _toolbar;
	private readonly StackPanel _workspaceStack;
	private readonly Panel _workspace;
	private readonly Panel _startOverlay;
	private readonly TextField _projectPath;
	private readonly TextField _projectName;
	private readonly Label _startError;
	private readonly Label _status;
	private readonly Label _statusRight;

	private readonly Dictionary<EditorTool, ToggleButton> _toolButtons = [];
	private readonly Dictionary<PanelTab, MenuItem> _windowMenuItems = [];
	private readonly List<UiShortcut> _installedShortcuts = [];
	private readonly UiTheme _theme = UiTheme.CreateDark();

	private Button? _playButton;
	private Button? _pauseButton;
	private Button? _stepButton;
	private Button? _stopButton;

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

	public EditorShell(
		EditorSession session,
		string? defaultProjectFolder = null,
		DockLayout? layout = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		ClipToBounds = true;
		Background = _theme.Palette.WindowBackground;

		_layout = layout ?? DockLayout.CreateEditorDefault();
		_dock = new DockHost(_layout, CreatePanel)
		{
			FlexGrow = 1f
		};

		_menuBar = BuildMenuBar();

		_workspaceStack = new StackPanel
		{
			Spacing = 0f
		};

		_toolbar = BuildToolbar();
		_workspaceStack.Add(_toolbar);
		_workspaceStack.Add(_dock);

		_workspace = new Panel
		{
			DrawBackground = false
		};

		_workspace.Add(_workspaceStack);

		_status = new Label("Ready")
		{
			Font = _theme.Fonts.Small,
			FlexGrow = 1f,
			VerticalAlignment = UiAlignment.Center
		};

		_statusRight = new Label(string.Empty)
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted,
			HorizontalAlignment = UiAlignment.End,
			VerticalAlignment = UiAlignment.Center
		};

		Panel statusBar = new()
		{
			Height = UiLength.Points(26f),
			DrawBackground = true
		};

		StackPanel statusRow = new(UiOrientation.Horizontal)
		{
			Spacing = 8f,
			Padding = new UiThickness(8f, 0f)
		};

		statusRow.Add(_status);
		statusRow.Add(_statusRight);
		statusBar.Add(statusRow);

		StackPanel root = new()
		{
			Spacing = 0f
		};

		root.Add(_menuBar);
		root.Add(_workspace);
		root.Add(statusBar);

		Add(root);

		string defaultPath = defaultProjectFolder ??
			Path.Combine(
				Environment.CurrentDirectory,
				"VoltGame");

		_projectPath = new TextField(defaultPath)
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
			Wrap = Volt.Ui.Text.UiTextWrap.Wrap
		};

		_startOverlay = BuildStartOverlay();
		Add(_startOverlay);

		_session.ProjectChanged += OnProjectChanged;
		_session.SceneChanged += OnSceneChanged;
		_session.SelectionChanged += OnSceneChanged;
		_session.PlayStateChanged += OnPlayStateChanged;
		_session.StatusChanged += OnStatusChanged;
		_session.RuntimeUpdated += OnRuntimeUpdated;

		UpdateStartVisibility();
		UpdatePlayButtons();
		UpdateStatus();
	}

	public EditorSession Session => _session;

	public DockLayout Layout => _layout;

	public DockHost Dock => _dock;

	public event Action? ExitRequested;

	protected override void OnAttached()
	{
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

		base.Dispose();
	}

	public void Update(double deltaSeconds)
	{
		_session.Update(deltaSeconds);
	}

	private Widget? CreatePanel(string id)
	{
		return id switch
		{
			"hierarchy" => new HierarchyPanel(_session),
			"scene" => new SceneViewport(_session),
			"game" => new GameViewport(_session),
			"inspector" => new InspectorPanel(_session),
			"project" => new ProjectPanel(_session),
			"console" => new ConsolePanel(_session),
			"profiler" => new ProfilerPanel(_session),
			_ => null
		};
	}

	private MenuBar BuildMenuBar()
	{
		MenuBar bar = new();

		MenuItem file = bar.AddMenu("File");

		file.AddChild(new MenuItem(
			"Show Start Screen",
			ShowStartScreen));

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
			"Delete Selection",
			() => Guard("Delete", () => _session.DeleteActiveEntity()))
		{
			Shortcut = "Del"
		});

		edit.AddChild(new MenuItem(
			"Duplicate Selection",
			() => Guard("Duplicate", () => _session.DuplicateActiveEntity())));

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
			() => Guard("Create entity", () =>
				_session.CreateEntity("GameObject"))));

		create.AddChild(new MenuItem(
			"Create Cube",
			() => Guard("Create cube", CreateCube)));

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
							"Select an entity first.");
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
				() => TogglePanel(captured));
			item.IsChecked = _layout.IsOpen(panelId);
			_windowMenuItems[captured] = item;
			window.AddChild(item);
		}

		MenuItem help = bar.AddMenu("Help");

		help.AddChild(new MenuItem(
			"About VOLT",
			() => _session.Log(
				"VOLT editor — native C# game engine. " +
				"UI and editor are built on Volt.Ui.")));

		help.AddChild(new MenuItem(
			"Show Documentation",
			() => _session.Log(
				"See docs/ui-framework.md and docs/editor-guide.md " +
				"in the repository.")));

		return bar;
	}

	private StackPanel BuildToolbar()
	{
		StackPanel toolbar = new(UiOrientation.Horizontal)
		{
			Spacing = 4f,
			Padding = new UiThickness(8f, 4f),
			Height = UiLength.Points(40f)
		};

		foreach (EditorTool tool in Enum.GetValues<EditorTool>())
		{
			ToggleButton button = new(ToolLabel(tool))
			{
				IsChecked = tool == _session.Tool,
				TooltipText = ToolTooltip(tool),
				MinWidth = 54f,
				MinHeight = 26f
			};

			EditorTool captured = tool;

			button.Toggled += _ => OnToolToggled(captured);

			_toolButtons[tool] = button;
			toolbar.Add(button);
		}

		toolbar.Add(new Separator(UiOrientation.Vertical));

		_playButton = CreateToolbarButton("Play", "Run the authored scene", Play);
		_pauseButton = CreateToolbarButton("Pause", "Pause gameplay", Pause);
		_stepButton = CreateToolbarButton("Step", "Advance one fixed tick", Step);
		_stopButton = CreateToolbarButton("Stop", "Stop and restore authored state", Stop);

		toolbar.Add(_playButton);
		toolbar.Add(_pauseButton);
		toolbar.Add(_stepButton);
		toolbar.Add(_stopButton);

		toolbar.Add(new Separator(UiOrientation.Vertical));

		ToggleButton local = new("World") { MinWidth = 58f, MinHeight = 26f };
		local.IsChecked = !_session.LocalTransform;
		local.TooltipText = "Toggle local/world transform mode";

		local.Toggled += _ =>
		{
			_session.LocalTransform = !local.IsChecked;
			local.Text = _session.LocalTransform ? "Local" : "World";
		};

		local.Text = _session.LocalTransform ? "Local" : "World";
		toolbar.Add(local);

		ToggleButton snap = new("Snap")
		{
			MinWidth = 52f,
			MinHeight = 26f,
			IsChecked = _session.GridSnapping
		};

		snap.TooltipText = "Toggle grid snapping";
		snap.Toggled += _ => _session.GridSnapping = snap.IsChecked;
		toolbar.Add(snap);

		NumericField snapStep = new(_session.SnapStep)
		{
			Width = UiLength.Points(64f),
			MinWidth = 52f,
			Height = UiLength.Points(26f),
			Format = "0.###",
			Minimum = 0.01f,
			Maximum = 10f,
			Step = 0.25f
		};

		snapStep.ValueChanged += _ =>
			_session.SnapStep = MathF.Max(0.01f, snapStep.Value);

		toolbar.Add(snapStep);

		return toolbar;
	}

	private Button CreateToolbarButton(
		string text,
		string tooltip,
		Action action)
	{
		Button button = new(text)
		{
			TooltipText = tooltip,
			MinHeight = 26f
		};

		button.Clicked += _ => Guard(text, action);
		return button;
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
			Width = UiLength.Points(480f),
			Height = UiLength.Points(350f),
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center,
			DrawBackground = true,
			DrawBorder = true,
			Padding = new UiThickness(22f)
		};

		StackPanel content = new()
		{
			Spacing = 8f
		};

		Label title = new("VOLT")
		{
			Font = _theme.Fonts.Heading,
			TextColor = _theme.Palette.Accent
		};

		Label subtitle = new(
			"A native C# game engine and editor built on Volt.Ui.")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted,
			Wrap = Volt.Ui.Text.UiTextWrap.Wrap
		};

		content.Add(title);
		content.Add(subtitle);
		content.Add(new Label("Project folder")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted
		});
		content.Add(_projectPath);
		content.Add(new Label("Project name")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextMuted
		});
		content.Add(_projectName);

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
		content.Add(_startError);

		content.Add(new Label(
			"The generated project contains a scene, camera, cube, " +
			"and an editable spin behaviour.")
		{
			Font = _theme.Fonts.Small,
			TextColor = _theme.Palette.TextDisabled,
			Wrap = Volt.Ui.Text.UiTextWrap.Wrap
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
			UiKey.Delete,
			UiKeyModifiers.None,
			() => Guard("Delete", () => _session.DeleteActiveEntity()),
			() => _session.ActiveEntityId is not null));
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

	private void OnToolToggled(EditorTool tool)
	{
		if (!_toolButtons.TryGetValue(tool, out ToggleButton? button))
		{
			return;
		}

		if (!button.IsChecked)
		{
			button.IsChecked = _session.Tool == tool;
			return;
		}

		_session.Tool = tool;

		foreach (KeyValuePair<EditorTool, ToggleButton> pair in _toolButtons)
		{
			if (pair.Key != tool)
			{
				pair.Value.IsChecked = false;
			}
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

	private void CreateCube()
	{
		SceneDocument scene = _session.Scene ??
			throw new InvalidOperationException("No scene is open.");

		AddEntityCommand add = new(scene, "Cube");

		AddComponentCommand renderer = new(
			scene,
			add.Entity.Id,
			new MeshRendererData
			{
				Mesh = "builtin:cube",
				Material = "builtin:default"
			});

		AddComponentCommand spin = new(
			scene,
			add.Entity.Id,
			new SpinBehaviourData
			{
				Axis = System.Numerics.Vector3.UnitY,
				DegreesPerSecond = 45f
			});

		_session.Undo.Execute(new CompositeCommand(
			"Create Cube",
			[add, renderer, spin]));

		_session.Select(add.Entity.Id);
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

		_dock.Refresh();

		if (_windowMenuItems.TryGetValue(tab, out MenuItem? item))
		{
			item.IsChecked = _layout.IsOpen(id);
		}
	}

	private void ShowStartScreen()
	{
		_startError.Text = string.Empty;
		_startOverlay.IsVisible = true;
	}

	private void CreateProjectFromStart()
	{
		try
		{
			_session.NewProject(
				_projectPath.Text,
				_projectName.Text);

			HideStartScreen();
		}

		catch (Exception exception)
		{
			_startError.Text = exception.Message;
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void OpenProjectFromStart()
	{
		try
		{
			_session.OpenProject(_projectPath.Text);
			HideStartScreen();
		}

		catch (Exception exception)
		{
			_startError.Text = exception.Message;
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void HideStartScreen()
	{
		_startOverlay.IsVisible = false;
		_startError.Text = string.Empty;
	}

	private void UpdateStartVisibility()
	{
		_startOverlay.IsVisible = _session.Project is null;
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

		if (_playButton is not null)
		{
			_playButton.IsEnabled = stopped;
		}

		if (_pauseButton is not null)
		{
			_pauseButton.IsEnabled = playing;
		}

		if (_stepButton is not null)
		{
			_stepButton.IsEnabled = paused;
		}

		if (_stopButton is not null)
		{
			_stopButton.IsEnabled = playing || paused;
		}
	}

	private void UpdateStatus()
	{
		string name = _session.Scene?.Name ?? "No scene";
		string dirty = _session.IsSceneDirty ? "*" : string.Empty;
		string status = _session.Status;

		_status.Text = $"{name}{dirty}  |  {status}";

		RuntimeScene? runtime = _session.Play?.Runtime;

		_statusRight.Text = runtime is null
			? $"{_session.PlayState}  ·  {_session.Scene?.EntityCount ?? 0} objects"
			: $"{_session.PlayState}  ·  tick {runtime.TickCount}  ·  " +
				$"{runtime.RenderableCount} renderable";
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

	private static string ToolLabel(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Select => "Select",
			EditorTool.Translate => "Move",
			EditorTool.Rotate => "Rotate",
			EditorTool.Scale => "Scale",
			_ => tool.ToString()
		};
	}

	private static string ToolTooltip(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Select => "Selection tool",
			EditorTool.Translate => "Translate selected object",
			EditorTool.Rotate => "Rotate selected object",
			EditorTool.Scale => "Scale selected object",
			_ => string.Empty
		};
	}
}
