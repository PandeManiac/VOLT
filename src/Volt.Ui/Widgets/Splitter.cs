using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A draggable divider that reports movement along one axis.
/// </summary>
/// <remarks>
/// The splitter does not own the panes it separates. Its parent or a
/// docking host receives <see cref="Dragged"/> and applies the delta
/// to its own layout model. This keeps the widget reusable outside the
/// editor.
/// </remarks>
public sealed class Splitter : Widget
{
	private bool _dragging;
	private UiPoint _lastPosition;

	public Splitter()
		: this(UiOrientation.Vertical)
	{
	}

	public Splitter(UiOrientation orientation)
	{
		Orientation = orientation;
		Cursor = orientation == UiOrientation.Vertical
			? UiCursor.ResizeHorizontal
			: UiCursor.ResizeVertical;

		if (orientation == UiOrientation.Vertical)
		{
			Width = UiLength.Points(4f);
		}

		else
		{
			Height = UiLength.Points(4f);
		}
	}

	public UiOrientation Orientation { get; set; }

	public event Action<Splitter, float>? Dragged;

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return Orientation == UiOrientation.Vertical
			? new UiSize(4f, contentAvailable.Height)
			: new UiSize(contentAvailable.Width, 4f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalVisualBounds;

		UiColor color = IsPressed
			? theme.Palette.Accent
			: IsHovered
				? theme.Palette.BorderStrong
				: theme.Palette.Border;

		drawList.AddRect(bounds, color);

		if (Orientation == UiOrientation.Vertical &&
			bounds.Height >= 24f)
		{
			float x = bounds.Center.X;
			float y = bounds.Center.Y;

			for (int index = -1; index <= 1; index++)
			{
				drawList.AddRect(
					new UiRect(x - 0.5f, y + (index * 4f) - 0.5f, 1f, 1f),
					theme.Palette.TextMuted);
			}
		}
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		_lastPosition = pointer.GlobalPosition;
		_dragging = CapturePointer(pointer.Button);
		return _dragging;
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (!_dragging)
		{
			return false;
		}

		float delta = Orientation == UiOrientation.Vertical
			? pointer.GlobalPosition.X - _lastPosition.X
			: pointer.GlobalPosition.Y - _lastPosition.Y;

		_lastPosition = pointer.GlobalPosition;
		Dragged?.Invoke(this, delta);
		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (!_dragging)
		{
			return false;
		}

		_dragging = false;
		ReleasePointer();
		return true;
	}

	protected override void OnDetached()
	{
		_dragging = false;
	}
}
