using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A tab strip with a content area that shows one page at a time.
/// </summary>
public sealed class TabView : Widget
{
	private readonly List<TabPage> _pages = [];
	private readonly List<UiTextLayoutResult> _layouts = [];
	private readonly List<UiRect> _tabRects = [];
	private readonly List<UiRect> _closeRects = [];
	private readonly Panel _contentHost;

	private UiFont? _layoutFont;
	private int _selectedIndex = -1;
	private float _tabHeight;

	public TabView()
	{
		IsFocusable = true;
		ClipToBounds = true;
		_contentHost = new Panel
		{
			DrawBackground = false,
			ClipToBounds = true,
			IsHitTestVisible = false
		};

		Add(_contentHost);
	}

	public event Action<TabView, TabPage>? TabClosed;

	public event Action<TabView, TabPage>? SelectionChanged;

	public IReadOnlyList<TabPage> Pages => _pages;

	public int SelectedIndex => _selectedIndex;

	public TabPage? SelectedPage =>
		_selectedIndex >= 0 && _selectedIndex < _pages.Count
			? _pages[_selectedIndex]
			: null;

	public TabPage AddPage(TabPage page)
	{
		ArgumentNullException.ThrowIfNull(page);
		_pages.Add(page);
		_contentHost.Add(page.Content);
		page.Content.IsVisible = false;
		InvalidatePages();

		if (_selectedIndex < 0)
		{
			Select(0);
		}

		return page;
	}

	public TabPage AddPage(string id, string title, Widget content)
	{
		return AddPage(new TabPage(id, title, content));
	}

	public bool RemovePage(TabPage page)
	{
		ArgumentNullException.ThrowIfNull(page);

		int index = _pages.IndexOf(page);

		if (index < 0)
		{
			return false;
		}

		_pages.RemoveAt(index);
		_contentHost.Remove(page.Content);
		InvalidatePages();

		if (_pages.Count == 0)
		{
			_selectedIndex = -1;
		}

		else if (_selectedIndex > index)
		{
			_selectedIndex--;
		}

		else if (_selectedIndex == index)
		{
			_selectedIndex = -1;
			Select(Math.Min(index, _pages.Count - 1));
		}

		else if (_selectedIndex < 0)
		{
			Select(0);
		}

		TabClosed?.Invoke(this, page);

		return true;
	}

	public void Select(int index)
	{
		if (index < 0 || index >= _pages.Count)
		{
			return;
		}

		if (_selectedIndex == index)
		{
			return;
		}

		if (_selectedIndex >= 0 &&
			_selectedIndex < _pages.Count)
		{
			_pages[_selectedIndex].Content.IsVisible = false;
		}

		_selectedIndex = index;
		_pages[index].Content.IsVisible = true;
		InvalidateMeasure();
		InvalidateVisual();
		SelectionChanged?.Invoke(this, _pages[index]);
	}

	public void Select(string id)
	{
		int index = _pages.FindIndex(page => page.Id == id);

		if (index >= 0)
		{
			Select(index);
		}
	}

	/// <summary>
	/// Recomputes tab labels after a page title changed.
	/// </summary>
	public void RefreshPages()
	{
		InvalidatePages();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		_tabHeight = theme.Metrics.TabHeight;
		EnsureLayouts(font);

		float headerWidth = 0f;

		for (int index = 0; index < _pages.Count; index++)
		{
			float width = _layouts[index].Width + 28f +
				(_pages[index].CanClose ? 18f : 0f);

			headerWidth += MathF.Max(56f, width);
		}

		float contentWidth = float.IsFinite(contentAvailable.Width)
			? contentAvailable.Width
			: MathF.Max(headerWidth, 120f);

		float contentHeight = float.IsFinite(contentAvailable.Height)
			? MathF.Max(0f, contentAvailable.Height - _tabHeight)
			: 0f;

		UiSize host = MeasureChild(
			_contentHost,
			new UiSize(contentWidth, contentHeight));

		return new UiSize(
			contentWidth,
			_tabHeight + MathF.Max(host.Height, contentHeight));
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		float headerHeight = MathF.Min(
			_tabHeight,
			contentBounds.Height);

		UiRect hostBounds = new(
			contentBounds.X,
			contentBounds.Y + headerHeight,
			contentBounds.Width,
			MathF.Max(0f, contentBounds.Height - headerHeight));

		ArrangeChildExact(_contentHost, hostBounds);

		// Tab hit rectangles must exist before the first input event,
		// not only after the first paint.
		UpdateTabRects(GlobalVisualBounds, headerHeight);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		UiRect bounds = GlobalVisualBounds;
		float headerHeight = MathF.Min(_tabHeight, bounds.Height);

		EnsureLayouts(font);
		UpdateTabRects(bounds, headerHeight);

		drawList.AddRect(
			new UiRect(bounds.X, bounds.Y, bounds.Width, headerHeight),
			theme.Palette.Header);

		for (int index = 0; index < _pages.Count; index++)
		{
			UiRect tab = _tabRects[index];
			bool selected = index == _selectedIndex;

			if (selected)
			{
				drawList.AddRect(tab, theme.Palette.SurfaceRaised);
				drawList.AddRect(
					new UiRect(
						tab.X,
						tab.Y,
						tab.Width,
						2f),
					theme.Palette.Accent);
			}

			UiColor color = selected
				? theme.Palette.Text
				: theme.Palette.TextMuted;

			drawList.AddText(
				_layouts[index],
				new UiPoint(
					tab.X + 12f,
					tab.Y + ((headerHeight -
						_layouts[index].Height) * 0.5f)),
				color);

			if (_pages[index].CanClose)
			{
				UiRect close = _closeRects[index];
				UiPoint center = close.Center;

				drawList.AddLine(
					new UiPoint(center.X - 4f, center.Y - 4f),
					new UiPoint(center.X + 4f, center.Y + 4f),
					1.3f,
					color);

				drawList.AddLine(
					new UiPoint(center.X + 4f, center.Y - 4f),
					new UiPoint(center.X - 4f, center.Y + 4f),
					1.3f,
					color);
			}
		}

		drawList.AddRect(
			new UiRect(bounds.X, bounds.Y + headerHeight - 1f, bounds.Width, 1f),
			theme.Palette.Border);
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		for (int index = 0; index < _tabRects.Count; index++)
		{
			UiRect close = _closeRects[index];

			if (_pages[index].CanClose &&
				close.Contains(pointer.GlobalPosition))
			{
				RemovePage(_pages[index]);
				return true;
			}

			if (_tabRects[index].Contains(pointer.GlobalPosition))
			{
				Focus();
				Select(index);
				return true;
			}
		}

		return false;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		bool control =
			key.HasModifier(UiKeyModifiers.Control) ||
			key.HasModifier(UiKeyModifiers.Super);

		if (control && key.Key == UiKey.Tab)
		{
			if (_pages.Count > 1)
			{
				int direction = key.HasModifier(UiKeyModifiers.Shift)
					? -1
					: 1;

				int next = (_selectedIndex + direction + _pages.Count) %
					_pages.Count;

				Select(next);
			}

			return true;
		}

		return false;
	}

	private void InvalidatePages()
	{
		_layouts.Clear();
		_tabRects.Clear();
		_closeRects.Clear();
		InvalidateMeasure();
		InvalidateVisual();
	}

	private void EnsureLayouts(UiFont font)
	{
		if (_layoutFont == font &&
			_layouts.Count == _pages.Count)
		{
			return;
		}

		_layouts.Clear();
		_tabRects.Clear();
		_closeRects.Clear();

		for (int index = 0; index < _pages.Count; index++)
		{
			_layouts.Add(
				UiTextLayout.Create(
					_pages[index].Title,
					font,
					400f,
					UiTextWrap.NoWrap,
					UiTextAlignment.Start));
		}

		_layoutFont = font;
	}

	private void UpdateTabRects(UiRect bounds, float headerHeight)
	{
		_tabRects.Clear();
		_closeRects.Clear();
		float x = bounds.X;

		for (int index = 0; index < _pages.Count; index++)
		{
			float width = MathF.Max(
				56f,
				_layouts[index].Width + 28f +
					(_pages[index].CanClose ? 18f : 0f));

			UiRect tab = new(x, bounds.Y, width, headerHeight);
			_tabRects.Add(tab);
			_closeRects.Add(
				new UiRect(
					tab.Right - 22f,
					tab.Y + ((headerHeight - 16f) * 0.5f),
					16f,
					16f));

			x += width;
		}
	}
}
