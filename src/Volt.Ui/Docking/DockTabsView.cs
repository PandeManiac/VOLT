using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Ui.Docking;

/// <summary>
/// Visualizes one tab group in a dock layout.
/// </summary>
internal sealed class DockTabsView : Widget
{
	private readonly DockTabsNode _node;
	private readonly Dictionary<string, Widget> _contents;
	private readonly Func<string, string> _title;
	private readonly Action<string> _activate;
	private readonly Action<string> _close;
	private readonly Action<DockTabsView, string, UiPoint> _dragCompleted;
	private readonly Panel _contentHost;
	private readonly List<UiTextLayoutResult> _layouts = [];
	private readonly List<UiRect> _tabRects = [];
	private readonly List<UiRect> _closeRects = [];

	private UiFont? _layoutFont;
	private float _tabHeight;
	private string? _dragPanel;
	private UiPoint _dragStart;
	private bool _dragging;

	public DockTabsView(
		DockTabsNode node,
		IEnumerable<KeyValuePair<string, Widget>> contents,
		Func<string, string> title,
		Action<string> activate,
		Action<string> close,
		Action<DockTabsView, string, UiPoint> dragCompleted)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(contents);
		ArgumentNullException.ThrowIfNull(title);
		ArgumentNullException.ThrowIfNull(activate);
		ArgumentNullException.ThrowIfNull(close);
		ArgumentNullException.ThrowIfNull(dragCompleted);

		_node = node;
		_title = title;
		_activate = activate;
		_close = close;
		_dragCompleted = dragCompleted;
		_contents = new Dictionary<string, Widget>(StringComparer.Ordinal);

		IsFocusable = true;
		ClipToBounds = true;

		_contentHost = new Panel
		{
			DrawBackground = false,
			ClipToBounds = true,
			IsHitTestVisible = false
		};

		Add(_contentHost);

		foreach (KeyValuePair<string, Widget> pair in contents)
		{
			_contents[pair.Key] = pair.Value;
			_contentHost.Add(pair.Value);
		}

		UpdateContentVisibility();
	}

	internal DockTabsNode Node => _node;

	public void RefreshTitles()
	{
		_layouts.Clear();
		_tabRects.Clear();
		_closeRects.Clear();
		InvalidateMeasure();
		InvalidateVisual();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		_tabHeight = theme.Metrics.TabHeight;

		UiFont font = theme.Fonts.Body;
		EnsureLayouts(font);

		float width = float.IsFinite(contentAvailable.Width)
			? contentAvailable.Width
			: 240f;

		float height = float.IsFinite(contentAvailable.Height)
			? contentAvailable.Height
			: 180f;

		UiSize host = MeasureChild(
			_contentHost,
			new UiSize(
				width,
				MathF.Max(0f, height - _tabHeight)));

		return new UiSize(
			width,
			MathF.Max(height, _tabHeight + host.Height));
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		float header = MathF.Min(_tabHeight, contentBounds.Height);

		ArrangeChildExact(
			_contentHost,
			new UiRect(
				contentBounds.X,
				contentBounds.Y + header,
				contentBounds.Width,
				MathF.Max(0f, contentBounds.Height - header)));

		UpdateTabRects(GlobalVisualBounds, header);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		UiRect bounds = GlobalVisualBounds;
		float header = MathF.Min(_tabHeight, bounds.Height);

		EnsureLayouts(font);
		UpdateTabRects(bounds, header);

		drawList.AddRect(bounds, theme.Palette.Surface);
		drawList.AddRect(
			new UiRect(bounds.X, bounds.Y, bounds.Width, header),
			theme.Palette.Header);

		if (_node.PanelIds.Count == 0)
		{
			UiTextLayoutResult empty = UiTextLayout.Create(
				"Empty dock group",
				font,
				MathF.Max(0f, bounds.Width - 16f),
				UiTextWrap.NoWrap,
				UiTextAlignment.Start);

			drawList.AddText(
				empty,
				new UiPoint(bounds.X + 8f, bounds.Y + 6f),
				theme.Palette.TextDisabled);

			return;
		}

		for (int index = 0; index < _node.PanelIds.Count; index++)
		{
			string id = _node.PanelIds[index];
			UiRect tab = _tabRects[index];
			bool selected = id == _node.ActivePanelId;

			if (selected)
			{
				drawList.AddRect(tab, theme.Palette.SurfaceRaised);
				drawList.AddRect(
					new UiRect(tab.X, tab.Y, tab.Width, 2f),
					theme.Palette.Accent);
			}

			UiColor foreground = selected
				? theme.Palette.Text
				: theme.Palette.TextMuted;

			UiTextLayoutResult layout = _layouts[index];

			drawList.AddText(
				layout,
				new UiPoint(
					tab.X + 9f,
					tab.Y + ((header - layout.Height) * 0.5f)),
				foreground);

			UiRect close = _closeRects[index];
			UiPoint center = close.Center;

			drawList.AddLine(
				new UiPoint(center.X - 3.5f, center.Y - 3.5f),
				new UiPoint(center.X + 3.5f, center.Y + 3.5f),
				1.2f,
				foreground);

			drawList.AddLine(
				new UiPoint(center.X + 3.5f, center.Y - 3.5f),
				new UiPoint(center.X - 3.5f, center.Y + 3.5f),
				1.2f,
				foreground);
		}

		drawList.AddRect(
			new UiRect(bounds.X, bounds.Y + header - 1f, bounds.Width, 1f),
			theme.Palette.Border);

		if (_dragging && _dragPanel is not null)
		{
			UiPoint pointer = Context?.PointerPosition ?? default;
			UiTextLayoutResult label = UiTextLayout.Create(
				_title(_dragPanel),
				font,
				240f,
				UiTextWrap.NoWrap,
				UiTextAlignment.Start);

			UiRect ghost = new(
				pointer.X + 12f,
				pointer.Y + 12f,
				label.Width + 14f,
				label.Height + 8f);

			drawList.AddRoundedRect(
				ghost,
				theme.Metrics.SmallCornerRadius,
				theme.Palette.SurfaceOverlay);

			drawList.AddText(
				label,
				new UiPoint(ghost.X + 7f, ghost.Y + 4f),
				theme.Palette.Text);
		}
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		UpdateTabRects(GlobalVisualBounds, MathF.Min(
			_tabHeight,
			GlobalVisualBounds.Height));

		for (int index = 0; index < _node.PanelIds.Count; index++)
		{
			string id = _node.PanelIds[index];
			UiRect close = _closeRects[index];

			if (close.Contains(pointer.GlobalPosition))
			{
				_close(id);
				return true;
			}

			if (!_tabRects[index].Contains(pointer.GlobalPosition))
			{
				continue;
			}

			Focus();
			_activate(id);
			_dragPanel = id;
			_dragStart = pointer.GlobalPosition;
			_dragging = false;
			CapturePointer(pointer.Button);
			return true;
		}

		return false;
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (_dragPanel is null)
		{
			return false;
		}

		if (!_dragging)
		{
			float dx = pointer.GlobalPosition.X - _dragStart.X;
			float dy = pointer.GlobalPosition.Y - _dragStart.Y;

			_dragging = (dx * dx) + (dy * dy) > 36f;
		}

		if (_dragging)
		{
			InvalidateVisual();
		}

		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (_dragPanel is null)
		{
			return false;
		}

		string id = _dragPanel;
		bool dragged = _dragging;

		_dragPanel = null;
		_dragging = false;
		ReleasePointer();

		if (dragged)
		{
			_dragCompleted(this, id, pointer.GlobalPosition);
		}

		InvalidateVisual();
		return true;
	}

	protected override void OnDetached()
	{
		_dragPanel = null;
		_dragging = false;
	}

	internal int GetDropIndex(UiPoint point)
	{
		for (int index = 0; index < _tabRects.Count; index++)
		{
			UiRect rect = _tabRects[index];

			if (point.X < rect.Center.X)
			{
				return index;
			}
		}

		return _tabRects.Count;
	}

	private void UpdateContentVisibility()
	{
		string? active = _node.ActiveOrFirst;

		foreach (KeyValuePair<string, Widget> pair in _contents)
		{
			pair.Value.IsVisible = pair.Key == active;
		}
	}

	private void EnsureLayouts(UiFont font)
	{
		if (_layoutFont == font &&
			_layouts.Count == _node.PanelIds.Count)
		{
			return;
		}

		_layouts.Clear();
		_tabRects.Clear();
		_closeRects.Clear();

		foreach (string id in _node.PanelIds)
		{
			_layouts.Add(
				UiTextLayout.Create(
					_title(id),
					font,
					400f,
					UiTextWrap.NoWrap,
					UiTextAlignment.Start));
		}

		_layoutFont = font;
	}

	private void UpdateTabRects(UiRect bounds, float header)
	{
		_tabRects.Clear();
		_closeRects.Clear();

		float x = bounds.X;

		for (int index = 0; index < _node.PanelIds.Count; index++)
		{
			float textWidth = index < _layouts.Count
				? _layouts[index].Width
				: 80f;

			float width = MathF.Max(72f, textWidth + 34f);

			if (x + width > bounds.Right)
			{
				width = MathF.Max(0f, bounds.Right - x);
			}

			UiRect tab = new(x, bounds.Y, width, header);
			_tabRects.Add(tab);

			_closeRects.Add(
				new UiRect(
					tab.Right - 22f,
					tab.Y + ((header - 16f) * 0.5f),
					16f,
					16f));

			x += width;

			if (x >= bounds.Right)
			{
				break;
			}
		}
	}
}
