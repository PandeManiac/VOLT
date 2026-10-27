using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Ui.Docking;

/// <summary>
/// Renders a <see cref="DockLayout"/> using Volt.Ui widgets.
/// </summary>
/// <remarks>
/// Panel content is created lazily by the supplied factory and cached
/// for the lifetime of the host, so moving or hiding a panel does not
/// destroy its state. Structural layout changes rebuild only the dock
/// chrome; the content widgets are re-parented without being disposed.
/// </remarks>
public sealed class DockHost : Widget
{
	private readonly Dictionary<string, Widget> _contents =
		new(StringComparer.Ordinal);

	private readonly Func<string, Widget?> _contentFactory;
	private Widget? _rootView;
	private bool _rebuildPending;

	public DockHost(
		DockLayout layout,
		Func<string, Widget?> contentFactory)
	{
		ArgumentNullException.ThrowIfNull(layout);
		ArgumentNullException.ThrowIfNull(contentFactory);

		Layout = layout;
		_contentFactory = contentFactory;

		ClipToBounds = true;
		IsHitTestVisible = false;

		Layout.Changed += OnLayoutChanged;
		Rebuild();
	}

	public DockLayout Layout { get; }

	/// <summary>
	/// Requests a structural refresh, for example after a panel title
	/// changed without a layout change.
	/// </summary>
	public void Refresh()
	{
		_rebuildPending = true;
		InvalidateMeasure();
	}

	public override void Dispose()
	{
		Layout.Changed -= OnLayoutChanged;

		foreach (Widget content in _contents.Values)
		{
			if (!content.IsDisposed && content.Parent is null)
			{
				content.Dispose();
			}
		}

		_contents.Clear();
		base.Dispose();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		if (_rebuildPending)
		{
			Rebuild();
		}

		if (_rootView is null)
		{
			return new UiSize(
				float.IsFinite(contentAvailable.Width)
					? contentAvailable.Width
					: 0f,
				float.IsFinite(contentAvailable.Height)
					? contentAvailable.Height
					: 0f);
		}

		UiSize size = MeasureChild(_rootView, contentAvailable);
		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: size.Width,
			float.IsFinite(contentAvailable.Height)
				? contentAvailable.Height
				: size.Height);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		if (_rootView is not null)
		{
			ArrangeChildExact(_rootView, contentBounds);
		}
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		drawList.AddRect(GlobalVisualBounds, theme.Palette.WindowBackground);
	}

	private void OnLayoutChanged(DockLayout layout)
	{
		Refresh();
	}

	private void Rebuild()
	{
		_rebuildPending = false;

		if (_rootView is not null)
		{
			foreach (Widget content in _contents.Values)
			{
				if (content.Parent is not null)
				{
					content.RemoveFromParent();
				}
			}

			Remove(_rootView);
			_rootView.Dispose();
			_rootView = null;
		}

		if (Layout.Root is not null)
		{
			_rootView = CreateNode(Layout.Root);
			Add(_rootView);
		}

		InvalidateMeasure();
	}

	private Widget CreateNode(DockNode node)
	{
		return node switch
		{
			DockTabsNode tabs => CreateTabs(tabs),
			DockSplitNode split => new DockSplitView(
				split,
				CreateNode(split.First ??
					throw new InvalidOperationException(
						"A dock split has no first child.")),
				CreateNode(split.Second ??
					throw new InvalidOperationException(
						"A dock split has no second child."))),
			_ => throw new InvalidOperationException(
				$"Unsupported dock node {node.GetType().Name}.")
		};
	}

	private DockTabsView CreateTabs(DockTabsNode node)
	{
		Dictionary<string, Widget> contents =
			new(StringComparer.Ordinal);

		foreach (string id in node.PanelIds)
		{
			contents[id] = GetContent(id);
		}

		return new DockTabsView(
			node,
			contents,
			GetTitle,
			Activate,
			Close,
			CompleteDrag);
	}

	private string GetTitle(string panelId)
	{
		return Layout.Panels.TryGetValue(panelId, out DockPanel? panel)
			? panel.Title
			: panelId;
	}

	private Widget GetContent(string panelId)
	{
		if (_contents.TryGetValue(panelId, out Widget? existing))
		{
			return existing;
		}

		Widget? created = _contentFactory(panelId);

		Widget content = created ?? new Label
		{
			Text = $"No content for '{panelId}'.",
			HorizontalAlignment = Volt.Ui.Layout.UiAlignment.Center,
			VerticalAlignment = Volt.Ui.Layout.UiAlignment.Center,
			TextAlignment = Volt.Ui.Text.UiTextAlignment.Center
		};

		_contents[panelId] = content;
		return content;
	}

	private void Activate(string panelId)
	{
		Layout.ActivatePanel(panelId);
	}

	private void Close(string panelId)
	{
		Layout.ClosePanel(panelId);
	}

	private void CompleteDrag(
		DockTabsView source,
		string panelId,
		UiPoint position)
	{
		Widget? hit = Context?.FindWidgetAt(position);

		while (hit is not null && hit is not DockTabsView)
		{
			hit = hit.Parent;
		}

		if (hit is not DockTabsView target || target == source)
		{
			return;
		}

		int index = target.GetDropIndex(position);
		Layout.MovePanel(panelId, target.Node, index);
	}
}
