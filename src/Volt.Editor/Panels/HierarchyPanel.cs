using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

/// <summary>
/// The scene object tree: hierarchy, search, creation, and selection.
/// </summary>
/// <remarks>
/// Rows are plain tree items driven by the authored scene, so the
/// panel stays a view over session state. Selection is mirrored in
/// both directions: clicking a row selects the entity, and a viewport
/// pick reveals the row. The header carries the creation actions and
/// a right-click menu offers the same commands per object.
/// </remarks>
public sealed class HierarchyPanel : Panel
{
	private readonly EditorSession _session;
	private readonly TreeView _tree;
	private readonly TextField _search;
	private readonly EmptyStateView _empty;
	private readonly Panel _emptyHost;
	private readonly IconButton _duplicateButton;
	private readonly IconButton _deleteButton;

	private readonly Dictionary<Guid, TreeViewItem> _items = [];
	private readonly HashSet<Guid> _expanded = [];
	private readonly HashSet<Guid> _known = [];

	private string _signature = string.Empty;
	private bool _updating;
	private bool _disposed;

	/// <summary>
	/// Raised when the context menu asks to rename the active row.
	/// </summary>
	public event Action? RenameRequested;

	public HierarchyPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		ClipToBounds = true;

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		StackPanel stack = new()
		{
			Spacing = 6f,
			Padding = new UiThickness(8f, 6f, 8f, 6f)
		};

		StackPanel header = new(UiOrientation.Horizontal)
		{
			Spacing = 4f,
			Height = UiLength.Points(24f)
		};

		header.Add(new Label("Scene Objects")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted,
			FlexGrow = 1f,
			VerticalAlignment = UiAlignment.Center
		});

		header.Add(CreateAction(UiIcon.Plus, "Create an empty object", CreateEntity));

		_duplicateButton = CreateAction(
			UiIcon.Duplicate,
			"Duplicate the selection",
			Duplicate);

		_deleteButton = CreateAction(
			UiIcon.Trash,
			"Delete the selection",
			Delete);

		header.Add(_duplicateButton);
		header.Add(_deleteButton);

		_search = new TextField
		{
			Placeholder = "Search objects",
			Height = UiLength.Points(26f)
		};

		_search.TextChanged += _ => RefreshStructure(force: true);

		_tree = new TreeView
		{
			FlexGrow = 1f,
			MinHeight = 80f
		};

		_tree.SelectionChanged += OnTreeSelectionChanged;
		_tree.ItemActivated += OnItemActivated;
		_tree.ItemDropped += OnItemDropped;
		_tree.ContextMenuRequested += OnContextMenuRequested;

		_empty = new EmptyStateView(
			UiIcon.Hierarchy,
			"No objects yet",
			"Create one with the + button, the GameObject menu, or right-click here.");

		_emptyHost = new Panel
		{
			DrawBackground = false,
			FlexGrow = 1f,
			MinHeight = 80f
		};

		_emptyHost.Add(_empty);

		stack.Add(header);
		stack.Add(_search);
		stack.Add(_tree);
		stack.Add(_emptyHost);

		Add(stack);

		_session.ProjectChanged += OnSessionChanged;
		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;

		RefreshStructure(force: true);
		UpdateActionStates();
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

	private void CreateEntity()
	{
		Guard(() => _session.CreateEntity(UniqueName("GameObject")));
	}

	private void CreatePrimitive(
		string name,
		string mesh,
		Guid? parentId = null)
	{
		Guard(() =>
		{
			SceneDocument scene = _session.Scene ??
				throw new InvalidOperationException("No scene is open.");

			AddEntityCommand add = new(scene, UniqueName(name), parentId);

			AddComponentCommand renderer = new(
				scene,
				add.Entity.Id,
				new MeshRendererData
				{
					Mesh = mesh,
					Material = "builtin:default"
				});

			_session.Undo.Execute(new CompositeCommand(
				$"Create {name}",
				[add, renderer]));

			_session.Select(add.Entity.Id);
		});
	}

	private void Duplicate()
	{
		Guard(() => _session.DuplicateSelection());
	}

	private void Delete()
	{
		Guard(() => _session.DeleteSelection());
	}

	private void Guard(Action action)
	{
		try
		{
			action();
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private string UniqueName(string baseName)
	{
		if (_session.Scene is not SceneDocument scene)
		{
			return baseName;
		}

		int suffix = 1;

		while (scene.Entities.Any(entity => entity.Name ==
			(suffix == 1 ? baseName : $"{baseName} {suffix}")))
		{
			suffix++;
		}

		return suffix == 1 ? baseName : $"{baseName} {suffix}";
	}

	private void OnSessionChanged(EditorSession session)
	{
		UpdateActionStates();
		RefreshStructure(force: false);
	}

	/// <summary>
	/// Keeps the header's selection commands disabled while there is
	/// nothing for them to act on.
	/// </summary>
	private void UpdateActionStates()
	{
		bool hasSelection = _session.Selection.Count > 0;

		_duplicateButton.IsEnabled = hasSelection;
		_deleteButton.IsEnabled = hasSelection;
	}

	private void RefreshStructure(bool force)
	{
		if (_updating)
		{
			return;
		}

		SceneDocument? scene = _session.Scene;
		string signature = scene is null
			? "none"
			: BuildSignature(scene, _search.Text);

		if (!force && signature == _signature)
		{
			SyncSelection();
			return;
		}

		_signature = signature;
		_updating = true;

		CaptureExpansion();

		_tree.Clear();
		_items.Clear();

		if (scene is null)
		{
			_empty.Title = "No scene open";
			_empty.Description =
				"Create a project from the File menu to start authoring.";

			_emptyHost.IsVisible = true;
			_tree.IsVisible = false;
			_search.IsVisible = false;
			_updating = false;
			return;
		}

		string filter = _search.Text.Trim();

		foreach (SceneEntity root in scene.GetChildren(null))
		{
			TreeViewItem? item = BuildItem(scene, root, filter);

			if (item is not null)
			{
				_tree.AddRoot(item);
			}
		}

		bool empty = _tree.Roots.Count == 0;

		_empty.Title = filter.Length > 0
			? "No matches"
			: "Scene is empty";

		_empty.Description = filter.Length > 0
			? $"No object matches \"{filter}\"."
			: "Create an object to begin building this scene.";

		_emptyHost.IsVisible = empty;
		_tree.IsVisible = !empty;
		_search.IsVisible = true;

		SyncSelection();
		_updating = false;
	}

	private TreeViewItem? BuildItem(
		SceneDocument scene,
		SceneEntity entity,
		string filter)
	{
		List<TreeViewItem> children = [];

		foreach (SceneEntity childEntity in scene.GetChildren(entity.Id))
		{
			TreeViewItem? child = BuildItem(
				scene,
				childEntity,
				filter);

			if (child is not null)
			{
				children.Add(child);
			}
		}

		bool matches = filter.Length == 0 ||
			entity.Name.Contains(
				filter,
				StringComparison.OrdinalIgnoreCase);

		if (!matches && children.Count == 0)
		{
			return null;
		}

		UiPalette palette =
			Context?.Theme.Palette ?? UiTheme.CreateDark().Palette;

		TreeViewItem item = new(entity.Id.ToString(), entity.Name, entity.Id)
		{
			IsExpanded = filter.Length > 0 ||
				_expanded.Contains(entity.Id) ||
				!_known.Contains(entity.Id),
			Icon = EditorIcons.ForEntity(entity),
			IconColor = EditorIcons.EntityColor(entity, palette)
		};

		foreach (TreeViewItem child in children)
		{
			item.AddChild(child);
		}

		_items[entity.Id] = item;
		return item;
	}

	private void CaptureExpansion()
	{
		foreach (KeyValuePair<Guid, TreeViewItem> pair in _items)
		{
			_known.Add(pair.Key);

			if (pair.Value.IsExpanded)
			{
				_expanded.Add(pair.Key);
			}

			else
			{
				_expanded.Remove(pair.Key);
			}
		}
	}

	private void SyncSelection()
	{
		if (_updating)
		{
			return;
		}

		_updating = true;

		List<TreeViewItem> selected = [];

		foreach (Guid id in _session.Selection)
		{
			if (_items.TryGetValue(id, out TreeViewItem? item))
			{
				selected.Add(item);
				_tree.Reveal(item);
			}
		}

		_tree.SetSelection(selected);
		_updating = false;
	}

	private void OnTreeSelectionChanged(
		TreeView tree,
		IReadOnlyList<TreeViewItem> selection)
	{
		if (_updating)
		{
			return;
		}

		Guid? active = selection.Count > 0 &&
			selection[^1].Tag is Guid id
				? id
				: null;

		_session.SelectMany(
			selection
				.Select(item => item.Tag)
				.OfType<Guid>(),
			active);
	}

	private void OnItemActivated(TreeView tree, TreeViewItem item)
	{
		if (item.Tag is Guid id)
		{
			_session.Select(id);
		}
	}

	/// <summary>
	/// Moves the dragged row, or the whole selection it belongs to,
	/// under the row it was dropped on.
	/// </summary>
	private void OnItemDropped(
		TreeView tree,
		TreeViewItem item,
		TreeViewItem? target)
	{
		if (item.Tag is not Guid sourceId)
		{
			return;
		}

		Guid? parentId = target?.Tag is Guid parent ? parent : null;

		Guard(() =>
		{
			SceneDocument scene = _session.Scene ??
				throw new InvalidOperationException("No scene is open.");

			IReadOnlyList<Guid> moved = _session.Selection.Contains(sourceId)
				? [.. SceneSelection.Roots(scene, _session.Selection)
					.Select(entity => entity.Id)]
				: [sourceId];

			_session.ReparentEntities(moved, parentId);
		});
	}

	private void OnContextMenuRequested(
		TreeView tree,
		TreeViewItem item,
		UiPoint position)
	{
		if (item.Tag is not Guid id)
		{
			return;
		}

		// Acting on a row that is already part of a multi-selection
		// keeps that selection, so a command reaches all of it.
		if (!_session.Selection.Contains(id))
		{
			_session.Select(id);
		}

		MenuItem rename = new("Rename", () =>
		{
			// Keep a multi-selection, but make the row the command
			// came from the object the inspector renames.
			_session.SelectMany(_session.Selection, id);
			RenameRequested?.Invoke();
		});

		MenuItem duplicate = new("Duplicate", Duplicate);
		MenuItem delete = new("Delete", Delete);
		MenuItem child = new(
			"Create Child Cube",
			() => CreatePrimitive("Cube", "builtin:cube", id));

		MenuPopup menu = new(
		[
			rename,
			duplicate,
			delete,
			MenuItem.Separator,
			child
		]);

		Context?.ShowPopup(
			menu,
			new UiRect(position.X, position.Y, 0f, 0f));
	}

	private static string BuildSignature(
		SceneDocument scene,
		string filter)
	{
		System.Text.StringBuilder builder = new();
		builder.Append(filter).Append('|');

		foreach (SceneEntity entity in scene.Entities)
		{
			builder
				.Append(entity.Id)
				.Append(':')
				.Append(entity.Name)
				.Append(':')
				.Append(entity.ParentId)
				.Append(';');
		}

		return builder.ToString();
	}

	private void Unsubscribe()
	{
		_session.ProjectChanged -= OnSessionChanged;
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
	}
}
