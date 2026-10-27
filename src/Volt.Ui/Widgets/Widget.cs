using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;

namespace Volt.Ui.Widgets;

/// <summary>
/// The base of every retained UI element.
/// </summary>
/// <remarks>
/// <para>
/// A widget owns its children. Removing a child detaches it but does
/// not dispose it, so it can be reparented. Disposing a widget
/// disposes its whole subtree. Widget lifetime is independent of any
/// ECS world; UI is not an entity system.
/// </para>
/// <para>
/// Layout is a measure/arrange pass. Measuring is cached against the
/// available size; arranging is cached against the final bounds and
/// inherited clip. Changing content, layout parameters, visibility,
/// or fonts invalidates the affected cache and its ancestors. Paint
/// is a tree walk that appends commands to a
/// <see cref="UiDrawList"/>; it does not allocate per widget.
/// </para>
/// <para>
/// Pointer events bubble from the hit widget to its ancestors and
/// stop when a handler returns true. Key events tunnel from the root
/// to the focused widget through <c>OnKeyPreview</c>, then bubble
/// through <c>OnKeyDown</c>/<c>OnKeyUp</c>. A handler may remove
/// widgets, change focus, or open overlays; the dispatcher verifies
/// attachment before every subsequent step.
/// </para>
/// </remarks>
public abstract class Widget : IDisposable
{
	private readonly List<Widget> _children = [];

	private UiContext? _context;
	private Widget? _parent;
	private string? _name;
	private bool _disposed;
	private bool _isVisible = true;
	private bool _isEnabled = true;
	private bool _isFocusable;
	private bool _isHitTestVisible = true;
	private bool _clipToBounds;
	private bool _focusOnPointerPress = true;
	private bool _isHovered;
	private bool _isPressed;

	private UiLength _width = UiLength.Auto;
	private UiLength _height = UiLength.Auto;
	private float _minWidth;
	private float _minHeight;
	private float _maxWidth = float.PositiveInfinity;
	private float _maxHeight = float.PositiveInfinity;
	private UiThickness _margin;
	private UiThickness _padding;
	private UiAlignment _horizontalAlignment = UiAlignment.Stretch;
	private UiAlignment _verticalAlignment = UiAlignment.Stretch;
	private float _flexGrow;
	private float _flexShrink;

	private bool _measureValid;
	private bool _arrangeValid;
	private UiSize _measuredAvailable;
	private UiSize _desiredSize;
	private UiRect _arrangeBounds;
	private UiPoint _arrangeOrigin;
	private UiRect _arrangeClip;

	private UiRect _bounds;
	private UiRect _visualBounds;
	private UiRect _contentBounds;
	private UiRect _globalBounds;
	private UiRect _clipBounds;

	/// <summary>
	/// Gets or sets a diagnostic name. It has no effect on identity.
	/// </summary>
	public string Name
	{
		get => _name ?? GetType().Name;
		set
		{
			_name = value;
		}
	}

	/// <summary>
	/// Gets the context-local identifier assigned when the widget was
	/// attached. It is stable for the attached lifetime.
	/// </summary>
	public int Id { get; private set; }

	public UiContext? Context => _context;

	public Widget? Parent => _parent;

	public IReadOnlyList<Widget> Children => _children;

	public bool IsAttached => _context is not null;

	public bool IsDisposed => _disposed;

	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			if (_isVisible == value)
			{
				return;
			}

			_isVisible = value;
			InvalidateMeasure();
		}
	}

	public bool IsEnabled
	{
		get => _isEnabled;
		set
		{
			if (_isEnabled == value)
			{
				return;
			}

			_isEnabled = value;
			InvalidateVisual();
		}
	}

	public bool IsFocusable
	{
		get => _isFocusable;
		set
		{
			if (_isFocusable == value)
			{
				return;
			}

			_isFocusable = value;

			if (!value && HasFocus)
			{
				_context?.ClearFocus();
			}
		}
	}

	public bool IsHitTestVisible
	{
		get => _isHitTestVisible;
		set => _isHitTestVisible = value;
	}

	/// <summary>
	/// Gets or sets whether descendants are clipped to this widget's
	/// visual bounds.
	/// </summary>
	public bool ClipToBounds
	{
		get => _clipToBounds;
		set
		{
			if (_clipToBounds == value)
			{
				return;
			}

			_clipToBounds = value;
			InvalidateArrange();
		}
	}

	public bool FocusOnPointerPress
	{
		get => _focusOnPointerPress;
		set => _focusOnPointerPress = value;
	}

	public bool IsHovered => _isHovered;

	public bool IsPressed => _isPressed;

	public bool HasFocus => _context?.FocusedWidget == this;

	public bool IsPointerCaptured => _context?.CapturedWidget == this;

	public UiCursor Cursor { get; set; } = UiCursor.Arrow;

	public string? TooltipText { get; set; }

	public UiLength Width
	{
		get => _width;
		set
		{
			_width = value;
			InvalidateMeasure();
		}
	}

	public UiLength Height
	{
		get => _height;
		set
		{
			_height = value;
			InvalidateMeasure();
		}
	}

	public float MinWidth
	{
		get => _minWidth;
		set
		{
			_minWidth = MathF.Max(0f, value);
			InvalidateMeasure();
		}
	}

	public float MinHeight
	{
		get => _minHeight;
		set
		{
			_minHeight = MathF.Max(0f, value);
			InvalidateMeasure();
		}
	}

	public float MaxWidth
	{
		get => _maxWidth;
		set
		{
			_maxWidth = MathF.Max(0f, value);
			InvalidateMeasure();
		}
	}

	public float MaxHeight
	{
		get => _maxHeight;
		set
		{
			_maxHeight = MathF.Max(0f, value);
			InvalidateMeasure();
		}
	}

	public UiThickness Margin
	{
		get => _margin;
		set
		{
			_margin = value;
			InvalidateMeasure();
		}
	}

	public UiThickness Padding
	{
		get => _padding;
		set
		{
			_padding = value;
			InvalidateMeasure();
		}
	}

	public UiAlignment HorizontalAlignment
	{
		get => _horizontalAlignment;
		set
		{
			_horizontalAlignment = value;
			InvalidateArrange();
		}
	}

	public UiAlignment VerticalAlignment
	{
		get => _verticalAlignment;
		set
		{
			_verticalAlignment = value;
			InvalidateArrange();
		}
	}

	/// <summary>
	/// Gets or sets the share of leftover space this widget receives
	/// inside a stacking container.
	/// </summary>
	public float FlexGrow
	{
		get => _flexGrow;
		set
		{
			if (!float.IsFinite(value) || value < 0f)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_flexGrow = value;
			_parent?.InvalidateMeasure();
		}
	}

	/// <summary>
	/// Gets or sets the weight used when a stacking container must
	/// shrink this widget to fit constrained space.
	/// </summary>
	public float FlexShrink
	{
		get => _flexShrink;
		set
		{
			if (!float.IsFinite(value) || value < 0f)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_flexShrink = value;
			_parent?.InvalidateMeasure();
		}
	}

	public UiSize DesiredSize => _desiredSize;

	/// <summary>
	/// Gets the outer bounds, including margin, relative to the
	/// parent's content area.
	/// </summary>
	public UiRect Bounds => _bounds;

	/// <summary>
	/// Gets the bounds after margin is removed, relative to the
	/// parent's content area.
	/// </summary>
	public UiRect VisualBounds => _visualBounds;

	/// <summary>
	/// Gets the content area after margin and padding, relative to the
	/// parent's content area.
	/// </summary>
	public UiRect ContentBounds => _contentBounds;

	/// <summary>
	/// Gets the global origin used when arranging children. Child
	/// bounds are relative to this widget's outer bounds, so the
	/// parent's global outer origin is the translation applied to them.
	/// </summary>
	private UiPoint ChildOrigin => new(
		_globalBounds.X,
		_globalBounds.Y);

	/// <summary>
	/// Gets the outer bounds in root UI coordinates.
	/// </summary>
	public UiRect GlobalBounds => _globalBounds;

	/// <summary>
	/// Gets the visual (margin-excluded) bounds in root coordinates.
	/// </summary>
	public UiRect GlobalVisualBounds => new(
		_globalBounds.X + _margin.Left,
		_globalBounds.Y + _margin.Top,
		MathF.Max(0f, _bounds.Width - _margin.Horizontal),
		MathF.Max(0f, _bounds.Height - _margin.Vertical));

	/// <summary>
	/// Gets the content area in root UI coordinates.
	/// </summary>
	public UiRect GlobalContentBounds => new(
		_globalBounds.X + _margin.Left + _padding.Left,
		_globalBounds.Y + _margin.Top + _padding.Top,
		MathF.Max(0f, _bounds.Width - _margin.Horizontal - _padding.Horizontal),
		MathF.Max(0f, _bounds.Height - _margin.Vertical - _padding.Vertical));

	/// <summary>
	/// Gets the effective clip in root UI coordinates.
	/// </summary>
	public UiRect ClipBounds => _clipBounds;

	/// <summary>
	/// Gets the global origin of children arranged in the content area.
	/// </summary>
	public UiPoint ContentOrigin => new(
		_contentBounds.X,
		_contentBounds.Y);

	public bool IsAncestorOf(Widget widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		for (Widget? current = widget._parent;
			current is not null;
			current = current._parent)
		{
			if (current == this)
			{
				return true;
			}
		}

		return false;
	}

	public T? FindAncestor<T>()
		where T : Widget
	{
		for (Widget? current = _parent;
			current is not null;
			current = current._parent)
		{
			if (current is T match)
			{
				return match;
			}
		}

		return null;
	}

	public Widget Add(Widget child)
	{
		Insert(_children.Count, child);
		return child;
	}

	/// <summary>
	/// Adds a child and preserves its concrete type for fluent setup.
	/// </summary>
	public T Add<T>(T child)
		where T : Widget
	{
		Insert(_children.Count, child);
		return child;
	}

	public void Insert(int index, Widget child)
	{
		ArgumentNullException.ThrowIfNull(child);
		ThrowIfUnusable();

		if (child == this)
		{
			throw new ArgumentException(
				"A widget cannot be its own child.",
				nameof(child));
		}

		if (child._disposed)
		{
			throw new ArgumentException(
				"A disposed widget cannot be attached.",
				nameof(child));
		}

		if (child._parent is not null)
		{
			throw new InvalidOperationException(
				"The widget already has a parent. Remove it before " +
				"reparenting.");
		}

		if (child.IsAncestorOf(this))
		{
			throw new InvalidOperationException(
				"Adding the widget would create a parent-child cycle.");
		}

		if (index < 0 || index > _children.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		child._parent = this;
		_children.Insert(index, child);

		if (_context is not null)
		{
			child.Attach(_context);
		}

		InvalidateMeasure();
	}

	/// <summary>
	/// Removes a child without disposing it.
	/// </summary>
	public bool Remove(Widget child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (child._parent != this)
		{
			return false;
		}

		int index = _children.IndexOf(child);

		if (index < 0)
		{
			return false;
		}

		_context?.NotifyWidgetDetaching(child);

		_children.RemoveAt(index);
		child._parent = null;
		child.DetachFromContext();

		InvalidateMeasure();

		return true;
	}

	public bool RemoveFromParent()
	{
		return _parent?.Remove(this) ?? false;
	}

	public void ClearChildren()
	{
		for (int index = _children.Count - 1; index >= 0; index--)
		{
			Remove(_children[index]);
		}
	}

	public void BringToFront()
	{
		if (_parent is null)
		{
			return;
		}

		int index = _parent._children.IndexOf(this);

		if (index >= 0 && index != _parent._children.Count - 1)
		{
			_parent._children.RemoveAt(index);
			_parent._children.Add(this);
			_parent.InvalidateArrange();
		}
	}

	public void SendToBack()
	{
		if (_parent is null)
		{
			return;
		}

		int index = _parent._children.IndexOf(this);

		if (index > 0)
		{
			_parent._children.RemoveAt(index);
			_parent._children.Insert(0, this);
			_parent.InvalidateArrange();
		}
	}

	public bool Focus()
	{
		if (_context is null || !_isFocusable || !_isVisible || !_isEnabled)
		{
			return false;
		}

		_context.Focus(this);
		return true;
	}

	public void ClearFocus()
	{
		if (HasFocus)
		{
			_context?.ClearFocus();
		}
	}

	public bool CapturePointer(UiMouseButton button = UiMouseButton.Left)
	{
		if (_context is null)
		{
			return false;
		}

		_context.CapturePointer(this, button);
		return true;
	}

	public void ReleasePointer()
	{
		_context?.ReleasePointer(this);
	}

	public void InvalidateMeasure()
	{
		if (_disposed)
		{
			return;
		}

		InvalidateMeasureCore();
	}

	public void InvalidateArrange()
	{
		if (_disposed)
		{
			return;
		}

		InvalidateArrangeCore();
	}

	/// <summary>
	/// Requests repaint. Paint state is resolved directly from widget
	/// properties, so no cache is invalidated here.
	/// </summary>
	public void InvalidateVisual()
	{
	}

	/// <summary>
	/// Invalidates layout for this widget and its whole subtree.
	/// </summary>
	public void InvalidateLayoutTree()
	{
		if (_disposed)
		{
			return;
		}

		for (int index = 0; index < _children.Count; index++)
		{
			_children[index].InvalidateLayoutTree();
		}

		InvalidateMeasureCore();
	}

	public virtual void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		RemoveFromParent();

		for (int index = _children.Count - 1; index >= 0; index--)
		{
			_children[index].Dispose();
		}

		_children.Clear();
		_context?.NotifyWidgetDisposed(this);
		_detachFromContextOnly();
		_disposed = true;
	}

	/// <summary>
	/// Measures the widget inside the available outer space.
	/// </summary>
	internal void Measure(UiSize available)
	{
		if (_disposed || !_isVisible)
		{
			_desiredSize = UiSize.Zero;
			_measureValid = true;
			_measuredAvailable = available;
			return;
		}

		if (_measureValid && _measuredAvailable == available)
		{
			return;
		}

		_measuredAvailable = available;
		_measureValid = true;

		float availableWidth = MathF.Max(0f, available.Width);
		float availableHeight = MathF.Max(0f, available.Height);

		float visualWidth = ResolveVisualWidth(availableWidth);
		float visualHeight = ResolveVisualHeight(availableHeight);

		float innerWidth = _width.IsAuto
			? MathF.Max(
				0f,
				availableWidth - _margin.Horizontal - _padding.Horizontal)
			: MathF.Max(
				0f,
				visualWidth - _padding.Horizontal);

		float innerHeight = _height.IsAuto
			? MathF.Max(
				0f,
				availableHeight - _margin.Vertical - _padding.Vertical)
			: MathF.Max(
				0f,
				visualHeight - _padding.Vertical);

		UiSize content = MeasureContent(
			new UiSize(innerWidth, innerHeight));

		if (float.IsNaN(content.Width) || content.Width < 0f)
		{
			content = content with { Width = 0f };
		}

		if (float.IsNaN(content.Height) || content.Height < 0f)
		{
			content = content with { Height = 0f };
		}

		if (_width.IsAuto)
		{
			visualWidth = content.Width + _padding.Horizontal;
		}

		if (_height.IsAuto)
		{
			visualHeight = content.Height + _padding.Vertical;
		}

		visualWidth = Clamp(visualWidth, _minWidth, _maxWidth);
		visualHeight = Clamp(visualHeight, _minHeight, _maxHeight);

		_desiredSize = new UiSize(
			visualWidth + _margin.Horizontal,
			visualHeight + _margin.Vertical);

		if (float.IsNaN(_desiredSize.Width) ||
			float.IsInfinity(_desiredSize.Width))
		{
			_desiredSize = _desiredSize with
			{
				Width = availableWidth
			};
		}

		if (float.IsNaN(_desiredSize.Height) ||
			float.IsInfinity(_desiredSize.Height))
		{
			_desiredSize = _desiredSize with
			{
				Height = availableHeight
			};
		}

	}

	/// <summary>
	/// Arranges the widget in a final outer rectangle.
	/// </summary>
	internal void Arrange(
		UiRect bounds,
		UiPoint parentContentOrigin,
		UiRect inheritedClip)
	{
		if (_disposed || !_isVisible)
		{
			_bounds = default;
			_visualBounds = default;
			_contentBounds = default;
			_globalBounds = default;
			_clipBounds = default;
			_arrangeValid = true;
			return;
		}

		if (_arrangeValid &&
			_arrangeBounds == bounds &&
			_arrangeOrigin == parentContentOrigin &&
			_arrangeClip == inheritedClip)
		{
			return;
		}

		_arrangeBounds = bounds;
		_arrangeOrigin = parentContentOrigin;
		_arrangeClip = inheritedClip;
		_arrangeValid = true;

		_bounds = bounds;
		_visualBounds = Deflate(bounds, _margin);
		_contentBounds = Deflate(_visualBounds, _padding);
		_globalBounds = new UiRect(
			parentContentOrigin.X + bounds.X,
			parentContentOrigin.Y + bounds.Y,
			bounds.Width,
			bounds.Height);

		UiRect globalVisual = new(
			parentContentOrigin.X + _visualBounds.X,
			parentContentOrigin.Y + _visualBounds.Y,
			_visualBounds.Width,
			_visualBounds.Height);

		_clipBounds = _clipToBounds
			? inheritedClip.Intersect(globalVisual)
			: inheritedClip;

		ArrangeContent(_contentBounds);
	}

	/// <summary>
	/// Paints this widget and its visible descendants.
	/// </summary>
	internal void Paint(UiDrawList drawList)
	{
		if (_disposed || !_isVisible || _clipBounds.IsEmpty)
		{
			return;
		}

		if (_clipToBounds)
		{
			drawList.PushClip(_clipBounds);
		}

		try
		{
			RenderSelf(drawList);

			for (int index = 0; index < _children.Count; index++)
			{
				Widget child = _children[index];

				if (!child.IsAttached || child.ClipBounds.IsEmpty)
				{
					continue;
				}

				child.Paint(drawList);
			}

			RenderOverlay(drawList);
		}

		finally
		{
			if (_clipToBounds)
			{
				drawList.PopClip();
			}
		}
	}

	/// <summary>
	/// Returns the topmost interactive widget at a root-space point.
	/// </summary>
	internal Widget? HitTest(UiPoint point)
	{
		if (_disposed ||
			!_isVisible ||
			_clipBounds.IsEmpty ||
			!_clipBounds.Contains(point))
		{
			return null;
		}

		if (!_isEnabled)
		{
			UiRect disabledVisual = new(
				_globalBounds.X + _margin.Left,
				_globalBounds.Y + _margin.Top,
				_visualBounds.Width,
				_visualBounds.Height);

			return _isHitTestVisible && disabledVisual.Contains(point)
				? this
				: null;
		}

		for (int index = _children.Count - 1; index >= 0; index--)
		{
			Widget? hit = _children[index].HitTest(point);

			if (hit is not null)
			{
				return hit;
			}
		}

		if (_isHitTestVisible)
		{
			UiRect globalVisual = new(
				_globalBounds.X + _margin.Left,
				_globalBounds.Y + _margin.Top,
				_visualBounds.Width,
				_visualBounds.Height);

			if (globalVisual.Contains(point))
			{
				return this;
			}
		}

		return null;
	}

	/// <summary>
	/// Measures content inside the available inner space.
	/// </summary>
	protected virtual UiSize MeasureContent(UiSize contentAvailable)
	{
		return UiSize.Zero;
	}

	/// <summary>
	/// Arranges children inside the content area.
	/// </summary>
	protected virtual void ArrangeContent(UiRect contentBounds)
	{
		for (int index = 0; index < _children.Count; index++)
		{
			ArrangeChild(_children[index], contentBounds);
		}
	}

	/// <summary>
	/// Paints the widget itself. Children are painted afterwards.
	/// </summary>
	protected virtual void RenderSelf(UiDrawList drawList)
	{
	}

	/// <summary>
	/// Paints above all children. Used by scrollbars and drag
	/// adornments that must not be covered by content.
	/// </summary>
	protected virtual void RenderOverlay(UiDrawList drawList)
	{
	}

	protected virtual void OnAttached()
	{
	}

	protected virtual void OnDetached()
	{
	}

	protected virtual void OnPointerEnter()
	{
	}

	protected virtual void OnPointerLeave()
	{
	}

	protected internal virtual bool OnPointerDown(UiPointerEvent pointer)
	{
		return false;
	}

	protected internal virtual bool OnPointerUp(UiPointerEvent pointer)
	{
		return false;
	}

	protected internal virtual bool OnPointerMove(UiPointerEvent pointer)
	{
		return false;
	}

	protected internal virtual bool OnWheel(UiWheelEvent wheel)
	{
		return false;
	}

	protected internal virtual bool OnKeyPreview(UiKeyEvent key)
	{
		return false;
	}

	protected internal virtual bool OnKeyDown(UiKeyEvent key)
	{
		return false;
	}

	protected internal virtual bool OnKeyUp(UiKeyEvent key)
	{
		return false;
	}

	protected internal virtual bool OnTextInput(UiTextEvent text)
	{
		return false;
	}

	protected internal virtual void OnClick(int clickCount)
	{
	}

	protected internal virtual void OnFocusChanged(bool focused)
	{
	}

	/// <summary>
	/// Measures a child and returns its desired outer size.
	/// </summary>
	protected UiSize MeasureChild(Widget child, UiSize available)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!child.IsVisible)
		{
			return UiSize.Zero;
		}

		child.Measure(available);

		return child.DesiredSize;
	}

	/// <summary>
	/// Arranges a child inside a slot, honoring its alignment.
	/// </summary>
	protected void ArrangeChild(Widget child, UiRect slot)
	{
		ArrangeChildCore(
			child,
			slot,
			forceWidth: false,
			forceHeight: false);
	}

	/// <summary>
	/// Arranges a child with one axis forced to the slot size and the
	/// other axis honoring its alignment.
	/// </summary>
	protected void ArrangeChildCore(
		Widget child,
		UiRect slot,
		bool forceWidth,
		bool forceHeight)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!child.IsVisible)
		{
			return;
		}

		UiSize desired = child.DesiredSize;

		bool stretchWidth = !forceWidth &&
			child.HorizontalAlignment == UiAlignment.Stretch &&
			child.Width.IsAuto;

		bool stretchHeight = !forceHeight &&
			child.VerticalAlignment == UiAlignment.Stretch &&
			child.Height.IsAuto;

		float width = forceWidth || stretchWidth
			? slot.Width
			: desired.Width;

		float height = forceHeight || stretchHeight
			? slot.Height
			: desired.Height;

		width = MathF.Max(0f, width);
		height = MathF.Max(0f, height);

		float x = forceWidth
			? slot.X
			: slot.X + AlignOffset(
				slot.Width,
				width,
				child.HorizontalAlignment);

		float y = forceHeight
			? slot.Y
			: slot.Y + AlignOffset(
				slot.Height,
				height,
				child.VerticalAlignment);

		child.Arrange(
			new UiRect(x, y, width, height),
			ChildOrigin,
			_clipBounds);
	}

	/// <summary>
	/// Arranges a child with explicit bounds and alignment disabled.
	/// </summary>
	protected void ArrangeChildExact(Widget child, UiRect bounds)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!child.IsVisible)
		{
			return;
		}

		child.Arrange(bounds, ChildOrigin, _clipBounds);
	}

	protected static float AlignOffset(
		float container,
		float size,
		UiAlignment alignment)
	{
		return alignment switch
		{
			UiAlignment.Center => MathF.Max(0f, (container - size) * 0.5f),
			UiAlignment.End => MathF.Max(0f, container - size),
			_ => 0f
		};
	}

	protected internal void SetHovered(bool hovered)
	{
		if (_isHovered == hovered)
		{
			return;
		}

		_isHovered = hovered;
		InvalidateVisual();

		if (hovered)
		{
			OnPointerEnter();
		}

		else
		{
			OnPointerLeave();
		}
	}

	protected internal void SetPressed(bool pressed)
	{
		if (_isPressed == pressed)
		{
			return;
		}

		_isPressed = pressed;
		InvalidateVisual();
	}

	protected internal void SetFocusState(bool focused)
	{
		OnFocusChanged(focused);
		InvalidateVisual();
	}

	internal void RaiseClick(int clickCount)
	{
		OnClick(clickCount);
	}

	internal void RaiseFocusChanged(bool focused)
	{
		SetFocusState(focused);
	}

	internal void RaisePointerEnter()
	{
		SetHovered(true);
	}

	internal void RaisePointerLeave()
	{
		SetHovered(false);
	}

	internal bool RaisePointerDown(UiPointerEvent pointer)
	{
		return OnPointerDown(pointer);
	}

	internal bool RaisePointerUp(UiPointerEvent pointer)
	{
		return OnPointerUp(pointer);
	}

	internal bool RaisePointerMove(UiPointerEvent pointer)
	{
		return OnPointerMove(pointer);
	}

	internal bool RaiseWheel(UiWheelEvent wheel)
	{
		return OnWheel(wheel);
	}

	internal bool RaiseKeyPreview(UiKeyEvent key)
	{
		return OnKeyPreview(key);
	}

	internal bool RaiseKeyDown(UiKeyEvent key)
	{
		return OnKeyDown(key);
	}

	internal bool RaiseKeyUp(UiKeyEvent key)
	{
		return OnKeyUp(key);
	}

	internal bool RaiseTextInput(UiTextEvent text)
	{
		return OnTextInput(text);
	}

	internal void RaiseAttached()
	{
		OnAttached();
	}

	internal void RaiseDetached()
	{
		OnDetached();
	}

	internal void Attach(UiContext context)
	{
		_context = context;
		Id = context.AllocateWidgetId();
		_measureValid = false;
		_arrangeValid = false;
		RaiseAttached();

		for (int index = 0; index < _children.Count; index++)
		{
			_children[index].Attach(context);
		}
	}

	private void DetachFromContext()
	{
		if (_context is null)
		{
			return;
		}

		RaiseDetached();

		for (int index = 0; index < _children.Count; index++)
		{
			_children[index].DetachFromContext();
		}

		_detachFromContextOnly();
	}

	private void _detachFromContextOnly()
	{
		_context = null;
		Id = 0;
		_measureValid = false;
		_arrangeValid = false;
	}

	private void InvalidateMeasureCore()
	{
		_measureValid = false;
		_arrangeValid = false;
		_parent?.InvalidateMeasureCoreFromChild();
	}

	private void InvalidateMeasureCoreFromChild()
	{
		if (!_measureValid && !_arrangeValid)
		{
			return;
		}

		_measureValid = false;
		_arrangeValid = false;
		_parent?.InvalidateMeasureCoreFromChild();
	}

	private void InvalidateArrangeCore()
	{
		_arrangeValid = false;
		_parent?.InvalidateArrangeCoreFromChild();
	}

	private void InvalidateArrangeCoreFromChild()
	{
		if (!_arrangeValid)
		{
			return;
		}

		_arrangeValid = false;
		_parent?.InvalidateArrangeCoreFromChild();
	}

	private float ResolveVisualWidth(float available)
	{
		return _width.Kind switch
		{
			UiLengthKind.Points => _width.Value,
			UiLengthKind.Percent when float.IsFinite(available) =>
				available * _width.Value,
			_ => 0f
		};
	}

	private float ResolveVisualHeight(float available)
	{
		return _height.Kind switch
		{
			UiLengthKind.Points => _height.Value,
			UiLengthKind.Percent when float.IsFinite(available) =>
				available * _height.Value,
			_ => 0f
		};
	}

	private static UiRect Deflate(UiRect rect, UiThickness thickness)
	{
		float width = MathF.Max(
			0f,
			rect.Width - thickness.Horizontal);

		float height = MathF.Max(
			0f,
			rect.Height - thickness.Vertical);

		return new UiRect(
			rect.X + thickness.Left,
			rect.Y + thickness.Top,
			width,
			height);
	}

	private static float Clamp(
		float value,
		float minimum,
		float maximum)
	{
		minimum = MathF.Max(0f, minimum);
		maximum = MathF.Max(minimum, maximum);

		return Math.Clamp(value, minimum, maximum);
	}

	private void ThrowIfUnusable()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
