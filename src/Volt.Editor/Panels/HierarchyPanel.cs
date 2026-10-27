using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

public sealed class HierarchyPanel : Panel
{
	private readonly EditorSession _session;
	private readonly TreeView _tree;
	private readonly TextField _search;
	private readonly StackPanel _toolbar;
	private readonly Label _empty;
	private readonly Dictionary<Guid, TreeViewItem> _items = [];

	private readonly HashSet<Guid> _expanded = [];
	private readonly HashSet<Guid> _known = [];
	private string _signature = string.Empty;
	private bool _updating;
	private bool _disposed;

	public HierarchyPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(6f);

		StackPanel stack = new()
		{
			Spacing = 4f
		};

		_search = new TextField
		{
			Placeholder = "Search hierarchy",
			Height = UiLength.Points(26f)
		};

		_search.TextChanged += _ => RefreshStructure(force: true);
		stack.Add(_search);

		_toolbar = new StackPanel(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_toolbar.Add(CreateButton("+", "Create empty entity", CreateEntity));
		_toolbar.Add(CreateButton("Dup", "Duplicate selection", Duplicate));
		_toolbar.Add(CreateButton("Del", "Delete selection", Delete));
		stack.Add(_toolbar);

		_tree = new TreeView
		{
			FlexGrow = 1f,
			MinHeight = 80f
		};

		_tree.SelectionChanged += OnTreeSelectionChanged;
		_tree.ItemActivated += OnItemActivated;

		stack.Add(_tree);

		_empty = new Label("No scene objects")
		{
			IsHitTestVisible = false,
			TextColor = Context?.Theme.Palette.TextDisabled,
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center
		};

		Add(stack);
		Add(_empty);

		_session.ProjectChanged += OnSessionChanged;
		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;

		RefreshStructure(force: true);
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

	private static Button CreateButton(
		string text,
		string tooltip,
		Action action)
	{
		Button button = new(text)
		{
			TooltipText = tooltip,
			MinWidth = 30f,
			Height = UiLength.Points(24f)
		};

		button.Clicked += _ => action();
		return button;
	}

	private void CreateEntity()
	{
		try
		{
			Guid? parent = _session.ActiveEntityId;
			_session.CreateEntity(UniqueName("GameObject"), parent);
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void Duplicate()
	{
		try
		{
			_session.DuplicateActiveEntity();
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void Delete()
	{
		try
		{
			_session.DeleteActiveEntity();
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
		RefreshStructure(force: false);
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
			_empty.IsVisible = true;
			_empty.Text = "No project open";
			_tree.IsVisible = false;
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

		_empty.Text = scene.EntityCount == 0
			? "No scene objects"
			: "No objects match the search";

		_empty.IsVisible = _tree.Roots.Count == 0;
		_tree.IsVisible = _tree.Roots.Count > 0;

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

		TreeViewItem item = new(entity.Id.ToString(), entity.Name, entity.Id)
		{
			IsExpanded = filter.Length > 0 ||
				_expanded.Contains(entity.Id) ||
				!_known.Contains(entity.Id)
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
