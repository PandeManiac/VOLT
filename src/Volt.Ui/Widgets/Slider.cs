using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A horizontal value slider with pointer capture and keyboard
/// stepping.
/// </summary>
public class Slider : Widget
{
	private float _value;
	private float _minimum;
	private float _maximum = 1f;
	private float _step;

	public Slider()
	{
		IsFocusable = true;
		Cursor = UiCursor.Hand;
		MinHeight = 18f;
	}

	public event Action<Slider>? ValueChanged;

	public float Value
	{
		get => _value;
		set => SetValue(value, notify: true);
	}

	public float Minimum
	{
		get => _minimum;
		set
		{
			_minimum = value;

			if (_maximum < _minimum)
			{
				_maximum = _minimum;
			}

			SetValue(_value, notify: false);
		}
	}

	public float Maximum
	{
		get => _maximum;
		set
		{
			_maximum = MathF.Max(_minimum, value);
			SetValue(_value, notify: false);
		}
	}

	/// <summary>
	/// Gets or sets a discrete step. Zero means continuous.
	/// </summary>
	public float Step
	{
		get => _step;
		set => _step = MathF.Abs(value);
	}

	public float NormalizedValue =>
		_maximum > _minimum
			? (_value - _minimum) / (_maximum - _minimum)
			: 0f;

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return new UiSize(120f, 16f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect content = GlobalContentBounds;

		if (content.IsEmpty)
		{
			return;
		}

		float trackHeight = 4f;
		float trackY = content.Y + ((content.Height - trackHeight) * 0.5f);
		float thumbRadius = 6f;
		float travel = MathF.Max(0f, content.Width - (thumbRadius * 2f));

		UiRect track = new(
			content.X + thumbRadius,
			trackY,
			travel,
			trackHeight);

		drawList.AddRoundedRect(
			track,
			trackHeight * 0.5f,
			theme.Palette.Border);

		float fillWidth = travel * NormalizedValue;

		if (fillWidth > 0f)
		{
			drawList.AddRoundedRect(
				new UiRect(track.X, track.Y, fillWidth, track.Height),
				trackHeight * 0.5f,
				IsEnabled
					? theme.Palette.Accent
					: theme.Palette.TextDisabled);
		}

		UiPoint thumb = new(
			track.X + fillWidth,
			content.Y + (content.Height * 0.5f));

		UiColor thumbColor = !IsEnabled
			? theme.Palette.TextDisabled
			: IsHovered || IsPressed
				? theme.Palette.AccentHover
				: theme.Palette.Accent;

		drawList.AddRoundedRect(
			new UiRect(
				thumb.X - thumbRadius,
				thumb.Y - thumbRadius,
				thumbRadius * 2f,
				thumbRadius * 2f),
			thumbRadius,
			thumbColor);

		if (HasFocus)
		{
			drawList.AddBorder(
				new UiRect(
					content.X,
					content.Y,
					content.Width,
					content.Height),
				theme.Palette.Accent,
				1f,
				theme.Metrics.SmallCornerRadius);
		}
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();
		CapturePointer(pointer.Button);
		SetFromPosition(pointer.GlobalPosition);
		return true;
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (!IsPointerCaptured)
		{
			return false;
		}

		SetFromPosition(pointer.GlobalPosition);
		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (!IsPointerCaptured)
		{
			return false;
		}

		ReleasePointer();
		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		float step = _step > 0f
			? _step
			: (_maximum - _minimum) / 100f;

		switch (key.Key)
		{
			case UiKey.Left:
			case UiKey.Down:
				SetValue(_value - step, notify: true);
				return true;

			case UiKey.Right:
			case UiKey.Up:
				SetValue(_value + step, notify: true);
				return true;

			case UiKey.Home:
				SetValue(_minimum, notify: true);
				return true;

			case UiKey.End:
				SetValue(_maximum, notify: true);
				return true;
		}

		return false;
	}

	private void SetFromPosition(UiPoint position)
	{
		UiRect content = GlobalContentBounds;
		float usable = MathF.Max(1f, content.Width - 12f);
		float x = position.X - content.X - 6f;
		float ratio = Math.Clamp(x / usable, 0f, 1f);

		SetValue(
			_minimum + ((_maximum - _minimum) * ratio),
			notify: true);
	}

	private void SetValue(float value, bool notify)
	{
		if (_maximum < _minimum)
		{
			_maximum = _minimum;
		}

		value = Math.Clamp(value, _minimum, _maximum);

		if (_step > 0f && notify)
		{
			value = _minimum +
				(MathF.Round((value - _minimum) / _step) * _step);

			value = Math.Clamp(value, _minimum, _maximum);
		}

		if (value == _value)
		{
			return;
		}

		_value = value;
		InvalidateVisual();

		if (notify)
		{
			ValueChanged?.Invoke(this);
		}
	}
}
