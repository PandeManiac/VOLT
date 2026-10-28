using Volt.Ui.Layout;

namespace Volt.Ui.Docking;

/// <summary>
/// A node in a dock layout tree.
/// </summary>
public abstract class DockNode
{
}

/// <summary>
/// Two dock regions separated by one draggable splitter.
/// </summary>
public sealed class DockSplitNode : DockNode
{
	private float _ratio = 0.5f;

	public DockSplitNode(UiOrientation orientation)
	{
		Orientation = orientation;
	}

	public DockSplitNode(
		UiOrientation orientation,
		DockNode first,
		DockNode second,
		float ratio = 0.5f)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);

		Orientation = orientation;
		First = first;
		Second = second;
		Ratio = ratio;
	}

	public UiOrientation Orientation { get; }

	public DockNode? First { get; internal set; }

	public DockNode? Second { get; internal set; }

	/// <summary>
	/// Gets or sets the fraction of space assigned to
	/// <see cref="First"/>, clamped to a usable range.
	/// </summary>
	public float Ratio
	{
		get => _ratio;
		set => _ratio = Math.Clamp(value, 0.05f, 0.95f);
	}
}

/// <summary>
/// A tab group containing one or more panels.
/// </summary>
public sealed class DockTabsNode : DockNode
{
	public DockTabsNode()
	{
	}

	public DockTabsNode(IEnumerable<string> panelIds)
	{
		ArgumentNullException.ThrowIfNull(panelIds);
		PanelIds.AddRange(panelIds);

		if (PanelIds.Count > 0)
		{
			ActivePanelId = PanelIds[0];
		}
	}

	public List<string> PanelIds { get; } = [];

	public string? ActivePanelId { get; set; }

	public bool IsEmpty => PanelIds.Count == 0;

	public string? ActiveOrFirst =>
		ActivePanelId is not null && PanelIds.Contains(ActivePanelId)
			? ActivePanelId
			: PanelIds.Count > 0
				? PanelIds[0]
				: null;
}
