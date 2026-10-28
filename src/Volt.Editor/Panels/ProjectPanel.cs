using System.Globalization;
using Volt.Editor.Core.Projects;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

/// <summary>
/// The project browser: folder navigation, tiles or rows, and details.
/// </summary>
/// <remarks>
/// <para>
/// The panel presents real catalog content, so every tile corresponds
/// to a file the editor can open or reference. Folders come from the
/// same project-relative identifiers the catalog produces, which keeps
/// the browser and the inspector's asset fields describing the same
/// paths.
/// </para>
/// <para>
/// Grid and list modes share one selection model: both are views over
/// the current folder, and activating a scene opens it.
/// </para>
/// </remarks>
public sealed class ProjectPanel : Panel
{
	private readonly EditorSession _session;
	private EditorProject? _openProject;
	private readonly TreeView _folders;
	private readonly TreeView _list;
	private readonly TileWrapPanel _tiles;
	private readonly ScrollView _tileScroll;
	private readonly ScrollView _listScroll;
	private readonly TextField _search;
	private readonly Label _breadcrumb;
	private readonly Label _details;
	private readonly EmptyStateView _empty;
	private readonly Panel _emptyHost;
	private readonly IconButton _tilesButton;
	private readonly IconButton _listButton;
	private readonly IconButton _refreshButton;
	private readonly IconButton _newSceneButton;
	private readonly IconButton _upButton;

	private readonly List<IconTileView> _tileViews = [];
	private readonly Dictionary<string, TreeViewItem> _listRows =
		new(StringComparer.OrdinalIgnoreCase);

	private string _folder = string.Empty;
	private bool _folderSelectionActive;
	private readonly HashSet<string> _collapsedFolders =
		new(StringComparer.OrdinalIgnoreCase);
	private bool _folderFilterActive;
	private bool _skipFolderCapture;
	private string _selectedId = string.Empty;
	private bool _selectedIsFolder;
	private string _signature = string.Empty;
	private bool _gridMode = true;
	private bool _updating;
	private bool _syncingSelection;
	private bool _disposed;

	public ProjectPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		ClipToBounds = true;

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		StackPanel header = new(UiOrientation.Horizontal)
		{
			Spacing = 4f,
			Height = UiLength.Points(26f)
		};

		_upButton = CreateAction(UiIcon.ChevronRight, "Go to the parent folder", GoUp);
		_breadcrumb = new Label("Project")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted,
			FlexGrow = 1f,
			VerticalAlignment = UiAlignment.Center,
			ClipToBounds = true
		};

		_refreshButton = CreateAction(UiIcon.Refresh, "Rescan the project folder", RefreshCatalog);
		_newSceneButton = CreateAction(UiIcon.Plus, "Create a new scene", CreateScene);
		_tilesButton = CreateAction(UiIcon.TileView, "Show assets as tiles", () => SetMode(grid: true));
		_listButton = CreateAction(UiIcon.ListView, "Show assets as a list", () => SetMode(grid: false));

		header.Add(_upButton);
		header.Add(_breadcrumb);
		header.Add(CreateSeparator());
		header.Add(_refreshButton);
		header.Add(_newSceneButton);
		header.Add(_tilesButton);
		header.Add(_listButton);

		_search = new TextField
		{
			Placeholder = "Search assets",
			Height = UiLength.Points(26f)
		};

		_search.TextChanged += _ => Rebuild(force: true);

		_folders = new TreeView
		{
			MinWidth = 96f,
			MaxWidth = 210f
		};

		_folders.SelectionChanged += OnFolderSelectionChanged;

		_tiles = new TileWrapPanel();

		_tileScroll = new ScrollView
		{
			FlexGrow = 1f,
			MinHeight = 44f,
			Content = _tiles
		};

		_list = new TreeView
		{
			FlexGrow = 1f,
			MinHeight = 60f
		};

		_list.SelectionChanged += OnListSelectionChanged;
		_list.ItemActivated += OnListActivated;

		_listScroll = new ScrollView
		{
			FlexGrow = 1f,
			MinHeight = 44f,
			Content = _list
		};

		StackPanel body = new(UiOrientation.Horizontal)
		{
			Spacing = 6f,
			FlexGrow = 1f
		};

		body.Add(_folders);
		body.Add(new Separator(UiOrientation.Vertical));
		body.Add(_tileScroll);
		body.Add(_listScroll);

		_empty = new EmptyStateView(
			UiIcon.Project,
			"No assets yet",
			"Save a scene or add files to the project's Assets folder.");

		// The empty state is an overlay rather than a stack row, so an
		// empty folder costs the panel no additional height.
		_emptyHost = new Panel
		{
			DrawBackground = false,
			IsHitTestVisible = false
		};

		_emptyHost.Add(_empty);

		_details = new Label(string.Empty)
		{
			Font = theme.Fonts.MonoSmall,
			TextColor = theme.Palette.TextDisabled,
			Height = UiLength.Points(20f),
			ClipToBounds = true
		};

		StackPanel stack = new()
		{
			Spacing = 5f,
			Padding = new UiThickness(8f, 6f, 8f, 6f)
		};

		stack.Add(header);
		stack.Add(_search);
		stack.Add(body);
		stack.Add(_details);

		Add(stack);
		Add(_emptyHost);

		_session.ProjectChanged += OnSessionChanged;
		_session.SceneChanged += OnSessionChanged;

		SetMode(grid: true);
		Rebuild(force: true);
	}

	protected override void OnDetached()
	{
		Unsubscribe();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Unsubscribe();
		base.Dispose();
	}

	private static Widget CreateSeparator()
	{
		return new Separator(UiOrientation.Vertical)
		{
			Margin = new UiThickness(2f, 0f)
		};
	}

	private static IconButton CreateAction(
		UiIcon icon,
		string tooltip,
		Action action)
	{
		IconButton button = new(icon, tooltip)
		{
			Width = UiLength.Points(24f),
			Height = UiLength.Points(24f),
			MinWidth = 22f,
			MinHeight = 22f
		};

		button.Clicked += _ => action();
		return button;
	}

	private void SetMode(bool grid)
	{
		_gridMode = grid;
		_tilesButton.IsActive = grid;
		_listButton.IsActive = !grid;
		_tileScroll.IsVisible = grid;
		_listScroll.IsVisible = !grid;
		_emptyHost.IsVisible = IsEmpty();
		SyncSelection(updateDetails: false);
	}

	private bool IsEmpty()
	{
		return _gridMode
			? _tiles.Children.Count == 0
			: _list.Roots.Count == 0;
	}

	private void OnSessionChanged(EditorSession session)
	{
		Rebuild(force: false);
	}

	private void RefreshCatalog()
	{
		try
		{
			_session.Project?.AssetCatalog.Refresh();
			Rebuild(force: true);
			_session.SetStatus("Asset catalog refreshed");
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void CreateScene()
	{
		if (_session.Project is not EditorProject project)
		{
			_session.SetStatus("Open a project to create a scene");
			return;
		}

		int index = 1;
		string relative;

		do
		{
			relative = $"Scenes/Scene{index}.volt.scene.json";
			index++;
		}
		while (File.Exists(project.ResolvePath(relative)));

		try
		{
			if (_session.CreateScene(
					relative,
					$"Scene {index - 1}") is not null)
			{
				// Creation opens the scene and raises SceneChanged while
				// the panel still shows the old folder. Move to the new
				// scene's folder and rebuild again so its tile is visible.
				_folder = ParentFolder(relative);
				_selectedId = ProjectAssetCatalog.NormalizeId(relative);
				_selectedIsFolder = false;
				Rebuild(force: true);
			}
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void GoUp()
	{
		if (_folder.Length == 0)
		{
			return;
		}

		int separator = _folder.LastIndexOf('/');
		_folder = separator < 0 ? string.Empty : _folder[..separator];
		_folderSelectionActive = true;
		Rebuild(force: true);
	}

	private void Rebuild(bool force)
	{
		if (_updating)
		{
			return;
		}

		EditorProject? project = _session.Project;

		if (!ReferenceEquals(_openProject, project))
		{
			_openProject = project;
			_folder = string.Empty;
			_folderSelectionActive = false;
			_collapsedFolders.Clear();
			_folderFilterActive = false;
			_skipFolderCapture = true;
			_search.Text = string.Empty;
			_selectedId = string.Empty;
			_selectedIsFolder = false;
			force = true;
		}

		if (project is null)
		{
			_signature = "none";
			_updating = true;
			_tiles.ClearChildren();
			_tileViews.Clear();
			_listRows.Clear();
			_folderSelectionActive = false;
			_selectedId = string.Empty;
			_selectedIsFolder = false;
			_folders.Clear();
			_list.Clear();
			_breadcrumb.Text = "No project open";
			_details.Text = string.Empty;
			_refreshButton.IsEnabled = false;
			_newSceneButton.IsEnabled = false;
			_empty.Title = "No project open";
			_empty.Description =
				"Create or open a project to browse its assets.";
			_emptyHost.IsVisible = true;
			_updating = false;
			return;
		}

		string signature = BuildSignature(project, _search.Text, _folder);

		if (!force && signature == _signature)
		{
			return;
		}

		_signature = signature;
		_updating = true;

		IReadOnlyList<ProjectAsset> assets = project.AssetCatalog.Assets;
		string filter = _search.Text.Trim();

		RebuildFolders(assets, filter);
		RebuildContents(assets, filter);

		_breadcrumb.Text = _folder.Length == 0
			? project.Name
			: $"{project.Name}  /  {_folder.Replace("/", "  /  ")}";

		_upButton.IsEnabled = _folder.Length > 0;
		_refreshButton.IsEnabled = true;
		_newSceneButton.IsEnabled = true;

		int total = _gridMode
			? _tiles.Children.Count
			: _list.Roots.Count;

		_details.Text = filter.Length > 0
			? $"{total} of {assets.Count} assets"
			: $"{total} items";

		if (_selectedId.Length > 0 &&
			(_selectedIsFolder ||
				project.AssetCatalog.Find(_selectedId) is not null))
		{
			UpdateDetails();
		}

		_empty.Title = filter.Length > 0
			? "No matching assets"
			: "This folder is empty";

		_empty.Description = filter.Length > 0
			? $"Nothing matches \"{filter}\"."
			: "Save a scene or add files to the project's Assets folder.";

		_emptyHost.IsVisible = total == 0;

		_updating = false;
	}

	private void RebuildFolders(
		IReadOnlyList<ProjectAsset> assets,
		string filter)
	{
		if (_skipFolderCapture)
		{
			_skipFolderCapture = false;
		}
		else if (!_folderFilterActive && filter.Length == 0)
		{
			CaptureCollapsedFolders();
		}

		_folderFilterActive = filter.Length > 0;

		_folders.Clear();

		SortedDictionary<string, TreeViewItem> nodes =
			new(StringComparer.OrdinalIgnoreCase);

		foreach (ProjectAsset asset in assets)
		{
			string[] segments = asset.Id.Split('/');

			if (segments.Length < 2)
			{
				continue;
			}

			string path = string.Empty;
			TreeViewItem? parent = null;

			for (int index = 0; index < segments.Length - 1; index++)
			{
				path = path.Length == 0
					? segments[index]
					: $"{path}/{segments[index]}";

				if (!nodes.TryGetValue(path, out TreeViewItem? node))
				{
					node = new TreeViewItem(
						$"folder:{path}",
						segments[index],
						path)
					{
						IsExpanded = filter.Length > 0 ||
							!_collapsedFolders.Contains(path),
						Icon = UiIcon.Folder
					};

					nodes[path] = node;

					// Top-level folders are attached under the project root
					// below, once that root exists.
					if (parent is not null)
					{
						parent.AddChild(node);
					}
				}

				parent = node;
			}
		}

		TreeViewItem? projectRoot = new(
			"folder:",
			_session.Project?.Name ?? "Project",
			string.Empty)
		{
			IsExpanded = filter.Length > 0 ||
				!_collapsedFolders.Contains(string.Empty),
			Icon = UiIcon.Project
		};

		foreach (KeyValuePair<string, TreeViewItem> pair in nodes)
		{
			if (pair.Key.Contains('/'))
			{
				continue;
			}

			projectRoot.AddChild(pair.Value);
		}

		_folders.AddRoot(projectRoot);

		TreeViewItem? selectedFolder = null;

		if (_folder.Length > 0)
		{
			nodes.TryGetValue(_folder, out selectedFolder);
		}
		else if (_folderSelectionActive)
		{
			selectedFolder = projectRoot;
		}

		if (filter.Length > 0)
		{
			_folders.ExpandAll();
		}
		else if (selectedFolder is not null && selectedFolder != projectRoot)
		{
			_folders.Reveal(selectedFolder);
		}

		// Rebuilt rows replace the old items, so restore the highlight
		// on the fresh node for the folder the browser is showing.
		if (selectedFolder is not null)
		{
			_folders.SetSelection([selectedFolder]);
		}
	}

	private void CaptureCollapsedFolders()
	{
		foreach (TreeViewItem root in _folders.Roots)
		{
			CaptureCollapsedFolder(root);
		}
	}

	private void CaptureCollapsedFolder(TreeViewItem item)
	{
		if (item.Tag is string path)
		{
			if (item.IsExpanded)
			{
				_collapsedFolders.Remove(path);
			}
			else
			{
				_collapsedFolders.Add(path);
			}
		}

		foreach (TreeViewItem child in item.Children)
		{
			CaptureCollapsedFolder(child);
		}
	}

	private void RebuildContents(
		IReadOnlyList<ProjectAsset> assets,
		string filter)
	{
		for (int index = _tiles.Children.Count - 1; index >= 0; index--)
		{
			Widget tile = _tiles.Children[index];

			_tiles.Remove(tile);
			tile.Dispose();
		}

		_tileViews.Clear();
		_listRows.Clear();
		_list.Clear();

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		List<ProjectAsset> visible = [];

		foreach (ProjectAsset asset in assets)
		{
			if (filter.Length > 0)
			{
				if (!asset.FileName.Contains(
					filter,
					StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				visible.Add(asset);
				continue;
			}

			string parent = ParentFolder(asset.Id);

			if (!string.Equals(parent, _folder, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			visible.Add(asset);
		}

		SortedDictionary<string, string> folders =
			new(StringComparer.OrdinalIgnoreCase);

		foreach (ProjectAsset asset in assets)
		{
			if (filter.Length > 0)
			{
				continue;
			}

			if (_folder.Length > 0 &&
				!asset.Id.StartsWith(
					_folder + "/",
					StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string remainder = asset.Id[(_folder.Length == 0
				? 0
				: _folder.Length + 1)..];
			int slash = remainder.IndexOf('/');

			if (slash <= 0)
			{
				continue;
			}

			string name = remainder[..slash];
			string folderPath = _folder.Length == 0
				? name
				: $"{_folder}/{name}";

			folders[folderPath] = name;
		}

		foreach (KeyValuePair<string, string> folder in folders)
		{
			IconTileView tile = new(
				UiIcon.Folder,
				folder.Value,
				folder.Key)
			{
				IconColor = theme.Palette.Warning,
				Detail = "Folder"
			};

			tile.Activated += OnTileActivated;
			tile.Clicked += OnTileClicked;

			_tileViews.Add(tile);
			_tiles.Add(tile);

			TreeViewItem row = new(
				$"list-folder:{folder.Key}",
				folder.Value,
				folder.Key)
			{
				Icon = UiIcon.Folder,
				IconColor = theme.Palette.Warning
			};

			_listRows[FolderKey(folder.Key)] = row;
			_list.AddRoot(row);
		}

		foreach (ProjectAsset asset in visible)
		{
			IconTileView tile = new(
				EditorIcons.ForAsset(asset.Kind),
				asset.FileName,
				asset)
			{
				IconColor = EditorIcons.AssetColor(asset.Kind, theme.Palette),
				Detail = asset.Kind.ToString()
			};

			tile.Activated += OnTileActivated;
			tile.Clicked += OnTileClicked;

			_tileViews.Add(tile);
			_tiles.Add(tile);

			TreeViewItem row = new(
				$"list-asset:{asset.Id}",
				$"{asset.FileName}   ({FormatSize(asset.Length)})",
				asset)
			{
				Icon = EditorIcons.ForAsset(asset.Kind),
				IconColor = EditorIcons.AssetColor(asset.Kind, theme.Palette)
			};

			_listRows[AssetKey(asset.Id)] = row;
			_list.AddRoot(row);
		}

		_tiles.InvalidateMeasure();
		_list.Refresh();
		SyncSelection(updateDetails: false);
	}

	private static string AssetKey(string id)
	{
		return "asset:" + id;
	}

	private static string FolderKey(string id)
	{
		return "folder:" + id;
	}

	private static string SelectionKey(string id, bool isFolder)
	{
		return isFolder ? FolderKey(id) : AssetKey(id);
	}

	private void SyncSelection(bool updateDetails)
	{
		if (_syncingSelection)
		{
			return;
		}

		_syncingSelection = true;

		try
		{
			foreach (IconTileView tile in _tileViews)
			{
				tile.IsSelected = IsSelectedTag(tile.Tag);
			}

			List<TreeViewItem> selected = [];

			if (_selectedId.Length > 0 &&
				_listRows.TryGetValue(
					SelectionKey(_selectedId, _selectedIsFolder),
					out TreeViewItem? row))
			{
				selected.Add(row);
			}

			_list.SetSelection(selected);
		}

		finally
		{
			_syncingSelection = false;
		}

		if (updateDetails)
		{
			UpdateDetails();
		}
	}

	private bool IsSelectedTag(object? tag)
	{
		if (_selectedId.Length == 0)
		{
			return false;
		}

		return _selectedIsFolder
			? tag is string folder &&
				string.Equals(folder, _selectedId, StringComparison.OrdinalIgnoreCase)
			: tag is ProjectAsset asset &&
				string.Equals(asset.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateDetails()
	{
		if (_selectedIsFolder)
		{
			_details.Text = $"Folder  ·  {_selectedId}";
			return;
		}

		ProjectAsset? asset = _session.Project?.AssetCatalog.Find(_selectedId);

		_details.Text = asset is null
			? string.Empty
			: $"{asset.Kind}  ·  {asset.Id}  ·  {FormatSize(asset.Length)}";
	}

	private static string ParentFolder(string id)
	{
		int slash = id.LastIndexOf('/');
		return slash < 0 ? string.Empty : id[..slash];
	}

	private void OnTileClicked(IconTileView tile)
	{
		if (tile.Tag is ProjectAsset asset)
		{
			_selectedId = asset.Id;
			_selectedIsFolder = false;
		}

		else if (tile.Tag is string folder)
		{
			_selectedId = folder;
			_selectedIsFolder = true;
		}

		else
		{
			return;
		}

		SyncSelection(updateDetails: true);
	}

	private void OnTileActivated(IconTileView tile)
	{
		OpenTag(tile.Tag);
	}

	private void OnFolderSelectionChanged(
		TreeView tree,
		IReadOnlyList<TreeViewItem> selection)
	{
		if (_updating || selection.Count == 0)
		{
			return;
		}

		if (selection[^1].Tag is not string folder)
		{
			return;
		}

		_folder = folder;
		_folderSelectionActive = true;
		Rebuild(force: true);
	}

	private void OnListSelectionChanged(
		TreeView tree,
		IReadOnlyList<TreeViewItem> selection)
	{
		if (_updating || selection.Count == 0)
		{
			return;
		}

		if (_syncingSelection)
		{
			return;
		}

		if (selection[^1].Tag is ProjectAsset asset)
		{
			_selectedId = asset.Id;
			_selectedIsFolder = false;
		}

		else if (selection[^1].Tag is string folder)
		{
			_selectedId = folder;
			_selectedIsFolder = true;
		}

		else
		{
			return;
		}

		SyncSelection(updateDetails: true);
	}

	private void OnListActivated(TreeView tree, TreeViewItem item)
	{
		OpenTag(item.Tag);
	}

	private void OpenTag(object? tag)
	{
		if (tag is string folder)
		{
			_folder = folder;
			_folderSelectionActive = true;
			Rebuild(force: true);
			return;
		}

		if (tag is not ProjectAsset asset)
		{
			return;
		}

		if (asset.Kind != ProjectAssetKind.Scene)
		{
			_session.SetStatus($"{asset.FileName} is not openable yet");
			return;
		}

		try
		{
			_session.OpenScene(asset.Id);
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private static string FormatSize(long bytes)
	{
		return bytes < 1024
			? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
			: $"{bytes / 1024.0:0.#} KiB";
	}

	private static string BuildSignature(
		EditorProject project,
		string filter,
		string folder)
	{
		System.Text.StringBuilder builder = new();
		builder.Append(filter).Append('|').Append(folder).Append('|');

		foreach (ProjectAsset asset in project.AssetCatalog.Assets)
		{
			builder
				.Append(asset.Id)
				.Append(':')
				.Append(asset.Length)
				.Append(':')
				.Append(asset.LastWriteTimeUtc.Ticks)
				.Append(';');
		}

		return builder.ToString();
	}

	private void Unsubscribe()
	{
		_session.ProjectChanged -= OnSessionChanged;
		_session.SceneChanged -= OnSessionChanged;
	}
}
