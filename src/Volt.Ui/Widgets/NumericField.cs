using System.Globalization;
using Volt.Ui.Input;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A single-line numeric editor with clamping, stepping, and
/// invariant formatting.
/// </summary>
/// <remarks>
/// The displayed text is only parsed when editing commits (Enter or
/// focus loss). Invalid text reverts to the current value rather than
/// silently changing it.
/// <para>
/// Assigning a value that the field already displays and already
/// formatted is a no-op: an inspector refreshes every frame, and
/// re-formatting unchanged values would allocate a string per field
/// per frame for nothing. Text that differs from the canonical
/// rendering, including an in-progress or rejected edit, is still
/// replaced exactly as before.
/// </para>
/// </remarks>
public class NumericField : TextField
{
	private float _value;
	private float _minimum = float.MinValue;
	private float _maximum = float.MaxValue;
	private float _step = 1f;
	private bool _updating;
	private string? _formattedText;
	private string _formattedFormat = string.Empty;

	public NumericField()
	{
		Format = "0.###";
	}

	public NumericField(float value)
		: this()
	{
		Value = value;
	}

	public event Action<NumericField>? ValueChanged;

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
			Value = _value;
		}
	}

	public float Maximum
	{
		get => _maximum;
		set
		{
			_maximum = value;
			Value = _value;
		}
	}

	public float Step
	{
		get => _step;
		set => _step = MathF.Abs(value);
	}

	public string Format { get; set; }

	protected override UiFont DefaultFont(UiTheme theme)
	{
		return theme.Fonts.Mono;
	}

	protected override void CommitText()
	{
		if (!TryParse(Text, out float parsed))
		{
			SetValue(_value, notify: false);
			return;
		}

		SetValue(parsed, notify: true);
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		switch (key.Key)
		{
			case UiKey.Up:
				SetValue(_value + StepFor(key), notify: true);
				return true;

			case UiKey.Down:
				SetValue(_value - StepFor(key), notify: true);
				return true;
		}

		return base.OnKeyDown(key);
	}

	public void SetValue(float value, bool notify)
	{
		float clamped = Math.Clamp(value, _minimum, _maximum);

		if (float.IsNaN(clamped))
		{
			clamped = _value;
		}

		bool changed = clamped != _value || _updating;
		_value = clamped;

		if (!changed && _formattedText is not null &&
			string.Equals(Format, _formattedFormat, StringComparison.Ordinal) &&
			string.Equals(Text, _formattedText, StringComparison.Ordinal))
		{
			return;
		}

		string formatted = _value.ToString(
			Format,
			CultureInfo.InvariantCulture);

		_updating = true;

		try
		{
			ReplaceText(formatted, notify: false);
		}

		finally
		{
			_updating = false;
		}

		_formattedText = formatted;
		_formattedFormat = Format;

		if (notify && changed)
		{
			ValueChanged?.Invoke(this);
		}
	}

	private float StepFor(UiKeyEvent key)
	{
		if (key.HasModifier(UiKeyModifiers.Shift))
		{
			return Step * 10f;
		}

		return Step;
	}

	private static bool TryParse(string text, out float value)
	{
		return float.TryParse(
			text,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value) &&
			float.IsFinite(value);
	}
}
