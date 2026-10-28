using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A popup list of menu entries.
/// </summary>
/// <remarks>
/// The popup closes before invoking an item, so the action may open a
/// modal, change focus, or rebuild the editor layout without leaving a
/// disposed widget in the input path. Nested submenus are not rendered
/// yet.
/// </remarks>
public sealed class MenuPopup : Widget
{
	private readonly List<MenuItem> _items;
	private readonly List<UiTextLayoutResult> _labelLayouts = [];
	private readonly List<UiTextLayoutResult> _shortcutLayouts = [];

	private UiFont? _layoutFont;
	private int _hoveredIndex = -1;
	private int _pressedIndex = -1;
	private float _rowHeight;

	public MenuPopup(IReadOnlyList<MenuItem> items)
	{
		ArgumentNullException.ThrowIfNull(items);

		_items = [.. items];
		IsFocusable = true;
		ClipToBounds = true;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		UiFont shortcutFont = theme.Fonts.Small;
		float rowHeight = MathF.Max(22f, font.LineHeight + 8f);
		float labelWidth = 0f;
		float shortcutWidth = 0f;
		int separators = 0;

		EnsureLayouts(font, shortcutFont);

		for (int index = 0; index < _items.Count; index++)
		{
			MenuItem item = _items[index];

			if (item.IsSeparator)
			{
				separators++;
				continue;
			}

			labelWidth = MathF.Max(
				labelWidth,
				_labelLayouts[index].Width);

			shortcutWidth = MathF.Max(
				shortcutWidth,
				_shortcutLayouts[index].Width);
		}

		float width = MathF.Max(
			150f,
			labelWidth + shortcutWidth + 56f);

		float height =
			((_items.Count - separators) * rowHeight) +
			(separators * 7f) +
			8f;

		_rowHeight = rowHeight;

		return new UiSize(width, height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetPopupStyle();
		UiRect bounds = GlobalVisualBounds;

		drawList.AddRoundedRect(
			bounds,
			style.CornerRadius,
			style.Background);

		drawList.AddBorder(
			bounds,
			style.Border,
			style.BorderWidth,
			style.CornerRadius);

		if (_layoutFont != theme.Fonts.Body)
		{
			_labelLayouts.Clear();
			_shortcutLayouts.Clear();
			_layoutFont = null;
		}

		UiFont font = theme.Fonts.Body;
		UiFont shortcutFont = theme.Fonts.Small;
		EnsureLayouts(font, shortcutFont);

		float y = bounds.Y + 4f;
		float rowHeight = _rowHeight > 0f
			? _rowHeight
			: MathF.Max(22f, font.LineHeight + 8f);

		for (int index = 0; index < _items.Count; index++)
		{
			MenuItem item = _items[index];

			if (item.IsSeparator)
			{
				drawList.AddRect(
					new UiRect(
						bounds.X + 8f,
						y + 3f,
						MathF.Max(0f, bounds.Width - 16f),
						1f),
					theme.Palette.Border);

				y += 7f;
				continue;
			}

			UiRect row = new(
				bounds.X + 2f,
				y,
				MathF.Max(0f, bounds.Width - 4f),
				rowHeight);

			if (index == _hoveredIndex && item.IsEnabled)
			{
				drawList.AddRoundedRect(
					row,
					theme.Metrics.SmallCornerRadius,
					theme.Palette.Hover);
			}

			UiColor foreground = !item.IsEnabled
				? theme.Palette.TextDisabled
				: index == _hoveredIndex
					? theme.Palette.Text
					: theme.Palette.Text;

			if (item.IsChecked)
			{
				float checkX = bounds.X + 12f;
				float checkY = row.Center.Y;

				drawList.AddLine(
					new UiPoint(checkX, checkY),
					new UiPoint(checkX + 3f, checkY + 3f),
					1.5f,
					theme.Palette.Accent);

				drawList.AddLine(
					new UiPoint(checkX + 3f, checkY + 3f),
					new UiPoint(checkX + 9f, checkY - 4f),
					1.5f,
					theme.Palette.Accent);
			}

			drawList.AddText(
				_labelLayouts[index],
				new UiPoint(
					bounds.X + 24f,
					row.Y + ((rowHeight -
						_labelLayouts[index].Height) * 0.5f)),
				foreground);

			if (_shortcutLayouts[index].Glyphs.Length > 0)
			{
				drawList.AddText(
					_shortcutLayouts[index],
					new UiPoint(
						bounds.Right - 10f -
							_shortcutLayouts[index].Width,
						row.Y + ((rowHeight -
							_shortcutLayouts[index].Height) * 0.5f)),
					theme.Palette.TextMuted);
			}

			y += rowHeight;
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

		return true;
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		int index = HitIndex(pointer.GlobalPosition);
		_pressedIndex = index;

		return index >= 0;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		int index = HitIndex(pointer.GlobalPosition);

		if (index < 0 ||
			index != _pressedIndex ||
			index >= _items.Count)
		{
			_pressedIndex = -1;
			return true;
		}

		MenuItem item = _items[index];
		_pressedIndex = -1;

		if (!item.IsEnabled || item.Action is not Action action)
		{
			return true;
		}

		Context?.ClosePopup();
		action();

		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		switch (key.Key)
		{
			case UiKey.Down:
				MoveHover(1);
				return true;

			case UiKey.Up:
				MoveHover(-1);
				return true;

			case UiKey.Enter:
			case UiKey.Space:
				if (_hoveredIndex >= 0 &&
					_hoveredIndex < _items.Count)
				{
					MenuItem item = _items[_hoveredIndex];

					if (item.IsEnabled &&
						item.Action is Action action)
					{
						Context?.ClosePopup();
						action();
					}
				}

				return true;
		}

		return false;
	}

	private void MoveHover(int direction)
	{
		if (_items.Count == 0)
		{
			return;
		}

		int index = _hoveredIndex;

		for (int step = 0; step < _items.Count; step++)
		{
			index = (index + direction + _items.Count) % _items.Count;

			if (!_items[index].IsSeparator &&
				_items[index].IsEnabled)
			{
				_hoveredIndex = index;
				InvalidateVisual();
				return;
			}
		}
	}

	private int HitIndex(UiPoint globalPosition)
	{
		UiRect bounds = GlobalVisualBounds;

		if (!bounds.Contains(globalPosition))
		{
			return -1;
		}

		float rowHeight = _rowHeight > 0f
			? _rowHeight
			: 26f;

		float y = bounds.Y + 4f;

		for (int index = 0; index < _items.Count; index++)
		{
			MenuItem item = _items[index];

			if (item.IsSeparator)
			{
				y += 7f;
				continue;
			}

			if (globalPosition.Y >= y &&
				globalPosition.Y < y + rowHeight)
			{
				return item.IsEnabled ? index : -1;
			}

			y += rowHeight;
		}

		return -1;
	}

	private void EnsureLayouts(UiFont font, UiFont shortcutFont)
	{
		if (_layoutFont == font &&
			_labelLayouts.Count == _items.Count)
		{
			return;
		}

		_labelLayouts.Clear();
		_shortcutLayouts.Clear();

		float width = float.IsFinite(ContentBounds.Width)
			? MathF.Max(80f, ContentBounds.Width - 48f)
			: 200f;

		for (int index = 0; index < _items.Count; index++)
		{
			MenuItem item = _items[index];

			_labelLayouts.Add(
				UiTextLayout.Create(
					item.IsSeparator ? string.Empty : item.Label,
					font,
					width,
					UiTextWrap.NoWrap,
					UiTextAlignment.Start));

			_shortcutLayouts.Add(
				UiTextLayout.Create(
					item.Shortcut,
					shortcutFont,
					width,
					UiTextWrap.NoWrap,
					UiTextAlignment.End));
		}

		_layoutFont = font;
	}
}
