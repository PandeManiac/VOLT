using System.Globalization;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
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
	private readonly HashSet<string> _selectedAssetIds =
		new(StringComparer.OrdinalIgnoreCase);
	private string _selectionAnchorId = string.Empty;
	private string _signature = string.Empty;
	private bool _gridMode = true;
	private bool _updating;
	private bool _syncingSelection;
	private bool _revealSelectionPending;
	private bool _disposed;

	public ProjectPanel(EditorSession session)
		: this(session, null)
	{
	}

	internal ProjectPanel(EditorSession session, ProjectPanelState? state)
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
		_tilesButton = CreateAction(
			UiIcon.TileView,
			"Show assets as tiles",
			() => SetMode(grid: true, focusSelection: true));
		_listButton = CreateAction(
			UiIcon.ListView,
			"Show assets as a list",
			() => SetMode(grid: false, focusSelection: true));

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
		_list.ContextMenuRequested += OnListContextMenuRequested;

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

		_openProject = session.Project;

		if (state is not null &&
			session.Project is EditorProject project &&
			state.ProjectId == project.Id)
		{
			_folder = state.Folder;
			_folderSelectionActive = state.FolderSelectionActive;
			_selectedId = state.SelectedId;
			_selectedIsFolder = state.SelectedIsFolder;
			_selectedAssetIds.UnionWith(state.SelectedAssetIds);
			_search.Text = state.Search;
			_collapsedFolders.UnionWith(state.CollapsedFolders);
			_skipFolderCapture = true;
			_gridMode = state.GridMode;
		}

		_session.ProjectChanged += OnSessionChanged;
		_session.SceneChanged += OnSessionChanged;

		SetMode(grid: _gridMode);
		Rebuild(force: true);
	}

	internal ProjectPanelState CaptureState()
	{
		return new ProjectPanelState
		{
			ProjectId = _session.Project?.Id,
			Folder = _folder,
			FolderSelectionActive = _folderSelectionActive,
			GridMode = _gridMode,
			SelectedId = _selectedId,
			SelectedIsFolder = _selectedIsFolder,
			SelectedAssetIds = [.. _selectedAssetIds],
			Search = _search.Text,
			CollapsedFolders = [.. _collapsedFolders]
		};
	}

	protected override bool OnKeyDown(UiKeyEvent key)
	{
		if (!key.IsDown)
		{
			return false;
		}

		bool shift = key.HasModifier(UiKeyModifiers.Shift);

		// The list view runs its own arrow navigation. The tile grid
		// needs this panel to move focus and selection together; Shift
		// extends the asset range from the current anchor.
		if (_gridMode &&
			(key.Modifiers == UiKeyModifiers.None ||
				key.Modifiers == UiKeyModifiers.Shift) &&
			TryMoveTileFocus(key.Key, extendSelection: shift))
		{
			return true;
		}

		// Asset shortcuts belong to the asset view. The folder tree
		// and the search field can both own the keyboard while an
		// asset is selected, so requiring focus here keeps Delete
		// from acting on something the author cannot see.
		if (key.Modifiers != UiKeyModifiers.None ||
			key.IsRepeat ||
			!AssetViewHasFocus() ||
			_selectedId.Length == 0 ||
			_selectedIsFolder)
		{
			return false;
		}

		IReadOnlyList<ProjectAsset> selected = SelectedAssets();

		if (selected.Count == 0)
		{
			return false;
		}

		switch (key.Key)
		{
			case UiKey.F2:
				if (selected.Count > 1)
				{
					_session.SetStatus("Rename one asset at a time");
					return true;
				}

				ShowRenameAssetDialog(selected[0]);
				return true;

			case UiKey.Delete:
				ShowDeleteAssetsDialog(selected);
				return true;
		}

		return false;
	}

	private bool TryMoveTileFocus(UiKey key, bool extendSelection)
	{
		int columns = _tiles.Columns;

		if (columns <= 0)
		{
			return false;
		}

		int index = _tileViews.FindIndex(tile => tile.HasFocus);

		if (index < 0)
		{
			return false;
		}

		int target = key switch
		{
			UiKey.Left => index - 1,
			UiKey.Right => index + 1,
			UiKey.Up => index - columns,
			UiKey.Down => index + columns,
			_ => -1
		};

		if (target < 0 ||
			target >= _tileViews.Count ||
			target == index)
		{
			return false;
		}

		IconTileView tile = _tileViews[target];

		if (extendSelection &&
			!_selectedIsFolder &&
			tile.Tag is ProjectAsset asset)
		{
			SelectAssetRange(asset.Id);
		}
		else if (!SelectTile(tile))
		{
			return false;
		}

		tile.Focus();
		RevealSelection();
		return true;
	}

	private bool AssetViewHasFocus()
	{
		if (_list.HasFocus)
		{
			return true;
		}

		foreach (IconTileView tile in _tileViews)
		{
			if (tile.HasFocus)
			{
				return true;
			}
		}

		return false;
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

	private void SetMode(bool grid, bool focusSelection = false)
	{
		_gridMode = grid;
		_tilesButton.IsActive = grid;
		_listButton.IsActive = !grid;
		_tileScroll.IsVisible = grid;
		_listScroll.IsVisible = !grid;
		_emptyHost.IsVisible = IsEmpty();
		SyncSelection(updateDetails: false);
		_revealSelectionPending = true;

		if (focusSelection)
		{
			FocusSelection();
		}
	}

	private void FocusSelection()
	{
		if (_gridMode)
		{
			IconTileView? primary = null;
			IconTileView? selected = null;

			foreach (IconTileView tile in _tileViews)
			{
				if (!IsSelectedTag(tile.Tag))
				{
					continue;
				}

				selected ??= tile;

				if (IsPrimaryTag(tile.Tag))
				{
					primary = tile;
					break;
				}
			}

			if ((primary ?? selected) is { } target && target.Focus())
			{
				RevealSelection();
			}

			return;
		}

		if (_list.SelectedItem is not null && _list.Focus())
		{
			RevealSelection();
		}
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		base.ArrangeContent(contentBounds);

		if (_revealSelectionPending)
		{
			_revealSelectionPending = false;
			RevealSelection();
		}
	}

	private void RevealSelection()
	{
		if (_selectedId.Length == 0)
		{
			return;
		}

		if (!_gridMode)
		{
			if (_listRows.TryGetValue(
				SelectionKey(_selectedId, _selectedIsFolder),
				out TreeViewItem? row))
			{
				_list.Reveal(row);
			}

			return;
		}

		IconTileView? selected = null;

		foreach (IconTileView tile in _tileViews)
		{
			if (IsPrimaryTag(tile.Tag))
			{
				selected = tile;
				break;
			}
		}

		if (selected is null)
		{
			foreach (IconTileView tile in _tileViews)
			{
				if (IsSelectedTag(tile.Tag))
				{
					selected = tile;
					break;
				}
			}
		}

		if (selected is null)
		{
			return;
		}

		UiRect viewport = _tileScroll.GlobalContentBounds;
		UiRect bounds = selected.GlobalVisualBounds;

		if (viewport.IsEmpty || bounds.IsEmpty)
		{
			return;
		}

		float offset = _tileScroll.ScrollOffset.Y;

		if (bounds.Y < viewport.Y)
		{
			offset -= viewport.Y - bounds.Y;
		}
		else if (bounds.Bottom > viewport.Bottom)
		{
			offset += bounds.Bottom - viewport.Bottom;
		}

		_tileScroll.SetScrollOffset(offset);
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
				SetSingleAssetState(
					ProjectAssetCatalog.NormalizeId(relative));
				Rebuild(force: true);
				_revealSelectionPending = true;
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
			ClearAssetSelection();
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
			ClearAssetSelection();
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
		HashSet<string> visibleAssetIds =
			new(StringComparer.OrdinalIgnoreCase);

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
			tile.SelectionRequested += OnTileSelectionRequested;
			tile.ContextMenuRequested += OnTileContextMenuRequested;

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
			visibleAssetIds.Add(asset.Id);

			IconTileView tile = new(
				EditorIcons.ForAsset(asset.Kind),
				asset.FileName,
				asset)
			{
				IconColor = EditorIcons.AssetColor(asset.Kind, theme.Palette),
				Detail = asset.Kind.ToString()
			};

			tile.Activated += OnTileActivated;
			tile.SelectionRequested += OnTileSelectionRequested;
			tile.ContextMenuRequested += OnTileContextMenuRequested;

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

		if (_selectedIsFolder)
		{
			_selectedAssetIds.Clear();
		}
		else
		{
			_selectedAssetIds.IntersectWith(visibleAssetIds);

			if (_selectedAssetIds.Count == 0)
			{
				_selectedId = string.Empty;
				_selectionAnchorId = string.Empty;
			}
			else if (!_selectedAssetIds.Contains(_selectedId))
			{
				_selectedId = _selectedAssetIds.First();
				_selectionAnchorId = _selectedId;
			}
			else if (!_selectedAssetIds.Contains(_selectionAnchorId))
			{
				_selectionAnchorId = _selectedId;
			}
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

	private void SyncSelection(bool updateDetails, bool syncList = true)
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

			if (_selectedIsFolder)
			{
				if (_listRows.TryGetValue(
					FolderKey(_selectedId),
					out TreeViewItem? folderRow))
				{
					selected.Add(folderRow);
				}
			}
			else
			{
				foreach (string id in _selectedAssetIds)
				{
					if (string.Equals(
						id,
						_selectedId,
						StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (_listRows.TryGetValue(
						AssetKey(id),
						out TreeViewItem? row))
					{
						selected.Add(row);
					}
				}

				if (_listRows.TryGetValue(
					AssetKey(_selectedId),
					out TreeViewItem? activeRow))
				{
					selected.Add(activeRow);
				}
			}

			if (syncList)
			{
				_list.SetSelection(selected);
			}
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
		if (_selectedIsFolder)
		{
			return tag is string folder &&
				string.Equals(folder, _selectedId, StringComparison.OrdinalIgnoreCase);
		}

		return tag is ProjectAsset asset &&
			_selectedAssetIds.Contains(asset.Id);
	}

	private bool IsPrimaryTag(object? tag)
	{
		if (_selectedIsFolder)
		{
			return tag is string folder &&
				string.Equals(folder, _selectedId, StringComparison.OrdinalIgnoreCase);
		}

		return tag is ProjectAsset asset &&
			string.Equals(asset.Id, _selectedId, StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateDetails()
	{
		if (_selectedIsFolder)
		{
			_details.Text = $"Folder  ·  {_selectedId}";
			return;
		}

		if (_selectedAssetIds.Count > 1)
		{
			_details.Text = $"{_selectedAssetIds.Count} assets selected";
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

	private void OnTileContextMenuRequested(
		IconTileView tile,
		UiPoint position)
	{
		if (tile.Tag is not ProjectAsset asset)
		{
			return;
		}

		if (!_selectedAssetIds.Contains(asset.Id))
		{
			SelectSingleAsset(asset.Id, updateDetails: true);
		}

		ShowAssetContextMenu(asset, SelectedAssets(), position);
	}

	private void OnListContextMenuRequested(
		TreeView tree,
		TreeViewItem item,
		UiPoint position)
	{
		if (item.Tag is not ProjectAsset asset)
		{
			return;
		}

		if (!_selectedAssetIds.Contains(asset.Id))
		{
			SelectSingleAsset(asset.Id, updateDetails: true);
		}

		ShowAssetContextMenu(asset, SelectedAssets(), position);
	}

	private void ShowAssetContextMenu(
		ProjectAsset asset,
		IReadOnlyList<ProjectAsset> selection,
		UiPoint position)
	{
		if (selection.Count == 0)
		{
			selection = [asset];
		}

		bool multiple = selection.Count > 1;
		bool openScene = selection.Any(item =>
			_session.ScenePath is string current &&
			string.Equals(
				current,
				item.Id,
				StringComparison.OrdinalIgnoreCase));
		bool startupScene = _session.Project is EditorProject project &&
			selection.Any(item => string.Equals(
				ProjectAssetCatalog.NormalizeId(project.StartupScenePath),
				item.Id,
				StringComparison.OrdinalIgnoreCase));

		MenuItem open = new("Open", () => OpenTag(asset))
		{
			IsEnabled = !multiple &&
				asset.Kind == ProjectAssetKind.Scene
		};

		MenuItem rename = new("Rename…", () => ShowRenameAssetDialog(asset))
		{
			IsEnabled = !multiple
		};

		MenuItem duplicate = new("Duplicate", () => DuplicateAsset(asset))
		{
			IsEnabled = !multiple
		};

		MenuItem delete = new(
			multiple ? $"Delete {selection.Count} Assets" : "Delete",
			() => ShowDeleteAssetsDialog(selection))
		{
			IsEnabled = !openScene && !startupScene
		};

		MenuPopup menu = new(
		[
			open,
			rename,
			duplicate,
			MenuItem.Separator,
			delete
		]);

		Context?.ShowPopup(
			menu,
			new UiRect(position.X, position.Y, 0f, 0f));
	}

	private void DuplicateAsset(ProjectAsset asset)
	{
		if (_session.Project is not EditorProject project)
		{
			return;
		}

		try
		{
			string copyId = project.DuplicateAsset(asset.Id);

			SetSingleAssetState(copyId);
			Rebuild(force: true);
			_revealSelectionPending = true;
			_session.SetStatus($"Duplicated {asset.FileName}");
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
			_session.SetStatus(exception.Message);
		}
	}

	private void ShowRenameAssetDialog(ProjectAsset asset)
	{
		if (Context is not UiContext context)
		{
			return;
		}

		UiTheme theme = context.Theme;
		Panel scrim = CreateAssetDialog(
			theme,
			"Rename asset",
			400f,
			out StackPanel content);

		content.Add(new Label($"Rename \"{asset.FileName}\".")
		{
			Font = theme.Fonts.Body,
			Wrap = UiTextWrap.Wrap
		});

		string extension = AssetExtension(asset.FileName);

		TextField name = new(AssetStem(asset.FileName))
		{
			Height = UiLength.Points(28f),
			Placeholder = "Asset name",
			MaximumLength = 120,
			TooltipText = "New file name without the extension"
		};

		content.Add(name);

		content.Add(new Label(extension.Length > 0
			? $"The {extension} extension is kept."
			: "The new name will not add an extension.")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		Label error = new(string.Empty)
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.Danger,
			Wrap = UiTextWrap.Wrap
		};

		content.Add(error);

		StackPanel buttons = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		Button rename = new("Rename")
		{
			IsPrimary = true,
			MinHeight = 28f
		};

		Button cancel = new("Cancel")
		{
			MinHeight = 28f
		};

		cancel.Clicked += _ => context.CloseModal();

		rename.Clicked += _ =>
		{
			try
			{
				string renamed = _session.RenameAsset(asset.Id, name.Text);

				SetSingleAssetState(renamed);
				Rebuild(force: true);
				_revealSelectionPending = true;
				context.CloseModal();
				_session.SetStatus(
					$"Renamed {asset.FileName} to {Path.GetFileName(renamed)}");
			}

			catch (Exception exception)
			{
				error.Text = exception.Message;
			}
		};

		buttons.Add(rename);
		buttons.Add(cancel);
		content.Add(buttons);

		context.ShowModal(scrim);
		name.Focus();
		name.SelectAll();
	}

	private void ShowDeleteAssetsDialog(IReadOnlyList<ProjectAsset> assets)
	{
		if (assets.Count == 0 || Context is not UiContext context)
		{
			return;
		}

		bool openScene = assets.Any(asset =>
			_session.ScenePath is string current &&
			string.Equals(
				current,
				asset.Id,
				StringComparison.OrdinalIgnoreCase));
		bool startupScene = _session.Project is EditorProject project &&
			assets.Any(asset => string.Equals(
				ProjectAssetCatalog.NormalizeId(project.StartupScenePath),
				asset.Id,
				StringComparison.OrdinalIgnoreCase));

		if (openScene)
		{
			_session.SetStatus("Close the scene before deleting its file");
			return;
		}

		if (startupScene)
		{
			_session.SetStatus(
				"The startup scene cannot be deleted");
			return;
		}

		int count = assets.Count;
		bool single = count == 1;
		ProjectAsset only = assets[0];
		UiTheme theme = context.Theme;
		Panel scrim = CreateAssetDialog(
			theme,
			single ? "Delete asset" : "Delete assets",
			420f,
			out StackPanel content);

		content.Add(new Label(single
			? $"Delete \"{only.FileName}\"?"
			: $"Delete {count} selected assets?")
		{
			Font = theme.Fonts.Body,
			Wrap = UiTextWrap.Wrap
		});

		content.Add(new Label(single
			? (only.Kind == ProjectAssetKind.Scene
				? "This removes the scene file and cannot be undone."
				: "This cannot be undone. References to this asset in " +
					"scenes will be cleared.")
			: "This cannot be undone. The selected files are removed, " +
				"and references to them in scenes will be cleared.")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		Label error = new(string.Empty)
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.Danger,
			Wrap = UiTextWrap.Wrap
		};

		content.Add(error);

		StackPanel buttons = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		Button delete = new(single ? "Delete" : $"Delete {count}")
		{
			IsPrimary = true,
			MinHeight = 28f
		};

		Button cancel = new("Cancel")
		{
			MinHeight = 28f
		};

		cancel.Clicked += _ => context.CloseModal();

		delete.Clicked += _ =>
		{
			try
			{
				int deleted = _session.DeleteAssets(
					assets.Select(asset => asset.Id));

				ClearAssetSelection();
				Rebuild(force: true);
				context.CloseModal();
				_session.SetStatus(deleted == 1
					? $"Deleted {only.FileName}"
					: $"Deleted {deleted} assets");
			}

			catch (Exception exception)
			{
				error.Text = exception.Message;
			}
		};

		buttons.Add(delete);
		buttons.Add(cancel);
		content.Add(buttons);

		context.ShowModal(scrim);
	}

	private static Panel CreateAssetDialog(
		UiTheme theme,
		string title,
		float width,
		out StackPanel content)
	{
		Panel scrim = new()
		{
			Background = UiColor.FromArgb(0xAA0B0E13),
			DrawBorder = false
		};

		Panel card = new()
		{
			Width = UiLength.Points(width),
			Height = UiLength.Auto,
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center,
			DrawBackground = true,
			Background = theme.Palette.SurfaceRaised,
			DrawBorder = true,
			BorderColor = theme.Palette.BorderStrong,
			Padding = new UiThickness(20f)
		};

		content = new StackPanel
		{
			Spacing = 10f
		};

		content.Add(new Label(title)
		{
			Font = theme.Fonts.Heading
		});

		content.Add(new Separator());

		card.Add(content);
		scrim.Add(card);

		return scrim;
	}

	private static string AssetExtension(string fileName)
	{
		return fileName.EndsWith(
			SceneSerializer.FileExtension,
			StringComparison.OrdinalIgnoreCase)
			? SceneSerializer.FileExtension
			: Path.GetExtension(fileName);
	}

	private static string AssetStem(string fileName)
	{
		string extension = AssetExtension(fileName);
		return fileName[..^extension.Length];
	}

	private void OnTileSelectionRequested(
		IconTileView tile,
		UiKeyModifiers modifiers)
	{
		bool control =
			(modifiers & UiKeyModifiers.Control) != 0 ||
			(modifiers & UiKeyModifiers.Super) != 0;
		bool shift = (modifiers & UiKeyModifiers.Shift) != 0;

		if (tile.Tag is string folder)
		{
			SelectFolder(folder);
			return;
		}

		if (tile.Tag is not ProjectAsset asset)
		{
			return;
		}

		if (control)
		{
			ToggleAssetSelection(asset.Id);
		}
		else if (shift)
		{
			SelectAssetRange(asset.Id);
		}
		else
		{
			SelectSingleAsset(asset.Id, updateDetails: true);
		}
	}

	private bool SelectTile(IconTileView tile)
	{
		if (tile.Tag is ProjectAsset asset)
		{
			SelectSingleAsset(asset.Id, updateDetails: true);
			return true;
		}

		if (tile.Tag is string folder)
		{
			SelectFolder(folder);
			return true;
		}

		return false;
	}

	private void SelectFolder(string folder)
	{
		ClearAssetSelection();
		_selectedId = folder;
		_selectedIsFolder = true;
		SyncSelection(updateDetails: true);
	}

	private void ClearAssetSelection()
	{
		_selectedAssetIds.Clear();
		_selectedId = string.Empty;
		_selectedIsFolder = false;
		_selectionAnchorId = string.Empty;
	}

	private void SetSingleAssetState(string id)
	{
		_selectedAssetIds.Clear();
		_selectedAssetIds.Add(id);
		_selectedId = id;
		_selectedIsFolder = false;
		_selectionAnchorId = id;
	}

	private void SelectSingleAsset(string id, bool updateDetails)
	{
		SetSingleAssetState(id);
		SyncSelection(updateDetails);
	}

	private void ToggleAssetSelection(string id)
	{
		if (_selectedIsFolder)
		{
			SelectSingleAsset(id, updateDetails: true);
			return;
		}

		if (_selectedAssetIds.Remove(id))
		{
			if (_selectedAssetIds.Count == 0)
			{
				ClearAssetSelection();
			}
			else if (string.Equals(
				_selectedId,
				id,
				StringComparison.OrdinalIgnoreCase))
			{
				_selectedId = VisibleAssetIds()
					.FirstOrDefault(assetId =>
						_selectedAssetIds.Contains(assetId)) ??
					_selectedAssetIds.First();
			}

			if (!_selectedAssetIds.Contains(_selectionAnchorId))
			{
				_selectionAnchorId = _selectedId;
			}
		}
		else
		{
			_selectedAssetIds.Add(id);
			_selectedId = id;
			_selectedIsFolder = false;
			_selectionAnchorId = id;
		}

		SyncSelection(updateDetails: true);
	}

	private void SelectAssetRange(string id)
	{
		if (_selectedIsFolder || _selectionAnchorId.Length == 0)
		{
			SelectSingleAsset(id, updateDetails: true);
			return;
		}

		List<string> order = VisibleAssetIds();
		int anchor = order.FindIndex(item => string.Equals(
			item,
			_selectionAnchorId,
			StringComparison.OrdinalIgnoreCase));
		int target = order.FindIndex(item => string.Equals(
			item,
			id,
			StringComparison.OrdinalIgnoreCase));

		if (anchor < 0 || target < 0)
		{
			SelectSingleAsset(id, updateDetails: true);
			return;
		}

		if (anchor > target)
		{
			(anchor, target) = (target, anchor);
		}

		_selectedAssetIds.Clear();

		for (int index = anchor; index <= target; index++)
		{
			_selectedAssetIds.Add(order[index]);
		}

		_selectedId = id;
		_selectedIsFolder = false;
		SyncSelection(updateDetails: true);
	}

	private List<string> VisibleAssetIds()
	{
		List<string> ids = [];

		foreach (IconTileView tile in _tileViews)
		{
			if (tile.Tag is ProjectAsset asset)
			{
				ids.Add(asset.Id);
			}
		}

		return ids;
	}

	private List<ProjectAsset> SelectedAssets()
	{
		List<ProjectAsset> selected = [];

		if (_session.Project is not EditorProject project)
		{
			return selected;
		}

		foreach (IconTileView tile in _tileViews)
		{
			if (tile.Tag is ProjectAsset asset &&
				_selectedAssetIds.Contains(asset.Id))
			{
				selected.Add(asset);
			}
		}

		if (selected.Count > 0)
		{
			return selected;
		}

		if (!_selectedIsFolder &&
			project.AssetCatalog.Find(_selectedId) is ProjectAsset active)
		{
			selected.Add(active);
		}

		return selected;
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
		if (_updating || _syncingSelection)
		{
			return;
		}

		if (selection.Count == 0)
		{
			ClearAssetSelection();
			UpdateDetails();
			return;
		}

		if (selection.Any(item => item.Tag is string folder))
		{
			if (selection[^1].Tag is string folder)
			{
				ClearAssetSelection();
				_selectedId = folder;
				_selectedIsFolder = true;
			}
			else if (selection[^1].Tag is ProjectAsset lastAsset)
			{
				SetSingleAssetState(lastAsset.Id);
			}
			else
			{
				return;
			}

			SyncSelection(updateDetails: true);
			return;
		}

		_selectedAssetIds.Clear();
		string? first = null;

		foreach (TreeViewItem item in selection)
		{
			if (item.Tag is ProjectAsset asset)
			{
				_selectedAssetIds.Add(asset.Id);
				first ??= asset.Id;
			}
		}

		if (_selectedAssetIds.Count == 0)
		{
			ClearAssetSelection();
			UpdateDetails();
			return;
		}

		if (selection[^1].Tag is ProjectAsset active)
		{
			_selectedId = active.Id;
		}

		_selectedIsFolder = false;

		if (!_selectedAssetIds.Contains(_selectionAnchorId))
		{
			_selectionAnchorId = first ?? _selectedId;
		}

		// TreeView already holds this selection and its own range
		// anchor. Re-setting it would move the anchor to the last row
		// and break a second Shift+arrow extension.
		SyncSelection(updateDetails: true, syncList: false);
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
