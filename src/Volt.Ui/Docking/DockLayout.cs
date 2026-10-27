using System.Text.Json;
using Volt.Ui.Layout;

namespace Volt.Ui.Docking;

/// <summary>
/// A persistent tree of dock regions, tab groups, and panels.
/// </summary>
/// <remarks>
/// This is a pure model. It knows nothing about widgets, rendering,
/// the editor, or the filesystem. A <see cref="DockHost"/> renders
/// it, and an application serializes it as workspace state.
/// </remarks>
public sealed class DockLayout
{
	public const int FormatVersion = 1;

	private readonly Dictionary<string, DockPanel> _panels =
		new(StringComparer.Ordinal);

	public event Action<DockLayout>? Changed;

	public DockLayout(DockNode? root, IEnumerable<DockPanel> panels)
	{
		ArgumentNullException.ThrowIfNull(panels);
		Root = root;

		foreach (DockPanel panel in panels)
		{
			if (!_panels.TryAdd(panel.Id, panel))
			{
				throw new InvalidOperationException(
					$"Duplicate dock panel identifier '{panel.Id}'.");
			}
		}

		Validate();
	}

	public DockNode? Root { get; private set; }

	public IReadOnlyDictionary<string, DockPanel> Panels => _panels;

	/// <summary>
	/// Creates the common editor layout: a left hierarchy, a central
	/// area split into scene/game tabs and bottom tabs, and a right
	/// inspector.
	/// </summary>
	public static DockLayout CreateEditorDefault()
	{
		DockPanel hierarchy = new("hierarchy", "Hierarchy");
		DockPanel scene = new("scene", "Scene");
		DockPanel game = new("game", "Game");
		DockPanel inspector = new("inspector", "Inspector");
		DockPanel project = new("project", "Project");
		DockPanel console = new("console", "Console");
		DockPanel profiler = new("profiler", "Profiler");

		DockTabsNode bottomTabs = new(["project", "console", "profiler"]);
		DockTabsNode centerTabs = new(["scene", "game"]);

		DockSplitNode center = new(
			UiOrientation.Vertical,
			centerTabs,
			bottomTabs,
			0.7f);

		DockSplitNode leftCenter = new(
			UiOrientation.Horizontal,
			new DockTabsNode(["hierarchy"]),
			center,
			0.2f);

		DockNode root = new DockSplitNode(
			UiOrientation.Horizontal,
			leftCenter,
			new DockTabsNode(["inspector"]),
			0.78f);

		DockLayout layout = new(
			root,
			[
				hierarchy,
				scene,
				game,
				inspector,
				project,
				console,
				profiler
			]);

		foreach (DockPanel panel in layout._panels.Values)
		{
			panel.IsOpen = layout.FindTabs(panel.Id) is not null;
		}

		return layout;
	}

	public DockTabsNode? FindTabs(string panelId)
	{
		ArgumentNullException.ThrowIfNull(panelId);
		return FindTabsCore(Root, panelId);
	}

	public bool IsOpen(string panelId)
	{
		return _panels.TryGetValue(panelId, out DockPanel? panel) &&
			panel.IsOpen;
	}

	public bool ClosePanel(string panelId)
	{
		DockTabsNode? tabs = FindTabs(panelId);

		if (tabs is null)
		{
			return false;
		}

		tabs.PanelIds.Remove(panelId);

		if (tabs.ActivePanelId == panelId)
		{
			tabs.ActivePanelId = tabs.PanelIds.Count > 0
				? tabs.PanelIds[0]
				: null;
		}

		if (_panels.TryGetValue(panelId, out DockPanel? panel))
		{
			panel.IsOpen = false;
		}

		RemoveEmptyNodes();
		Changed?.Invoke(this);

		return true;
	}

	public bool OpenPanel(string panelId)
	{
		if (!_panels.TryGetValue(panelId, out DockPanel? panel))
		{
			return false;
		}

		DockTabsNode? existing = FindTabs(panelId);

		if (existing is not null)
		{
			existing.ActivePanelId = panelId;
			Changed?.Invoke(this);
			return true;
		}

		DockTabsNode? target = FindFirstTabs(Root);

		if (target is null)
		{
			Root = new DockTabsNode([panelId]);
		}

		else
		{
			target.PanelIds.Add(panelId);
			target.ActivePanelId = panelId;
		}

		panel.IsOpen = true;
		Changed?.Invoke(this);

		return true;
	}

	public bool ActivatePanel(string panelId)
	{
		DockTabsNode? tabs = FindTabs(panelId);

		if (tabs is null)
		{
			return false;
		}

		if (tabs.ActivePanelId == panelId)
		{
			return false;
		}

		tabs.ActivePanelId = panelId;
		Changed?.Invoke(this);

		return true;
	}

	/// <summary>
	/// Moves a panel into another tab group. Dropping a panel onto its
	/// own group reorders it.
	/// </summary>
	public bool MovePanel(
		string panelId,
		DockTabsNode target,
		int index = -1)
	{
		ArgumentNullException.ThrowIfNull(target);

		DockTabsNode? source = FindTabs(panelId);

		if (source is null)
		{
			return false;
		}

		if (source == target)
		{
			int current = source.PanelIds.IndexOf(panelId);
			source.PanelIds.RemoveAt(current);

			int destination = index < 0 || index > source.PanelIds.Count
				? source.PanelIds.Count
				: index;

			source.PanelIds.Insert(destination, panelId);
			source.ActivePanelId = panelId;
			Changed?.Invoke(this);

			return true;
		}

		source.PanelIds.Remove(panelId);

		if (source.ActivePanelId == panelId)
		{
			source.ActivePanelId = source.PanelIds.Count > 0
				? source.PanelIds[0]
				: null;
		}

		int targetIndex = index < 0 || index > target.PanelIds.Count
			? target.PanelIds.Count
			: index;

		target.PanelIds.Insert(targetIndex, panelId);
		target.ActivePanelId = panelId;

		RemoveEmptyNodes();
		Changed?.Invoke(this);

		return true;
	}

	public void SetRatio(DockSplitNode node, float ratio)
	{
		ArgumentNullException.ThrowIfNull(node);
		node.Ratio = ratio;
	}

	public string Serialize()
	{
		DockLayoutDto dto = new()
		{
			Version = FormatVersion,
			Panels = [.. _panels.Values
				.OrderBy(panel => panel.Id, StringComparer.Ordinal)
				.Select(panel => new DockPanelDto
				{
					Id = panel.Id,
					Title = panel.Title,
					IsOpen = panel.IsOpen
				})],
			Root = Root is null ? null : ToDto(Root)
		};

		return JsonSerializer.Serialize(
			dto,
			DockJson.Options);
	}

	public static DockLayout Deserialize(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);

		DockLayoutDto? dto;

		try
		{
			dto = JsonSerializer.Deserialize<DockLayoutDto>(
				json,
				DockJson.Options);
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				"The dock layout JSON is malformed.",
				exception);
		}

		if (dto is null)
		{
			throw new InvalidDataException(
				"The dock layout document is empty.");
		}

		if (dto.Version != FormatVersion)
		{
			throw new InvalidDataException(
				$"Unsupported dock layout version {dto.Version}.");
		}

		if (dto.Panels is null)
		{
			throw new InvalidDataException(
				"The dock layout has no panel list.");
		}

		List<DockPanel> panels = [];
		HashSet<string> ids = new(StringComparer.Ordinal);

		foreach (DockPanelDto panel in dto.Panels)
		{
			if (string.IsNullOrWhiteSpace(panel.Id) ||
				!ids.Add(panel.Id))
			{
				throw new InvalidDataException(
					"The dock layout contains an empty or duplicate " +
					"panel identifier.");
			}

			panels.Add(new DockPanel(panel.Id, panel.Title ?? panel.Id));
		}

		DockNode? root = dto.Root is null
			? null
			: FromDto(dto.Root, ids, 0);

		DockLayout layout = new(root, panels);
		layout.RestoreOpenState();

		return layout;
	}

	private void RestoreOpenState()
	{
		HashSet<string> open = new(StringComparer.Ordinal);
		CollectPanels(Root, open);

		foreach (DockPanel panel in _panels.Values)
		{
			panel.IsOpen = open.Contains(panel.Id);
		}
	}

	private static void CollectPanels(DockNode? node, HashSet<string> ids)
	{
		switch (node)
		{
			case DockTabsNode tabs:
				foreach (string id in tabs.PanelIds)
				{
					ids.Add(id);
				}

				break;

			case DockSplitNode split:
				CollectPanels(split.First, ids);
				CollectPanels(split.Second, ids);
				break;
		}
	}

	private void Validate()
	{
		HashSet<string> seen = new(StringComparer.Ordinal);
		ValidateNode(Root, seen, 0);

		foreach (string id in seen)
		{
			if (!_panels.ContainsKey(id))
			{
				throw new InvalidDataException(
					$"The dock layout references unknown panel '{id}'.");
			}
		}
	}

	private static void ValidateNode(
		DockNode? node,
		HashSet<string> seen,
		int depth)
	{
		if (depth > 64)
		{
			throw new InvalidDataException(
				"The dock layout tree is too deep.");
		}

		switch (node)
		{
			case null:
				return;

			case DockTabsNode tabs:
				foreach (string id in tabs.PanelIds)
				{
					if (string.IsNullOrWhiteSpace(id) ||
						!seen.Add(id))
					{
						throw new InvalidDataException(
							"The dock layout contains an empty or " +
							"duplicate panel reference.");
					}
				}

				if (tabs.ActivePanelId is not null &&
					!tabs.PanelIds.Contains(tabs.ActivePanelId))
				{
					tabs.ActivePanelId = tabs.PanelIds.Count > 0
						? tabs.PanelIds[0]
						: null;
				}

				break;

			case DockSplitNode split:
				if (split.First is null || split.Second is null)
				{
					throw new InvalidDataException(
						"A dock split must have two children.");
				}

				ValidateNode(split.First, seen, depth + 1);
				ValidateNode(split.Second, seen, depth + 1);
				break;
		}
	}

	private static DockTabsNode? FindTabsCore(
		DockNode? node,
		string panelId)
	{
		switch (node)
		{
			case DockTabsNode tabs:
				return tabs.PanelIds.Contains(panelId) ? tabs : null;

			case DockSplitNode split:
				return FindTabsCore(split.First, panelId) ??
					FindTabsCore(split.Second, panelId);

			default:
				return null;
		}
	}

	private static DockTabsNode? FindFirstTabs(DockNode? node)
	{
		switch (node)
		{
			case DockTabsNode tabs:
				return tabs;

			case DockSplitNode split:
				return FindFirstTabs(split.First) ??
					FindFirstTabs(split.Second);

			default:
				return null;
		}
	}

	private void RemoveEmptyNodes()
	{
		Root = RemoveEmpty(Root);

		while (Root is DockSplitNode split &&
			(split.First is null || split.Second is null))
		{
			Root = split.First ?? split.Second;
		}
	}

	private static DockNode? RemoveEmpty(DockNode? node)
	{
		switch (node)
		{
			case null:
				return null;

			case DockTabsNode tabs:
				return tabs.PanelIds.Count == 0 ? null : tabs;

			case DockSplitNode split:
				split.First = RemoveEmpty(split.First);
				split.Second = RemoveEmpty(split.Second);

				if (split.First is null)
				{
					return split.Second;
				}

				if (split.Second is null)
				{
					return split.First;
				}

				return split;

			default:
				return node;
		}
	}

	private static DockNodeDto ToDto(DockNode node)
	{
		return node switch
		{
			DockTabsNode tabs => new DockNodeDto
			{
				Kind = "tabs",
				Panels = [.. tabs.PanelIds],
				Active = tabs.ActivePanelId
			},
			DockSplitNode split => new DockNodeDto
			{
				Kind = "split",
				Orientation = split.Orientation.ToString(),
				Ratio = split.Ratio,
				First = split.First is null ? null : ToDto(split.First),
				Second = split.Second is null ? null : ToDto(split.Second)
			},
			_ => throw new InvalidOperationException(
				$"Unsupported dock node {node.GetType().Name}.")
		};
	}

	private static DockNode FromDto(
		DockNodeDto dto,
		HashSet<string> panels,
		int depth)
	{
		if (depth > 64)
		{
			throw new InvalidDataException(
				"The dock layout tree is too deep.");
		}

		if (dto.Kind == "tabs")
		{
			DockTabsNode tabs = new();

			if (dto.Panels is not null)
			{
				foreach (string id in dto.Panels)
				{
					if (string.IsNullOrWhiteSpace(id) ||
						!panels.Contains(id))
					{
						throw new InvalidDataException(
							$"Unknown dock panel '{id}'.");
					}

					tabs.PanelIds.Add(id);
				}
			}

			tabs.ActivePanelId = dto.Active is not null &&
				tabs.PanelIds.Contains(dto.Active)
				? dto.Active
				: tabs.PanelIds.Count > 0
					? tabs.PanelIds[0]
					: null;

			return tabs;
		}

		if (dto.Kind == "split")
		{
			if (dto.First is null || dto.Second is null)
			{
				throw new InvalidDataException(
					"A dock split is missing a child.");
			}

			if (!Enum.TryParse(
				dto.Orientation,
				ignoreCase: true,
				out UiOrientation orientation))
			{
				throw new InvalidDataException(
					$"Unknown dock orientation '{dto.Orientation}'.");
			}

			return new DockSplitNode(
				orientation,
				FromDto(dto.First, panels, depth + 1),
				FromDto(dto.Second, panels, depth + 1),
				float.IsFinite(dto.Ratio) ? dto.Ratio : 0.5f);
		}

		throw new InvalidDataException(
			$"Unknown dock node kind '{dto.Kind}'.");
	}

}

internal static class DockJson
{
	public static JsonSerializerOptions Options { get; } = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};
}

internal sealed class DockLayoutDto
{
	public int Version { get; set; }

	public List<DockPanelDto>? Panels { get; set; }

	public DockNodeDto? Root { get; set; }
}

internal sealed class DockPanelDto
{
	public string Id { get; set; } = string.Empty;

	public string? Title { get; set; }

	public bool IsOpen { get; set; }
}

internal sealed class DockNodeDto
{
	public string Kind { get; set; } = string.Empty;

	public string? Orientation { get; set; }

	public float Ratio { get; set; } = 0.5f;

	public DockNodeDto? First { get; set; }

	public DockNodeDto? Second { get; set; }

	public List<string>? Panels { get; set; }

	public string? Active { get; set; }
}
