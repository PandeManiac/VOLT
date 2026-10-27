using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Ui;

/// <summary>
/// Owns one retained UI tree, its theme, input state, focus, and
/// pointer capture.
/// </summary>
/// <remarks>
/// <para>
/// A context is independent of SDL, Vulkan, ECS, and the editor. A
/// platform bridge calls <see cref="BeginFrame"/>, forwards events
/// through the public input methods, and paints with
/// <see cref="Draw"/>. Headless tests call the same methods without a
/// window or graphics device.
/// </para>
/// <para>
/// The tree has a content layer and an overlay layer. Normal
/// application widgets are added to <see cref="Content"/>; popups,
/// tooltips, and modals live in <see cref="Overlay"/> and receive
/// input first. Removing a widget immediately clears focus, capture,
/// hover, and popup references that point into the removed subtree.
/// </para>
/// <para>
/// Pointer events bubble from the hit widget to the root. Key events
/// tunnel from the root to the focused widget and then bubble back.
/// Returning true from a handler stops propagation. Handlers may
/// remove widgets or change focus; the dispatcher validates each
/// widget before the next callback.
/// </para>
/// </remarks>
public sealed class UiContext : IDisposable
{
	private readonly Panel _root;
	private readonly Panel _content;
	private readonly CanvasPanel _overlay;
	private readonly UiDrawList _drawList = new();
	private readonly List<UiShortcut> _shortcuts = [];
	private readonly List<Widget> _hoverChain = [];
	private readonly List<Widget> _dispatchPath = [];

	private UiTheme _theme;
	private Widget? _focused;
	private Widget? _captured;
	private Widget? _hovered;
	private Widget? _pressed;
	private Widget? _activePopup;
	private Widget? _modal;
	private TooltipView? _tooltip;
	private Widget? _tooltipOwner;
	private string? _tooltipText;
	private double _hoverStarted;

	private UiMouseButton _capturedButton;
	private UiMouseButton _pressedButton;
	private UiPoint _pointer;
	private UiPoint _pointerDelta;
	private UiPoint _lastClickPosition;
	private UiKeyModifiers _modifiers;
	private UiSize _surface;
	private double _time;
	private double _lastClickTime = double.NegativeInfinity;
	private int _clickCount;
	private Widget? _lastClickWidget;
	private int _nextWidgetId = 1;
	private bool _disposed;

	public UiContext()
		: this(UiTheme.CreateDark())
	{
	}

	public UiContext(UiTheme theme)
	{
		ArgumentNullException.ThrowIfNull(theme);

		_theme = theme;
		Clipboard = new InMemoryClipboard();

		_content = new Panel
		{
			DrawBackground = false,
			ClipToBounds = true,
			IsHitTestVisible = false
		};

		_overlay = new CanvasPanel();

		_root = new Panel
		{
			DrawBackground = false,
			ClipToBounds = true,
			IsHitTestVisible = false
		};

		_root.Add(_content);
		_root.Add(_overlay);
		_root.Attach(this);
	}

	public UiTheme Theme => _theme;

	public Widget Root => _root;

	public Panel Content => _content;

	public CanvasPanel Overlay => _overlay;

	public UiDrawList DrawList => _drawList;

	public Widget? FocusedWidget => _focused;

	public Widget? CapturedWidget => _captured;

	public Widget? HoveredWidget => _hovered;

	public Widget? ActivePopup => _activePopup;

	public Widget? Modal => _modal;

	public UiPoint PointerPosition => _pointer;

	public UiPoint PointerDelta => _pointerDelta;

	public UiKeyModifiers Modifiers => _modifiers;

	public double TimeSeconds => _time;

	public UiSize Surface => _surface;

	public bool HasModal => _modal is not null;

	public IUiClipboard Clipboard { get; set; }

	/// <summary>
	/// Gets the image textures this context can draw.
	/// </summary>
	/// <remarks>
	/// A renderer reads this registry to upload images and to free the
	/// slots that were released. Disposing the context releases every
	/// image it holds.
	/// </remarks>
	public UiTextureRegistry Textures { get; } = new();

	public IList<UiShortcut> Shortcuts => _shortcuts;

	public UiCursor Cursor
	{
		get
		{
			Widget? target = _captured ?? _hovered;

			for (Widget? current = target;
				current is not null;
				current = current.Parent)
			{
				if (current.Cursor != UiCursor.Arrow)
				{
					return current.Cursor;
				}
			}

			return UiCursor.Arrow;
		}
	}

	/// <summary>
	/// Creates a widget identifier for attachment.
	/// </summary>
	internal int AllocateWidgetId()
	{
		int id = _nextWidgetId;

		if (id == int.MaxValue)
		{
			_nextWidgetId = 1;
		}

		else
		{
			_nextWidgetId++;
		}

		return id;
	}

	/// <summary>
	/// Adds a widget to the content layer.
	/// </summary>
	public Widget Add(Widget widget)
	{
		return _content.Add(widget);
	}

	/// <summary>
	/// Runs measure and arrange if the surface or tree changed.
	/// </summary>
	public void Layout(UiSize surface)
	{
		ThrowIfDisposed();

		surface = new UiSize(
			MathF.Max(0f, surface.Width),
			MathF.Max(0f, surface.Height));

		_surface = surface;

		_root.Measure(surface);
		_root.Arrange(
			new UiRect(0f, 0f, surface.Width, surface.Height),
			UiPoint.Zero,
			new UiRect(0f, 0f, surface.Width, surface.Height));
	}

	/// <summary>
	/// Starts an application frame: advances time, updates layout, and
	/// refreshes hover state.
	/// </summary>
	public void BeginFrame(
		double timeSeconds,
		UiSize surface,
		UiPoint pointerPosition,
		UiPoint pointerDelta = default)
	{
		ThrowIfDisposed();

		_time = timeSeconds;
		_pointerDelta = pointerDelta;
		_pointer = pointerPosition;

		Layout(surface);
		UpdateHover();
		UpdateTooltip();
	}

	/// <summary>
	/// Ends a frame. Transient hover leave events are not deferred;
	/// this exists as a symmetry point for hosts that need one.
	/// </summary>
	public void EndFrame()
	{
		_pointerDelta = UiPoint.Zero;
	}

	/// <summary>
	/// Replaces the active theme and rebuilds font-aware layout.
	/// </summary>
	public void SetTheme(UiTheme theme)
	{
		ArgumentNullException.ThrowIfNull(theme);
		ThrowIfDisposed();

		_theme = theme;
		_root.InvalidateLayoutTree();
	}

	/// <summary>
	/// Replaces the theme with the same palette at a new display scale.
	/// </summary>
	public void SetDpiScale(float dpiScale)
	{
		SetTheme(_theme.WithDpiScale(dpiScale));
	}

	/// <summary>
	/// Paints the tree and returns the completed draw list.
	/// </summary>
	public UiDrawList Draw()
	{
		ThrowIfDisposed();

		_drawList.Begin(_surface);
		_root.Paint(_drawList);

		return _drawList;
	}

	/// <summary>
	/// Moves the pointer and dispatches a move event.
	/// </summary>
	public bool PointerMove(
		UiPoint position,
		UiPoint delta = default,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		ThrowIfDisposed();

		_pointer = position;
		_pointerDelta = delta;
		_modifiers = modifiers;
		UpdateHover();

		Widget? target = _captured ?? HitTest(position);

		if (target is null)
		{
			return false;
		}

		UiPointerEvent pointer = new(
			position,
			position,
			delta,
			UiMouseButton.Left,
			modifiers,
			1);

		return BubblePointer(target, pointer, (widget, args) =>
			widget.RaisePointerMove(args));
	}

	/// <summary>
	/// Dispatches a mouse-button press.
	/// </summary>
	public bool PointerDown(
		UiMouseButton button,
		UiPoint position,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		ThrowIfDisposed();

		_pointer = position;
		_modifiers = modifiers;

		HideTooltip();

		if (_activePopup is not null &&
			!IsInside(_activePopup, position))
		{
			ClosePopup();
			return true;
		}

		UpdateHover();

		Widget? target = _captured ?? HitTest(position);

		if (target is null)
		{
			ClearFocus();
			return false;
		}

		if (!target.IsEnabled)
		{
			return true;
		}

		_pressed = target;
		_pressedButton = button;
		target.SetPressed(true);

		if (target.FocusOnPointerPress)
		{
			Widget? focusTarget = FindFocusTarget(target);

			if (focusTarget is not null)
			{
				Focus(focusTarget);
			}

			else if (_focused is not null &&
				!_focused.IsAncestorOf(target))
			{
				ClearFocus();
			}
		}

		UiPointerEvent pointer = new(
			position,
			position,
			UiPoint.Zero,
			button,
			modifiers,
			1);

		BubblePointer(target, pointer, (widget, args) =>
			widget.RaisePointerDown(args));

		return true;
	}

	/// <summary>
	/// Dispatches a mouse-button release and synthesizes a click when
	/// the release completes a press on the same control.
	/// </summary>
	public bool PointerUp(
		UiMouseButton button,
		UiPoint position,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		ThrowIfDisposed();

		_pointer = position;
		_modifiers = modifiers;
		UpdateHover();

		Widget? target = _captured ?? HitTest(position);

		if (target is not null)
		{
			UiPointerEvent pointer = new(
				position,
				position,
				UiPoint.Zero,
				button,
				modifiers,
				1);

			BubblePointer(target, pointer, (widget, args) =>
				widget.RaisePointerUp(args));
		}

		bool wasCaptured = _captured is not null &&
			_capturedButton == button;

		if (wasCaptured)
		{
			ReleasePointer(_captured!);
		}

		if (_pressed is not null)
		{
			Widget pressed = _pressed;
			_pressed = null;
			pressed.SetPressed(false);

			UiRect visual = new(
				pressed.GlobalBounds.X + pressed.Margin.Left,
				pressed.GlobalBounds.Y + pressed.Margin.Top,
				pressed.VisualBounds.Width,
				pressed.VisualBounds.Height);

			bool inside = visual.Contains(position);

			if (inside && pressed.IsAttached)
			{
				int clickCount = NextClickCount(
					pressed,
					position);
				pressed.RaiseClick(clickCount);
			}
		}

		return true;
	}

	/// <summary>
	/// Dispatches a scroll event. Positive Y means up.
	/// </summary>
	public bool Wheel(
		UiPoint delta,
		UiPoint position,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		ThrowIfDisposed();

		_pointer = position;
		_modifiers = modifiers;
		UpdateHover();

		Widget? target = _captured ?? HitTest(position);

		if (target is null)
		{
			return false;
		}

		UiWheelEvent wheel = new(position, delta, modifiers);

		return BubblePointer(target, wheel);
	}

	/// <summary>
	/// Dispatches a physical key press.
	/// </summary>
	public bool KeyDown(
		UiKey key,
		UiKeyModifiers modifiers = UiKeyModifiers.None,
		bool isRepeat = false)
	{
		ThrowIfDisposed();

		_modifiers = modifiers;
		UiKeyEvent keyEvent = new(key, true, isRepeat, modifiers);

		if (TryDispatchKey(keyEvent))
		{
			return true;
		}

		if (!isRepeat)
		{
			for (int index = 0; index < _shortcuts.Count; index++)
			{
				UiShortcut shortcut = _shortcuts[index];

				if (shortcut.Matches(keyEvent))
				{
					shortcut.Action();
					return true;
				}
			}
		}

		if (key == UiKey.Tab && !isRepeat)
		{
			MoveFocus(
				(modifiers & UiKeyModifiers.Shift) != 0
					? -1
					: 1);

			return true;
		}

		return false;
	}

	/// <summary>
	/// Dispatches a physical key release.
	/// </summary>
	public bool KeyUp(
		UiKey key,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		ThrowIfDisposed();

		_modifiers = modifiers;
		UiKeyEvent keyEvent = new(key, false, false, modifiers);

		return TryDispatchKey(keyEvent);
	}

	/// <summary>
	/// Delivers committed Unicode text to the focused widget.
	/// </summary>
	public bool TextInput(string text)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(text);

		if (text.Length == 0 || _focused is null || !_focused.IsAttached)
		{
			return false;
		}

		UiTextEvent textEvent = new(text);
		Widget widget = _focused;

		return widget.RaiseTextInput(textEvent);
	}

	/// <summary>
	/// Shows an IME composition preview. The current platform bridge
	/// does not deliver composition yet; widgets can override
	/// <see cref="Widget"/> text handling when a bridge supplies it.
	/// </summary>
	public void SetComposition(string? composition)
	{
		Composition = composition;
	}

	/// <summary>
	/// Gets the active IME composition preview, if any.
	/// </summary>
	public string? Composition { get; private set; }

	/// <summary>
	/// Handles a window focus loss by dropping capture and hover.
	/// </summary>
	public void WindowFocusLost()
	{
		if (_captured is not null)
		{
			ReleasePointer(_captured);
		}

		ClearHover();
		_pressed?.SetPressed(false);
		_pressed = null;
	}

	public void Focus(Widget widget)
	{
		ArgumentNullException.ThrowIfNull(widget);
		ThrowIfDisposed();

		if (widget == _focused)
		{
			return;
		}

		if (!widget.IsAttached ||
			!widget.IsVisible ||
			!widget.IsEnabled ||
			!widget.IsFocusable)
		{
			return;
		}

		Widget? previous = _focused;
		_focused = widget;

		previous?.RaiseFocusChanged(false);
		widget.RaiseFocusChanged(true);
	}

	/// <summary>
	/// Returns the topmost interactive widget at a root-space point,
	/// honoring modal and popup layers. Intended for drag/drop hit
	/// testing outside the normal pointer routing path.
	/// </summary>
	public Widget? FindWidgetAt(UiPoint point)
	{
		return HitTest(point);
	}

	public void ClearFocus()
	{
		if (_focused is null)
		{
			return;
		}

		Widget previous = _focused;
		_focused = null;
		previous.RaiseFocusChanged(false);
		SetComposition(null);
	}

	/// <summary>
	/// Moves focus through the visible, enabled, focusable tree.
	/// </summary>
	public void MoveFocus(int direction)
	{
		if (direction == 0)
		{
			return;
		}

		List<Widget> order = [];
		CollectFocusable(_root, order);

		if (order.Count == 0)
		{
			ClearFocus();
			return;
		}

		int current = _focused is null
			? -1
			: order.IndexOf(_focused);

		int next = current;

		for (int step = 0; step < order.Count; step++)
		{
			next = (next + (direction > 0 ? 1 : -1) +
				order.Count) % order.Count;

			if (order[next] != _focused)
			{
				break;
			}
		}

		Focus(order[next]);
	}

	public void CapturePointer(Widget widget, UiMouseButton button)
	{
		ArgumentNullException.ThrowIfNull(widget);
		ThrowIfDisposed();

		if (!widget.IsAttached)
		{
			return;
		}

		_captured = widget;
		_capturedButton = button;
	}

	public void ReleasePointer(Widget widget)
	{
		ArgumentNullException.ThrowIfNull(widget);

		if (_captured == widget)
		{
			_captured = null;
		}
	}

	/// <summary>
	/// Shows a widget in the overlay and routes outward clicks to it.
	/// </summary>
	public void ShowPopup(Widget popup, UiRect preferredBounds)
	{
		ArgumentNullException.ThrowIfNull(popup);
		ThrowIfDisposed();

		ClosePopup();

		_overlay.Add(popup);
		popup.Measure(new UiSize(_surface.Width, _surface.Height));

		float width = popup.Width.IsAuto
			? popup.DesiredSize.Width
			: preferredBounds.Width;

		float height = popup.Height.IsAuto
			? popup.DesiredSize.Height
			: preferredBounds.Height;

		width = MathF.Min(width, _surface.Width);
		height = MathF.Min(height, _surface.Height);

		float x = Math.Clamp(
			preferredBounds.X,
			0f,
			MathF.Max(0f, _surface.Width - width));

		float y = Math.Clamp(
			preferredBounds.Y,
			0f,
			MathF.Max(0f, _surface.Height - height));

		_overlay.SetSlot(popup, new UiRect(x, y, width, height));
		_activePopup = popup;

		if (popup.IsFocusable)
		{
			Focus(popup);
		}
	}

	public void ClosePopup()
	{
		if (_activePopup is null)
		{
			return;
		}

		Widget popup = _activePopup;
		_activePopup = null;

		if (_focused is not null &&
			(popup == _focused || popup.IsAncestorOf(_focused)))
		{
			ClearFocus();
		}

		popup.RemoveFromParent();
		popup.Dispose();
	}

	public void ShowModal(Widget modal)
	{
		ArgumentNullException.ThrowIfNull(modal);
		ThrowIfDisposed();

		CloseModal();

		_overlay.Add(modal);
		_overlay.SetSlot(
			modal,
			new UiRect(0f, 0f, _surface.Width, _surface.Height));

		_modal = modal;

		if (modal.IsFocusable)
		{
			Focus(modal);
		}
	}

	public void CloseModal()
	{
		if (_modal is null)
		{
			return;
		}

		Widget modal = _modal;
		_modal = null;

		if (_focused is not null &&
			(modal == _focused || modal.IsAncestorOf(_focused)))
		{
			ClearFocus();
		}

		modal.RemoveFromParent();
		modal.Dispose();
	}

	/// <summary>
	/// Called by a widget before it is removed from the tree.
	/// </summary>
	internal void NotifyWidgetDetaching(Widget subtree)
	{
		if (_focused is not null &&
			(subtree == _focused || subtree.IsAncestorOf(_focused)))
		{
			ClearFocus();
		}

		if (_captured is not null &&
			(subtree == _captured || subtree.IsAncestorOf(_captured)))
		{
			_captured = null;
		}

		if (_hovered is not null &&
			(subtree == _hovered || subtree.IsAncestorOf(_hovered)))
		{
			_hovered = null;
		}

		if (_pressed is not null &&
			(subtree == _pressed || subtree.IsAncestorOf(_pressed)))
		{
			_pressed = null;
		}

		if (_activePopup is not null &&
			(subtree == _activePopup ||
				subtree.IsAncestorOf(_activePopup)))
		{
			_activePopup = null;
		}

		if (_modal is not null &&
			(subtree == _modal || subtree.IsAncestorOf(_modal)))
		{
			_modal = null;
		}

		_hoverChain.RemoveAll(widget =>
			widget == subtree || subtree.IsAncestorOf(widget));
	}

	internal void NotifyWidgetDisposed(Widget widget)
	{
		NotifyWidgetDetaching(widget);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_focused = null;
		_captured = null;
		_hovered = null;
		_pressed = null;
		_activePopup = null;
		_modal = null;
		_tooltipOwner = null;
		_tooltip = null;
		_shortcuts.Clear();
		Textures.Clear();
		_root.Dispose();
	}

	private Widget? HitTest(UiPoint point)
	{
		if (_modal is not null)
		{
			return _modal.IsAttached
				? _modal.HitTest(point)
				: null;
		}

		if (_activePopup is not null &&
			_activePopup.IsAttached)
		{
			Widget? popupHit = _activePopup.HitTest(point);

			if (popupHit is not null)
			{
				return popupHit;
			}
		}

		return _root.HitTest(point);
	}

	private void UpdateHover()
	{
		Widget? target = HitTest(_pointer);
		_targetHoverChain.Clear();

		for (Widget? current = target;
			current is not null;
			current = current.Parent)
		{
			_targetHoverChain.Add(current);
		}

		for (int index = _hoverChain.Count - 1; index >= 0; index--)
		{
			Widget widget = _hoverChain[index];

			if (!_targetHoverChain.Contains(widget))
			{
				widget.RaisePointerLeave();
			}
		}

		for (int index = _targetHoverChain.Count - 1; index >= 0; index--)
		{
			Widget widget = _targetHoverChain[index];

			if (!_hoverChain.Contains(widget))
			{
				widget.RaisePointerEnter();
			}
		}

		_hoverChain.Clear();
		_hoverChain.AddRange(_targetHoverChain);
		_hovered = target;

		string? tooltipText = target?.TooltipText;

		if (target != _tooltipOwner || tooltipText != _tooltipText)
		{
			HideTooltip();
			_tooltipOwner = target;
			_tooltipText = tooltipText;
			_hoverStarted = _time;
		}
	}

	private void UpdateTooltip()
	{
		if (_tooltip is not null ||
			_modal is not null ||
			_activePopup is not null ||
			_tooltipOwner is null ||
			!_tooltipOwner.IsAttached ||
			string.IsNullOrEmpty(_tooltipText) ||
			_time - _hoverStarted < 0.55)
		{
			return;
		}

		TooltipView tooltip = new()
		{
			Text = _tooltipText
		};

		_overlay.Add(tooltip);
		tooltip.Measure(new UiSize(_surface.Width, _surface.Height));

		float width = tooltip.DesiredSize.Width;
		float height = tooltip.DesiredSize.Height;
		float x = Math.Clamp(
			_pointer.X + 14f,
			0f,
			MathF.Max(0f, _surface.Width - width));
		float y = Math.Clamp(
			_pointer.Y + 20f,
			0f,
			MathF.Max(0f, _surface.Height - height));

		_overlay.SetSlot(tooltip, new UiRect(x, y, width, height));
		_tooltip = tooltip;
	}

	private void HideTooltip()
	{
		if (_tooltip is null)
		{
			return;
		}

		TooltipView tooltip = _tooltip;
		_tooltip = null;
		tooltip.RemoveFromParent();
		tooltip.Dispose();
	}

	private readonly List<Widget> _targetHoverChain = [];

	private void ClearHover()
	{
		for (int index = 0; index < _hoverChain.Count; index++)
		{
			_hoverChain[index].RaisePointerLeave();
		}

		_hoverChain.Clear();
		_hovered = null;
	}

	private Widget? FindFocusTarget(Widget target)
	{
		for (Widget? current = target;
			current is not null;
			current = current.Parent)
		{
			if (current.IsFocusable &&
				current.FocusOnPointerPress &&
				current.IsEnabled &&
				current.IsVisible)
			{
				return current;
			}
		}

		return null;
	}

	private int NextClickCount(Widget widget, UiPoint position)
	{
		double elapsed = _time - _lastClickTime;
		float distance = MathF.Sqrt(
			MathF.Pow(position.X - _lastClickPosition.X, 2f) +
			MathF.Pow(position.Y - _lastClickPosition.Y, 2f));

		bool same = _lastClickWidget == widget &&
			elapsed <= 0.35 &&
			distance <= 5f;

		_clickCount = same ? Math.Min(3, _clickCount + 1) : 1;
		_lastClickTime = _time;
		_lastClickPosition = position;
		_lastClickWidget = widget;

		return _clickCount;
	}

	private bool TryDispatchKey(in UiKeyEvent keyEvent)
	{
		if (keyEvent.IsDown && keyEvent.Key == UiKey.Escape)
		{
			if (_activePopup is not null)
			{
				ClosePopup();
				return true;
			}

			if (_modal is not null)
			{
				CloseModal();
				return true;
			}
		}

		if (_focused is null || !_focused.IsAttached)
		{
			return false;
		}

		_dispatchPath.Clear();

		for (Widget? current = _focused;
			current is not null;
			current = current.Parent)
		{
			_dispatchPath.Add(current);
		}

		if (keyEvent.IsDown)
		{
			for (int index = _dispatchPath.Count - 1;
				index >= 0;
				index--)
			{
				Widget widget = _dispatchPath[index];

				if (!widget.IsAttached)
				{
					continue;
				}

				if (widget.RaiseKeyPreview(keyEvent))
				{
					return true;
				}
			}
		}

		for (int index = 0; index < _dispatchPath.Count; index++)
		{
			Widget widget = _dispatchPath[index];

			if (!widget.IsAttached)
			{
				continue;
			}

			bool handled = keyEvent.IsDown
				? widget.RaiseKeyDown(keyEvent)
				: widget.RaiseKeyUp(keyEvent);

			if (handled)
			{
				return true;
			}
		}

		return false;
	}

	private bool BubblePointer(
		Widget target,
		in UiPointerEvent pointer,
		Func<Widget, UiPointerEvent, bool> handler)
	{
		if (!target.IsAttached)
		{
			return false;
		}

		_dispatchPath.Clear();

		for (Widget? current = target;
			current is not null;
			current = current.Parent)
		{
			_dispatchPath.Add(current);
		}

		for (int index = 0; index < _dispatchPath.Count; index++)
		{
			Widget widget = _dispatchPath[index];

			if (!widget.IsAttached)
			{
				continue;
			}

			UiPointerEvent local = pointer.ToLocal(widget);

			if (handler(widget, local))
			{
				return true;
			}
		}

		return false;
	}

	private bool BubblePointer(Widget target, in UiWheelEvent wheel)
	{
		if (!target.IsAttached)
		{
			return false;
		}

		_dispatchPath.Clear();

		for (Widget? current = target;
			current is not null;
			current = current.Parent)
		{
			_dispatchPath.Add(current);
		}

		for (int index = 0; index < _dispatchPath.Count; index++)
		{
			Widget widget = _dispatchPath[index];

			if (!widget.IsAttached)
			{
				continue;
			}

			if (widget.RaiseWheel(wheel))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsInside(Widget widget, UiPoint point)
	{
		UiRect bounds = widget.GlobalBounds;

		return bounds.Contains(point);
	}

	private static void CollectFocusable(
		Widget widget,
		List<Widget> destination)
	{
		if (!widget.IsVisible || !widget.IsEnabled)
		{
			return;
		}

		if (widget.IsFocusable)
		{
			destination.Add(widget);
		}

		for (int index = 0; index < widget.Children.Count; index++)
		{
			CollectFocusable(widget.Children[index], destination);
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
