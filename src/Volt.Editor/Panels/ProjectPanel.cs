using System.Globalization;
using Volt.Editor.Core.Projects;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

public sealed class ProjectPanel : Panel
{
	private readonly EditorSession _session;
	private readonly TreeView _tree;
	private readonly TextField _search;
	private readonly Label _details;
	private readonly Label _empty;
	private readonly StackPanel _stack;

	private string _signature = string.Empty;
	private bool _updating;
	private bool _disposed;

	public ProjectPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(6f);

		_stack = new StackPanel
		{
			Spacing = 4f
		};

		StackPanel toolbar = new(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		Button refresh = new("Refresh")
		{
			MinHeight = 24f
		};

		refresh.Clicked += _ => RefreshCatalog();

		Button newScene = new("New Scene")
		{
			MinHeight = 24f
		};

		newScene.Clicked += _ => CreateScene();

		toolbar.Add(refresh);
		toolbar.Add(newScene);

		_search = new TextField
		{
			Placeholder = "Search assets",
			Height = UiLength.Points(26f)
		};

		_search.TextChanged += _ => Rebuild(force: true);

		_tree = new TreeView
		{
			FlexGrow = 1f,
			MinHeight = 80f
		};

		_tree.ItemActivated += OnItemActivated;
		_tree.SelectionChanged += OnSelectionChanged;

		_details = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small,
			Height = UiLength.Points(22f),
			ClipToBounds = true
		};

		_empty = new Label("No project assets")
		{
			IsHitTestVisible = false,
			HorizontalAlignment = UiAlignment.Center,
			VerticalAlignment = UiAlignment.Center,
			TextAlignment = Volt.Ui.Text.UiTextAlignment.Center
		};

		_stack.Add(toolbar);
		_stack.Add(_search);
		_stack.Add(_tree);
		_stack.Add(_details);

		Add(_stack);
		Add(_empty);

		_session.ProjectChanged += OnSessionChanged;
		_session.SceneChanged += OnSessionChanged;

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
			_session.CreateScene(relative, Path.GetFileNameWithoutExtension(
				Path.GetFileNameWithoutExtension(relative)));
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void Rebuild(bool force)
	{
		if (_updating)
		{
			return;
		}

		EditorProject? project = _session.Project;

		if (project is null)
		{
			_signature = "none";
			_updating = true;
			_tree.Clear();
			_tree.IsVisible = false;
			_empty.IsVisible = true;
			_empty.Text = "No project open";
			_details.Text = string.Empty;
			_updating = false;
			return;
		}

		string signature = BuildSignature(project, _search.Text);

		if (!force && signature == _signature)
		{
			return;
		}

		_signature = signature;
		_updating = true;
		_tree.Clear();

		string filter = _search.Text.Trim();

		AssetTreeNode root = new(string.Empty, string.Empty);

		foreach (ProjectAsset asset in project.AssetCatalog.Assets)
		{
			if (filter.Length > 0 &&
				!asset.FileName.Contains(
					filter,
					StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			root.Add(asset);
		}

		foreach (AssetTreeNode child in root.Children.Values)
		{
			TreeViewItem? item = BuildItem(child);

			if (item is not null)
			{
				_tree.AddRoot(item);
			}
		}

		_empty.IsVisible = _tree.Roots.Count == 0;
		_empty.Text = project.AssetCatalog.Assets.Count == 0
			? "No assets yet"
			: "No assets match the search";
		_tree.IsVisible = _tree.Roots.Count > 0;
		_details.Text = $"{project.AssetCatalog.Assets.Count} assets";
		_updating = false;
	}

	private static TreeViewItem? BuildItem(AssetTreeNode node)
	{
		if (node.Asset is not null)
		{
			TreeViewItem assetItem = new(
				node.Path,
				node.Asset.FileName,
				node.Asset)
			{
				IconColor = KindColor(node.Asset.Kind)
			};

			return assetItem;
		}

		TreeViewItem folder = new(
			node.Path,
			node.Name,
			node.Path)
		{
			IsExpanded = true,
			IconColor = null
		};

		foreach (AssetTreeNode child in node.Children.Values
			.OrderBy(entry => entry.Asset is null ? 0 : 1)
			.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
		{
			TreeViewItem? childItem = BuildItem(child);

			if (childItem is not null)
			{
				folder.AddChild(childItem);
			}
		}

		return folder.Children.Count == 0 ? null : folder;
	}

	private static Volt.Ui.UiColor? KindColor(ProjectAssetKind kind)
	{
		return kind switch
		{
			ProjectAssetKind.Scene => Volt.Ui.UiColor.FromRgb(0x45C2E6),
			ProjectAssetKind.Mesh => Volt.Ui.UiColor.FromRgb(0x4FD1A1),
			ProjectAssetKind.Material => Volt.Ui.UiColor.FromRgb(0xE6B450),
			ProjectAssetKind.Texture => Volt.Ui.UiColor.FromRgb(0xB08CFF),
			ProjectAssetKind.Script => Volt.Ui.UiColor.FromRgb(0x8CBF7A),
			_ => null
		};
	}

	private void OnItemActivated(TreeView tree, TreeViewItem item)
	{
		if (item.Tag is not ProjectAsset asset)
		{
			return;
		}

		if (asset.Kind == ProjectAssetKind.Scene)
		{
			try
			{
				_session.OpenScene(asset.Id);
			}

			catch (Exception exception)
			{
				_session.Log(exception.Message, EditorLogLevel.Error);
			}
		}
	}

	private void OnSelectionChanged(
		TreeView tree,
		IReadOnlyList<TreeViewItem> selection)
	{
		if (selection.Count == 0 ||
			selection[^1].Tag is not ProjectAsset asset)
		{
			_details.Text = string.Empty;
			return;
		}

		_details.Text =
			$"{asset.Kind}  ·  {asset.Id}  ·  " +
			$"{FormatSize(asset.Length)}";
	}

	private static string FormatSize(long bytes)
	{
		return bytes < 1024
			? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
			: $"{bytes / 1024.0:0.#} KiB";
	}

	private static string BuildSignature(
		EditorProject project,
		string filter)
	{
		System.Text.StringBuilder builder = new();
		builder.Append(filter).Append('|');

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

	private sealed class AssetTreeNode
	{
		private readonly Dictionary<string, AssetTreeNode> _children =
			new(StringComparer.OrdinalIgnoreCase);

		public AssetTreeNode(string path, string name)
		{
			Path = path;
			Name = name;
		}

		public string Path { get; }

		public string Name { get; }

		public ProjectAsset? Asset { get; private set; }

		public IReadOnlyDictionary<string, AssetTreeNode> Children =>
			_children;

		public void Add(ProjectAsset asset)
		{
			string[] segments = asset.Id.Split('/');
			AssetTreeNode current = this;

			for (int index = 0; index < segments.Length; index++)
			{
				string segment = segments[index];
				string path = index == 0
					? segment
					: $"{current.Path}/{segment}";

				if (!current._children.TryGetValue(
					segment,
					out AssetTreeNode? child))
				{
					child = new AssetTreeNode(path, segment);
					current._children.Add(segment, child);
				}

				current = child;
			}

			current.Asset = asset;
		}
	}
}
