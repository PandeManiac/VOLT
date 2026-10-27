using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// The top-level application menu bar.
/// </summary>
public sealed class MenuBar : Widget
{
	private readonly List<MenuItem> _menus = [];
	private readonly List<UiTextLayoutResult> _layouts = [];
	private readonly List<UiRect> _itemBounds = [];

	private UiFont? _layoutFont;
	private int _hoveredIndex = -1;

	public MenuBar()
	{
		ClipToBounds = true;
	}

	public IReadOnlyList<MenuItem> Menus => _menus;

	public MenuItem AddMenu(string title)
	{
		MenuItem menu = new(title);
		_menus.Add(menu);
		_layouts.Clear();
		_itemBounds.Clear();
		InvalidateMeasure();

		return menu;
	}

	public MenuItem AddMenu(MenuItem menu)
	{
		ArgumentNullException.ThrowIfNull(menu);
		_menus.Add(menu);
		_layouts.Clear();
		_itemBounds.Clear();
		InvalidateMeasure();

		return menu;
	}

	public void ClearMenus()
	{
		_menus.Clear();
		_layouts.Clear();
		_itemBounds.Clear();
		InvalidateMeasure();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		float height = theme.Metrics.MenuBarHeight;
		float x = 0f;

		_itemBounds.Clear();
		EnsureLayouts(font);

		for (int index = 0; index < _menus.Count; index++)
		{
			float width = _layouts[index].Width + 20f;
			_itemBounds.Add(new UiRect(x, 0f, width, height));
			x += width;
		}

		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: x,
			height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalVisualBounds;

		drawList.AddRect(bounds, theme.Palette.Header);
		drawList.AddRect(
			new UiRect(bounds.X, bounds.Bottom - 1f, bounds.Width, 1f),
			theme.Palette.Border);

		UiFont font = theme.Fonts.Body;

		if (_layoutFont != font)
		{
			EnsureLayouts(font);
		}

		float y = bounds.Y +
			MathF.Max(0f, (bounds.Height - theme.Fonts.Body.LineHeight) *
				0.5f);

		for (int index = 0; index < _menus.Count; index++)
		{
			UiRect item = new(
				bounds.X + _itemBounds[index].X,
				bounds.Y,
				_itemBounds[index].Width,
				bounds.Height);

			if (index == _hoveredIndex)
			{
				drawList.AddRoundedRect(
					item,
					theme.Metrics.SmallCornerRadius,
					theme.Palette.Hover);
			}

			drawList.AddText(
				_layouts[index],
				new UiPoint(
					item.X + 10f,
					y + MathF.Max(
						0f,
						(bounds.Height - _layouts[index].Height) * 0.5f)),
				theme.Palette.Text);
		}
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		int index = HitIndex(pointer.GlobalPosition);

		if (index != _hoveredIndex)
		{
			_hoveredIndex = index;
			InvalidateVisual();
		}

		return index >= 0;
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		int index = HitIndex(pointer.GlobalPosition);

		if (index < 0 || index >= _menus.Count)
		{
			return false;
		}

		MenuItem menu = _menus[index];
		UiRect item = _itemBounds[index];

		Context?.ShowPopup(
			new MenuPopup(menu.Children),
			new UiRect(
				GlobalVisualBounds.X + item.X,
				GlobalVisualBounds.Bottom,
				0f,
				0f));

		return true;
	}

	protected override void OnPointerLeave()
	{
		_hoveredIndex = -1;
		InvalidateVisual();
	}

	private int HitIndex(UiPoint globalPosition)
	{
		UiRect bounds = GlobalVisualBounds;

		if (!bounds.Contains(globalPosition))
		{
			return -1;
		}

		float x = globalPosition.X - bounds.X;

		for (int index = 0; index < _itemBounds.Count; index++)
		{
			if (x >= _itemBounds[index].X &&
				x < _itemBounds[index].Right)
			{
				return index;
			}
		}

		return -1;
	}

	private void EnsureLayouts(UiFont font)
	{
		if (_layoutFont == font &&
			_layouts.Count == _menus.Count)
		{
			return;
		}

		_layouts.Clear();
		_itemBounds.Clear();

		for (int index = 0; index < _menus.Count; index++)
		{
			_layouts.Add(
				UiTextLayout.Create(
					_menus[index].Label,
					font,
					400f,
					UiTextWrap.NoWrap,
					UiTextAlignment.Start));
		}

		_layoutFont = font;
	}
}
