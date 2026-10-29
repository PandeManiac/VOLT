using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A virtualized hierarchical list with selection, expansion, search
/// support, keyboard navigation, and an item context-menu event.
/// </summary>
/// <remarks>
/// Only rows intersecting the viewport are painted. Expansion state
/// is owned by the items; the tree owns selection and scrolling. The
/// view does not mutate the hierarchy beyond expanding or collapsing
/// nodes, so an editor can use it over its authored scene model.
/// </remarks>
public sealed class TreeView : Widget
{
	private readonly List<TreeViewItem> _roots = [];
	private readonly List<TreeViewItem> _visible = [];
	private readonly Dictionary<TreeViewItem, int> _visibleIndices = [];
	private readonly List<TreeViewItem> _selection = [];

	/// <summary>
	/// How far the pointer must travel before a press on a row becomes
	/// a drag.
	/// </summary>
	private const float DragThresholdPixels = 4f;

	private bool _flattenDirty = true;
	private float _scrollOffset;
	private float _maximumScroll;
	private float _rowHeight;
	private int _hoveredRow = -1;
	private TreeViewItem? _selectionAnchor;
	private TreeViewItem? _dragSource;
	private UiPoint _dragStart;
	private UiKeyModifiers _dragModifiers;
	private bool _dragging;
	private TreeViewItem? _dropTarget;
	private bool _dropIntoRoot;

	public TreeView()
	{
		IsFocusable = true;
		ClipToBounds = true;
		FocusOnPointerPress = true;
	}

	public event Action<TreeView, IReadOnlyList<TreeViewItem>>? SelectionChanged;

	public event Action<TreeView, TreeViewItem, UiPoint>? ContextMenuRequested;

	public event Action<TreeView, TreeViewItem>? ItemActivated;

	/// <summary>
	/// Raised when a row is dropped after a drag.
	/// </summary>
	/// <remarks>
	/// The target is the row under the pointer, or null when the drop
	/// landed on the empty area below the rows, which means the root.
	/// The dragged row's own subtree is never reported as a target: the
	/// highlight and the drop are both suppressed for it.
	/// </remarks>
	public event Action<TreeView, TreeViewItem, TreeViewItem?>? ItemDropped;

	public IReadOnlyList<TreeViewItem> Roots => _roots;

	public IReadOnlyList<TreeViewItem> SelectedItems => _selection;

	public TreeViewItem? SelectedItem =>
		_selection.Count > 0 ? _selection[^1] : null;

	public float ScrollOffset => _scrollOffset;

	public float MaximumScroll => _maximumScroll;

	public TreeViewItem AddRoot(TreeViewItem item)
	{
		ArgumentNullException.ThrowIfNull(item);
		_roots.Add(item);
		item.Parent = null;
		_flattenDirty = true;
		InvalidateMeasure();

		return item;
	}

	public bool RemoveRoot(TreeViewItem item)
	{
		bool removed = _roots.Remove(item);
		item.Parent = null;
		_flattenDirty = true;
		InvalidateMeasure();

		return removed;
	}

	public void Clear()
	{
		_roots.Clear();
		_selection.Clear();
		_selectionAnchor = null;
		_flattenDirty = true;
		InvalidateMeasure();
	}

	public void Refresh()
	{
		_flattenDirty = true;
		InvalidateMeasure();
		InvalidateVisual();
	}

	public void ExpandAll()
	{
		foreach (TreeViewItem item in _visible)
		{
			item.IsExpanded = item.HasChildren;
		}

		Refresh();
	}

	public void CollapseAll()
	{
		foreach (TreeViewItem item in _visible)
		{
			item.IsExpanded = false;
		}

		Refresh();
	}

	public void SelectSingle(TreeViewItem? item)
	{
		ClearSelection();

		if (item is not null)
		{
			item.IsSelected = true;
			_selection.Add(item);
			_selectionAnchor = item;
		}

		SelectionChanged?.Invoke(this, _selection);
		InvalidateVisual();
	}

	public void SetSelection(IEnumerable<TreeViewItem> items)
	{
		ArgumentNullException.ThrowIfNull(items);
		ClearSelection();

		foreach (TreeViewItem item in items)
		{
			if (!item.IsSelected)
			{
				item.IsSelected = true;
				_selection.Add(item);
			}
		}

		_selectionAnchor = _selection.Count > 0 ? _selection[^1] : null;
		SelectionChanged?.Invoke(this, _selection);
		InvalidateVisual();
	}

	public void Reveal(TreeViewItem item)
	{
		ArgumentNullException.ThrowIfNull(item);

		for (TreeViewItem? current = item.Parent;
			current is not null;
			current = current.Parent)
		{
			current.IsExpanded = true;
		}

		_flattenDirty = true;
		InvalidateMeasure();
		Flatten();

		int index = _visibleIndices.TryGetValue(item, out int itemIndex)
			? itemIndex
			: -1;

		if (index >= 0)
		{
			float height = MathF.Max(1f, ContentBounds.Height);
			float target = (index * _rowHeight) -
				((height - _rowHeight) * 0.5f);

			SetScroll(target);
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		_rowHeight = theme.Metrics.RowHeight;
		Flatten();

		float height = float.IsFinite(contentAvailable.Height)
			? contentAvailable.Height
			: _visible.Count * _rowHeight;

		float width = float.IsFinite(contentAvailable.Width)
			? contentAvailable.Width
			: 220f;

		return new UiSize(width, height);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		Flatten();
		UpdateMaximumScroll();
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiFont font = theme.Fonts.Body;
		UiRect content = GlobalContentBounds;

		if (content.IsEmpty)
		{
			return;
		}

		Flatten();

		int first = Math.Max(
			0,
			(int)MathF.Floor(_scrollOffset / _rowHeight));

		int last = Math.Min(
			_visible.Count - 1,
			(int)MathF.Ceiling(
				(_scrollOffset + content.Height) / _rowHeight));

		drawList.PushClip(content);

		try
		{
			for (int index = first; index <= last; index++)
			{
				TreeViewItem item = _visible[index];
				float y = content.Y +
					(index * _rowHeight) -
					_scrollOffset;

				UiRect row = new(
					content.X,
					y,
					content.Width,
					_rowHeight);

				if (item.IsSelected)
				{
					drawList.AddRect(row, theme.Palette.Selection);
				}

				else if (index == _hoveredRow)
				{
					drawList.AddRect(row, theme.Palette.Hover);
				}

				if (_dragging &&
					item == _dropTarget &&
					IsValidDropTarget(item))
				{
					drawList.AddRect(
						row,
						theme.Palette.Selection.WithAlpha(0.55f));

					drawList.AddBorder(
						row,
						theme.Palette.Accent,
						1.5f);
				}

				float x = content.X + 4f + (item.Depth * 16f);
				UiColor foreground = item.IsSelected
					? theme.Palette.Accent
					: theme.Palette.Text;

				if (item.HasChildren)
				{
					DrawChevron(
						drawList,
						x + 3f,
						y + (_rowHeight * 0.5f),
						item.IsExpanded,
						theme.Palette.TextMuted);
				}

				x += 14f;

				if (item.Icon != UiIcon.None)
				{
					float glyph = MathF.Min(14f, _rowHeight - 6f);

					UiIconPainter.Draw(
						drawList,
						item.Icon,
						new UiRect(
							x,
							y + ((_rowHeight - glyph) * 0.5f),
							glyph,
							glyph),
						item.IconColor ??
							(item.IsSelected
								? theme.Palette.Accent
								: theme.Palette.TextMuted),
						1.4f);

					x += glyph + 6f;
				}

				else if (item.IconColor is UiColor icon)
				{
					drawList.AddRoundedRect(
						new UiRect(
							x,
							y + ((_rowHeight - 8f) * 0.5f),
							8f,
							8f),
						2f,
						icon);

					x += 12f;
				}

				UiTextLayoutResult layout = item.GetLayout(
					font,
					MathF.Max(10f, content.Right - x - 4f));

				drawList.AddText(
					layout,
					new UiPoint(
						x,
						y + ((_rowHeight - layout.Height) * 0.5f)),
					item.IsSelected
						? foreground
						: theme.Palette.Text);
			}

			if (_dragging && _dropIntoRoot)
			{
				float rowsBottom = content.Y +
					(_visible.Count * _rowHeight) -
					_scrollOffset;

				float y = MathF.Min(rowsBottom, content.Bottom) - 2f;

				drawList.AddRect(
					new UiRect(content.X, y, content.Width, 2f),
					theme.Palette.Accent);
			}
		}

		finally
		{
			drawList.PopClip();
		}

		DrawScrollBar(drawList, content, theme);
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		if (pointer.Button == UiMouseButton.Right)
		{
			TreeViewItem? target = HitItem(pointer.GlobalPosition);

			if (target is not null)
			{
				if (!target.IsSelected)
				{
					SelectSingle(target);
				}

				ContextMenuRequested?.Invoke(
					this,
					target,
					pointer.GlobalPosition);
			}

			return target is not null;
		}

		if (pointer.Button != UiMouseButton.Left)
		{
			return false;
		}

		TreeViewItem? item = HitItem(pointer.GlobalPosition);

		if (item is null)
		{
			return false;
		}

		Focus();

		float x = GlobalContentBounds.X + 4f +
			(item.Depth * 16f);

		bool expander = item.HasChildren &&
			pointer.GlobalPosition.X >= x &&
			pointer.GlobalPosition.X <= x + 14f;

		if (expander)
		{
			item.IsExpanded = !item.IsExpanded;
			Refresh();
			return true;
		}

		_dragSource = item;
		_dragStart = pointer.GlobalPosition;
		_dragModifiers = pointer.Modifiers;
		_dragging = false;
		_dropTarget = null;
		_dropIntoRoot = false;
		CapturePointer(pointer.Button);

		bool control =
			(pointer.Modifiers & UiKeyModifiers.Control) != 0 ||
			(pointer.Modifiers & UiKeyModifiers.Super) != 0;

		bool shift =
			(pointer.Modifiers & UiKeyModifiers.Shift) != 0;

		if (control)
		{
			if (item.IsSelected)
			{
				item.IsSelected = false;
				_selection.Remove(item);
			}

			else
			{
				item.IsSelected = true;
				_selection.Add(item);
			}

			_selectionAnchor = item;
			SelectionChanged?.Invoke(this, _selection);
			InvalidateVisual();
		}

		else if (shift && _selectionAnchor is not null)
		{
			SelectRange(_selectionAnchor, item);
		}

		else if (!item.IsSelected || _selection.Count == 1)
		{
			SelectSingle(item);
		}

		else
		{
			// Keep a multi-selection intact so a drag moves all of it;
			// a release without a drag collapses it to this row.
			_selectionAnchor = item;
		}

		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (pointer.Button != UiMouseButton.Left || _dragSource is null)
		{
			return false;
		}

		TreeViewItem source = _dragSource;
		TreeViewItem? target = _dropTarget;
		UiKeyModifiers modifiers = _dragModifiers;
		bool dragging = _dragging;
		bool validTarget = dragging && IsValidDropTarget(target);
		bool intoRoot = dragging && _dropIntoRoot;

		_dragSource = null;
		_dragging = false;
		_dragModifiers = UiKeyModifiers.None;
		_dropTarget = null;
		_dropIntoRoot = false;
		InvalidateVisual();

		if (dragging)
		{
			if (validTarget || intoRoot)
			{
				ItemDropped?.Invoke(
					this,
					source,
					validTarget ? target : null);
			}

			return true;
		}

		bool plain = (modifiers &
			(UiKeyModifiers.Control |
				UiKeyModifiers.Super |
				UiKeyModifiers.Shift)) == 0;

		if (plain)
		{
			SelectSingle(source);
		}

		return true;
	}

	protected internal override void OnClick(int clickCount)
	{
		if (clickCount >= 2 && SelectedItem is TreeViewItem item)
		{
			ItemActivated?.Invoke(this, item);
		}
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (_dragSource is not null)
		{
			if (!_dragging)
			{
				float dx = pointer.GlobalPosition.X - _dragStart.X;
				float dy = pointer.GlobalPosition.Y - _dragStart.Y;

				if ((dx * dx) + (dy * dy) <
					DragThresholdPixels * DragThresholdPixels)
				{
					return true;
				}

				_dragging = true;
			}

			UpdateDropTarget(pointer.GlobalPosition);
			return true;
		}

		TreeViewItem? item = HitItem(pointer.GlobalPosition);
		int row = item is null
			? -1
			: _visibleIndices.GetValueOrDefault(item, -1);

		if (row != _hoveredRow)
		{
			_hoveredRow = row;
			InvalidateVisual();
		}

		return item is not null;
	}

	protected internal override bool OnWheel(UiWheelEvent wheel)
	{
		if (_maximumScroll <= 0f)
		{
			return false;
		}

		SetScroll(_scrollOffset - (wheel.Delta.Y * _rowHeight * 3f));
		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		if (_visible.Count == 0)
		{
			return false;
		}

		int index = SelectedItem is null
			? -1
			: _visibleIndices.GetValueOrDefault(SelectedItem, -1);

		switch (key.Key)
		{
			case UiKey.Down:
				SelectIndex(Math.Min(_visible.Count - 1, index + 1), key);
				return true;

			case UiKey.Up:
				SelectIndex(Math.Max(0, index - 1), key);
				return true;

			case UiKey.Right:
				{
					TreeViewItem? item = SelectedItem;

					if (item is null)
					{
						return false;
					}

					if (item.HasChildren && !item.IsExpanded)
					{
						item.IsExpanded = true;
						Refresh();
					}

					return true;
				}

			case UiKey.Left:
				{
					TreeViewItem? item = SelectedItem;

					if (item is null)
					{
						return false;
					}

					if (item.HasChildren && item.IsExpanded)
					{
						item.IsExpanded = false;
						Refresh();
					}

					else if (item.Parent is not null)
					{
						SelectSingle(item.Parent);
						Reveal(item.Parent);
					}

					return true;
				}

			case UiKey.Home:
				SelectIndex(0, key);
				return true;

			case UiKey.End:
				SelectIndex(_visible.Count - 1, key);
				return true;

			case UiKey.Enter:
				if (SelectedItem is TreeViewItem selected)
				{
					ItemActivated?.Invoke(this, selected);
				}

				return true;
		}

		return false;
	}

	private void SelectIndex(int index, UiKeyEvent key)
	{
		if (index < 0 || index >= _visible.Count)
		{
			return;
		}

		if (key.HasModifier(UiKeyModifiers.Shift) &&
			_selectionAnchor is not null)
		{
			SelectRange(_selectionAnchor, _visible[index]);
		}

		else
		{
			SelectSingle(_visible[index]);
		}

		Reveal(_visible[index]);
	}

	private void SelectRange(TreeViewItem from, TreeViewItem to)
	{
		int start = _visibleIndices.GetValueOrDefault(from, -1);
		int end = _visibleIndices.GetValueOrDefault(to, -1);

		if (start < 0 || end < 0)
		{
			SelectSingle(to);
			return;
		}

		if (start > end)
		{
			(start, end) = (end, start);
		}

		ClearSelection();

		for (int index = start; index <= end; index++)
		{
			TreeViewItem item = _visible[index];
			item.IsSelected = true;
			_selection.Add(item);
		}

		InvalidateVisual();
		SelectionChanged?.Invoke(this, _selection);
	}

	private void ClearSelection()
	{
		for (int index = 0; index < _selection.Count; index++)
		{
			_selection[index].IsSelected = false;
		}

		_selection.Clear();
	}

	private void UpdateDropTarget(UiPoint position)
	{
		_dropTarget = null;
		_dropIntoRoot = false;

		UiRect content = GlobalContentBounds;

		if (content.Contains(position))
		{
			TreeViewItem? target = HitItem(position);

			if (target is not null)
			{
				_dropTarget = target;
			}

			else
			{
				// Empty space below the rows drops into the root, which
				// is what an author expects from the end of the list.
				float rowsBottom = content.Y +
					(_visible.Count * _rowHeight) -
					_scrollOffset;

				_dropIntoRoot = position.Y >= rowsBottom;
			}
		}

		InvalidateVisual();
	}

	private bool IsValidDropTarget(TreeViewItem? target)
	{
		return target is not null &&
			target != _dragSource &&
			(_dragSource is null || !IsInSubtree(target, _dragSource));
	}

	private static bool IsInSubtree(
		TreeViewItem item,
		TreeViewItem ancestor)
	{
		for (TreeViewItem? current = item.Parent;
			current is not null;
			current = current.Parent)
		{
			if (current == ancestor)
			{
				return true;
			}
		}

		return false;
	}

	private TreeViewItem? HitItem(UiPoint globalPosition)
	{
		UiRect content = GlobalContentBounds;

		if (!content.Contains(globalPosition) || _rowHeight <= 0f)
		{
			return null;
		}

		int index = (int)MathF.Floor(
			(globalPosition.Y - content.Y + _scrollOffset) / _rowHeight);

		return index >= 0 && index < _visible.Count
			? _visible[index]
			: null;
	}

	private void Flatten()
	{
		if (!_flattenDirty)
		{
			return;
		}

		_flattenDirty = false;
		_visible.Clear();
		_visibleIndices.Clear();

		for (int index = 0; index < _roots.Count; index++)
		{
			FlattenNode(_roots[index], 0);
		}

		UpdateMaximumScroll();
	}

	private void FlattenNode(TreeViewItem item, int depth)
	{
		item.Depth = depth;
		_visibleIndices[item] = _visible.Count;
		_visible.Add(item);

		if (!item.IsExpanded)
		{
			return;
		}

		for (int index = 0; index < item.Children.Count; index++)
		{
			FlattenNode(item.Children[index], depth + 1);
		}
	}

	private void UpdateMaximumScroll()
	{
		float contentHeight = _visible.Count * _rowHeight;
		_maximumScroll = MathF.Max(
			0f,
			contentHeight - ContentBounds.Height);

		_scrollOffset = Math.Clamp(
			_scrollOffset,
			0f,
			_maximumScroll);
	}

	private void SetScroll(float offset)
	{
		offset = Math.Clamp(offset, 0f, _maximumScroll);

		if (offset == _scrollOffset)
		{
			return;
		}

		_scrollOffset = offset;
		InvalidateVisual();
	}

	private void DrawScrollBar(
		UiDrawList drawList,
		UiRect content,
		UiTheme theme)
	{
		if (_maximumScroll <= 0f)
		{
			return;
		}

		float trackWidth = 8f;
		UiRect track = new(
			content.Right - trackWidth,
			content.Y,
			trackWidth,
			content.Height);

		float visibleRatio = Math.Clamp(
			content.Height / (content.Height + _maximumScroll),
			0.08f,
			1f);

		float thumbHeight = track.Height * visibleRatio;
		float travel = track.Height - thumbHeight;
		float position = _maximumScroll <= 0f
			? 0f
			: (_scrollOffset / _maximumScroll) * travel;

		drawList.AddRoundedRect(
			new UiRect(
				track.X + 2f,
				track.Y + position,
				MathF.Max(2f, track.Width - 4f),
				MathF.Max(8f, thumbHeight)),
			3f,
			theme.Palette.BorderStrong);
	}

	private static void DrawChevron(
		UiDrawList drawList,
		float x,
		float y,
		bool expanded,
		UiColor color)
	{
		if (expanded)
		{
			drawList.AddLine(
				new UiPoint(x - 3f, y - 2f),
				new UiPoint(x, y + 2f),
				1.3f,
				color);

			drawList.AddLine(
				new UiPoint(x, y + 2f),
				new UiPoint(x + 3f, y - 2f),
				1.3f,
				color);
		}

		else
		{
			drawList.AddLine(
				new UiPoint(x - 2f, y - 3f),
				new UiPoint(x + 2f, y),
				1.3f,
				color);

			drawList.AddLine(
				new UiPoint(x + 2f, y),
				new UiPoint(x - 2f, y + 3f),
				1.3f,
				color);
		}
	}
}
