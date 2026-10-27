using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A single-choice drop-down list.
/// </summary>
public sealed class Dropdown : Widget
{
	private readonly List<string> _items = [];
	private int _selectedIndex = -1;
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;

	public Dropdown()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		Padding = new UiThickness(7f, 3f);
		MinHeight = 20f;
	}

	public event Action<Dropdown>? SelectionChanged;

	public IReadOnlyList<string> Items => _items;

	public int SelectedIndex
	{
		get => _selectedIndex;
		set
		{
			int next = _items.Count == 0
				? -1
				: Math.Clamp(value, -1, _items.Count - 1);

			if (next == _selectedIndex)
			{
				return;
			}

			_selectedIndex = next;
			_layout = null;
			InvalidateVisual();
			SelectionChanged?.Invoke(this);
		}
	}

	public string? SelectedItem =>
		_selectedIndex >= 0 && _selectedIndex < _items.Count
			? _items[_selectedIndex]
			: null;

	public void SetItems(IEnumerable<string> items, int selectedIndex = -1)
	{
		ArgumentNullException.ThrowIfNull(items);
		_items.Clear();
		_items.AddRange(items);
		_selectedIndex = _items.Count == 0
			? -1
			: Math.Clamp(selectedIndex, -1, _items.Count - 1);
		_layout = null;
		InvalidateMeasure();
		InvalidateVisual();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float height = MathF.Max(
			0f,
			theme.Metrics.FieldHeight - Padding.Vertical);

		return new UiSize(120f, height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetTextFieldStyle(
			this.GetVisualState(),
			HasFocus);

		drawList.AddRoundedRect(
			GlobalVisualBounds,
			style.CornerRadius,
			style.Background);

		drawList.AddBorder(
			GlobalVisualBounds,
			style.Border,
			style.BorderWidth,
			style.CornerRadius);

		UiRect content = GlobalContentBounds;
		UiFont font = theme.Fonts.Body;
		string text = SelectedItem ?? string.Empty;
		UiTextLayoutResult layout = EnsureLayout(
			font,
			text,
			MathF.Max(0.001f, content.Width - 14f));

		float y = content.Y +
			MathF.Max(0f, (content.Height - layout.Height) * 0.5f);

		drawList.AddText(
			layout,
			new UiPoint(content.X, y),
			SelectedItem is null
				? theme.Palette.TextDisabled
				: style.Foreground);

		float arrowX = content.Right - 6f;
		float arrowY = content.Center.Y;

		drawList.AddLine(
			new UiPoint(arrowX - 3f, arrowY - 1f),
			new UiPoint(arrowX, arrowY + 2f),
			1.3f,
			theme.Palette.TextMuted);

		drawList.AddLine(
			new UiPoint(arrowX, arrowY + 2f),
			new UiPoint(arrowX + 3f, arrowY - 1f),
			1.3f,
			theme.Palette.TextMuted);
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();
		OpenPopup();
		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		switch (key.Key)
		{
			case UiKey.Space:
			case UiKey.Enter:
				OpenPopup();
				return true;

			case UiKey.Down:
				SelectedIndex = Math.Min(
					_items.Count - 1,
					_selectedIndex + 1);
				return true;

			case UiKey.Up:
				SelectedIndex = Math.Max(0, _selectedIndex - 1);
				return true;
		}

		return false;
	}

	private void OpenPopup()
	{
		if (_items.Count == 0 || Context is not UiContext context)
		{
			return;
		}

		context.ShowPopup(
			new DropdownPopup(this, _items),
			new UiRect(
				GlobalVisualBounds.X,
				GlobalVisualBounds.Bottom + 2f,
				GlobalVisualBounds.Width,
				0f));
	}

	private void ApplySelection(int index)
	{
		SelectedIndex = index;
	}

	private UiTextLayoutResult EnsureLayout(
		UiFont font,
		string text,
		float width)
	{
		if (_layout is not null &&
			_layoutText == text &&
			_layoutFont == font &&
			_layoutWidth == width)
		{
			return _layout;
		}

		_layout = UiTextLayout.Create(
			text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		_layoutText = text;
		_layoutFont = font;
		_layoutWidth = width;

		return _layout;
	}

	private sealed class DropdownPopup : Widget
	{
		private readonly Dropdown _owner;
		private readonly List<string> _items;
		private int _hovered = -1;
		private int _pressed = -1;
		private float _rowHeight;

		public DropdownPopup(Dropdown owner, IReadOnlyList<string> items)
		{
			_owner = owner;
			_items = [.. items];
			IsFocusable = true;
			ClipToBounds = true;
		}

		protected override UiSize MeasureContent(UiSize contentAvailable)
		{
			UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
			UiFont font = theme.Fonts.Body;
			_rowHeight = MathF.Max(22f, font.LineHeight + 8f);
			float width = _owner.Width.Kind == Volt.Ui.Layout.UiLengthKind.Points
				? MathF.Max(120f, _owner.Width.Value)
				: 120f;

			return new UiSize(
				width,
				(_items.Count * _rowHeight) + 8f);
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

			UiFont font = theme.Fonts.Body;
			float y = bounds.Y + 4f;

			for (int index = 0; index < _items.Count; index++)
			{
				UiRect row = new(
					bounds.X + 2f,
					y,
					MathF.Max(0f, bounds.Width - 4f),
					_rowHeight);

				if (index == _hovered)
				{
					drawList.AddRoundedRect(
						row,
						theme.Metrics.SmallCornerRadius,
						theme.Palette.Hover);
				}

				UiTextLayoutResult layout = UiTextLayout.Create(
					_items[index],
					font,
					MathF.Max(0.001f, row.Width - 12f),
					UiTextWrap.NoWrap,
					UiTextAlignment.Start);

				drawList.AddText(
					layout,
					new UiPoint(
						row.X + 8f,
						row.Y + ((_rowHeight - layout.Height) * 0.5f)),
					index == _owner.SelectedIndex
						? theme.Palette.Accent
						: theme.Palette.Text);

				y += _rowHeight;
			}
		}

		protected internal override bool OnPointerMove(UiPointerEvent pointer)
		{
			int index = HitIndex(pointer.GlobalPosition);

			if (index != _hovered)
			{
				_hovered = index;
				InvalidateVisual();
			}

			return true;
		}

		protected internal override bool OnPointerDown(UiPointerEvent pointer)
		{
			_pressed = HitIndex(pointer.GlobalPosition);
			return _pressed >= 0;
		}

		protected internal override bool OnPointerUp(UiPointerEvent pointer)
		{
			int index = HitIndex(pointer.GlobalPosition);

			if (index < 0 || index != _pressed)
			{
				_pressed = -1;
				return true;
			}

			_owner.ApplySelection(index);
			Context?.ClosePopup();

			return true;
		}

		protected internal override bool OnKeyDown(UiKeyEvent key)
		{
			if (_items.Count == 0)
			{
				return false;
			}

			switch (key.Key)
			{
				case UiKey.Down:
					_hovered = Math.Min(
						_items.Count - 1,
						_hovered + 1);
					InvalidateVisual();
					return true;

				case UiKey.Up:
					_hovered = Math.Max(0, _hovered - 1);
					InvalidateVisual();
					return true;

				case UiKey.Enter:
				case UiKey.Space:
					if (_hovered >= 0)
					{
						_owner.ApplySelection(_hovered);
						Context?.ClosePopup();
					}

					return true;
			}

			return false;
		}

		private int HitIndex(UiPoint globalPosition)
		{
			UiRect bounds = GlobalVisualBounds;

			if (!bounds.Contains(globalPosition))
			{
				return -1;
			}

			int index = (int)MathF.Floor(
				(globalPosition.Y - bounds.Y - 4f) / _rowHeight);

			return index >= 0 && index < _items.Count
				? index
				: -1;
		}
	}
}
