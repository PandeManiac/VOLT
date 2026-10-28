using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;

namespace Volt.Ui.Widgets;

/// <summary>
/// A clipping viewport with one scrollable content widget and an
/// optional scrollbar.
/// </summary>
/// <remarks>
/// Vertical scrolling constrains the content width and measures its
/// height unconstrained; horizontal scrolling does the reverse. The
/// scroll offset is clamped after content is measured, so a shrinking
/// document cannot leave the viewport beyond its end. Only one axis
/// scrolls at a time.
/// </remarks>
public class ScrollView : Widget
{
	private Widget? _content;
	private float _maxScroll;
	private float _thumbGrabOffset;
	private bool _draggingThumb;

	public ScrollView()
		: this(UiOrientation.Vertical)
	{
	}

	public ScrollView(UiOrientation orientation)
	{
		Orientation = orientation;
		ClipToBounds = true;
		IsFocusable = true;
		FocusOnPointerPress = false;
	}

	public UiOrientation Orientation { get; }

	public bool ShowScrollBar { get; set; } = true;

	public float ScrollBarThickness { get; set; }

	public UiPoint ScrollOffset { get; private set; }

	public float MaximumScroll => _maxScroll;

	public Widget? Content
	{
		get => _content;
		set
		{
			if (_content == value)
			{
				return;
			}

			if (_content is not null)
			{
				Remove(_content);
			}

			_content = value;

			if (value is not null)
			{
				Add(value);
			}

			InvalidateMeasure();
		}
	}

	public void ScrollToStart()
	{
		SetScrollOffset(0f);
	}

	public void ScrollToEnd()
	{
		SetScrollOffset(_maxScroll);
	}

	public void SetScrollOffset(float offset)
	{
		if (!float.IsFinite(offset))
		{
			return;
		}

		offset = Math.Clamp(offset, 0f, _maxScroll);

		UiPoint next = Orientation == UiOrientation.Vertical
			? new UiPoint(0f, offset)
			: new UiPoint(offset, 0f);

		if (ScrollOffset == next)
		{
			return;
		}

		ScrollOffset = next;
		InvalidateArrange();
		InvalidateVisual();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		if (_content is not Widget content)
		{
			return UiSize.Zero;
		}

		float track = ShowScrollBar ? TrackThickness : 0f;
		float availableWidth = contentAvailable.Width;
		float availableHeight = contentAvailable.Height;

		UiSize contentMeasure;
		float desiredWidth;
		float desiredHeight;

		if (Orientation == UiOrientation.Vertical)
		{
			float width = float.IsFinite(availableWidth)
				? MathF.Max(0f, availableWidth - track)
				: float.PositiveInfinity; content.Measure(new UiSize(width, float.PositiveInfinity));
			contentMeasure = content.DesiredSize;
			desiredWidth = contentMeasure.Width + track;
			desiredHeight = contentMeasure.Height;
		}

		else
		{
			float height = float.IsFinite(availableHeight)
				? MathF.Max(0f, availableHeight - track)
				: float.PositiveInfinity; content.Measure(new UiSize(float.PositiveInfinity, height));
			contentMeasure = content.DesiredSize;
			desiredWidth = contentMeasure.Width;
			desiredHeight = contentMeasure.Height + track;
		}

		if (float.IsFinite(availableWidth))
		{
			desiredWidth = MathF.Min(desiredWidth, availableWidth);
		}

		if (float.IsFinite(availableHeight))
		{
			desiredHeight = MathF.Min(desiredHeight, availableHeight);
		}

		return new UiSize(desiredWidth, desiredHeight);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		if (_content is not Widget content)
		{
			return;
		}

		float track = ShowScrollBar ? TrackThickness : 0f;

		float contentWidth;
		float contentHeight;
		float viewportWidth;
		float viewportHeight;

		if (Orientation == UiOrientation.Vertical)
		{
			viewportWidth = MathF.Max(0f, contentBounds.Width - track);
			viewportHeight = contentBounds.Height;
			contentWidth = viewportWidth; content.Measure(
					new UiSize(contentWidth, float.PositiveInfinity));

			contentHeight = MathF.Max(
				viewportHeight,
				content.DesiredSize.Height);

			_maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
			float offset = Math.Clamp(
				ScrollOffset.Y,
				0f,
				_maxScroll);

			ScrollOffset = new UiPoint(0f, offset); ArrangeChildExact(
					content,
					new UiRect(
						contentBounds.X,
						contentBounds.Y - offset,
						contentWidth,
						contentHeight));
		}

		else
		{
			viewportWidth = contentBounds.Width;
			viewportHeight = MathF.Max(
				0f,
				contentBounds.Height - track);
			contentHeight = viewportHeight; content.Measure(
					new UiSize(float.PositiveInfinity, contentHeight));

			contentWidth = MathF.Max(
				viewportWidth,
				content.DesiredSize.Width);

			_maxScroll = MathF.Max(0f, contentWidth - viewportWidth);
			float offset = Math.Clamp(
				ScrollOffset.X,
				0f,
				_maxScroll);

			ScrollOffset = new UiPoint(offset, 0f); ArrangeChildExact(
					content,
					new UiRect(
						contentBounds.X - offset,
						contentBounds.Y,
					contentWidth,
					contentHeight));
		}
	}

	protected override void RenderOverlay(UiDrawList drawList)
	{
		if (!ShowScrollBar || _maxScroll <= 0f)
		{
			return;
		}

		UiRect track = GetTrackRect();
		UiRect thumb = GetThumbRect();

		drawList.AddRect(track, Context?.Theme.Palette.Header ?? default);
		drawList.AddRoundedRect(
			thumb,
			MathF.Max(2f, TrackThickness * 0.5f),
			Context?.Theme.Palette.BorderStrong ?? default);
	}

	protected internal override bool OnWheel(UiWheelEvent wheel)
	{
		if (_maxScroll <= 0f)
		{
			return false;
		}

		float delta = Orientation == UiOrientation.Vertical
			? wheel.Delta.Y
			: wheel.Delta.X;

		if (delta == 0f)
		{
			return false;
		}

		SetScrollOffset(ScrollOffsetAxis - (delta * 48f));
		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		if (_maxScroll <= 0f)
		{
			return false;
		}

		float page = MathF.Max(24f, ContentBounds.Height - 24f);
		float next = ScrollOffsetAxis;

		switch (key.Key)
		{
			case UiKey.PageUp:
				next -= page;
				break;

			case UiKey.PageDown:
				next += page;
				break;

			case UiKey.Home:
				next = 0f;
				break;

			case UiKey.End:
				next = _maxScroll;
				break;

			default:
				return false;
		}

		SetScrollOffset(next);
		return true;
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		if (_maxScroll <= 0f || !ShowScrollBar)
		{
			return false;
		}

		UiRect thumb = GetThumbRect();

		if (!thumb.Contains(pointer.GlobalPosition))
		{
			return false;
		}

		Focus();
		_thumbGrabOffset = Orientation == UiOrientation.Vertical
			? pointer.GlobalPosition.Y - thumb.Y
			: pointer.GlobalPosition.X - thumb.X;

		_draggingThumb = CapturePointer(pointer.Button);
		return _draggingThumb;
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (!_draggingThumb)
		{
			return false;
		}

		UiRect track = GetTrackRect();
		UiRect thumb = GetThumbRect();

		float trackLength = Orientation == UiOrientation.Vertical
			? track.Height
			: track.Width;

		float thumbLength = Orientation == UiOrientation.Vertical
			? thumb.Height
			: thumb.Width;

		float travel = MathF.Max(1f, trackLength - thumbLength);
		float position = Orientation == UiOrientation.Vertical
			? pointer.GlobalPosition.Y - track.Y - _thumbGrabOffset
			: pointer.GlobalPosition.X - track.X - _thumbGrabOffset;

		float ratio = Math.Clamp(position / travel, 0f, 1f);
		SetScrollOffset(ratio * _maxScroll);

		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (!_draggingThumb)
		{
			return false;
		}

		_draggingThumb = false;
		ReleasePointer();
		return true;
	}

	protected override void OnDetached()
	{
		_draggingThumb = false;
	}

	private float TrackThickness =>
		ScrollBarThickness > 0f
			? ScrollBarThickness
			: Context?.Theme.Metrics.ScrollBarThickness ?? 10f;

	private float ScrollOffsetAxis =>
		Orientation == UiOrientation.Vertical
			? ScrollOffset.Y
			: ScrollOffset.X;

	private UiRect GetTrackRect()
	{
		float thickness = TrackThickness;

		// The track is drawn and hit-tested in root coordinates, so it
		// follows the global content rectangle.
		UiRect content = GlobalContentBounds;

		return Orientation == UiOrientation.Vertical
			? new UiRect(
				content.Right - thickness,
				content.Y,
				thickness,
				content.Height)
			: new UiRect(
				content.X,
				content.Bottom - thickness,
				content.Width,
				thickness);
	}

	private UiRect GetThumbRect()
	{
		UiRect track = GetTrackRect();
		float contentLength;
		float viewportLength;

		if (Orientation == UiOrientation.Vertical)
		{
			viewportLength = ContentBounds.Height;
			contentLength = viewportLength + _maxScroll;
		}

		else
		{
			viewportLength = ContentBounds.Width;
			contentLength = viewportLength + _maxScroll;
		}

		if (contentLength <= 0f)
		{
			return UiRect.Empty;
		}

		float ratio = Math.Clamp(
			viewportLength / contentLength,
			0.08f,
			1f);

		if (Orientation == UiOrientation.Vertical)
		{
			float height = track.Height * ratio;
			float travel = track.Height - height;
			float position = _maxScroll <= 0f
				? 0f
				: (ScrollOffset.Y / _maxScroll) * travel;

			return new UiRect(
				track.X + 2f,
				track.Y + position,
				MathF.Max(2f, track.Width - 4f),
				MathF.Max(8f, height));
		}

		float width = track.Width * ratio;
		float xTravel = track.Width - width;
		float x = _maxScroll <= 0f
			? 0f
			: (ScrollOffset.X / _maxScroll) * xTravel;

		return new UiRect(
			track.X + x,
			track.Y + 2f,
			MathF.Max(8f, width),
			MathF.Max(2f, track.Height - 4f));
	}
}
