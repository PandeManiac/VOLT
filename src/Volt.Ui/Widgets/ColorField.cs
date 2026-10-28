using System.Globalization;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A color swatch with an editable hexadecimal representation.
/// </summary>
public sealed class ColorField : Widget
{
	private readonly TextField _hex;
	private UiColor _value;
	private bool _updating;

	public ColorField()
	{
		_hex = new TextField
		{
			Placeholder = "#RRGGBB",
			Width = UiLength.Auto
		};

		_hex.EditingEnded += _ => CommitHex();
		Add(_hex);
		_value = UiColor.White;
		_hex.Text = Format(_value);
	}

	public event Action<ColorField>? ValueChanged;

	public bool IsEditing => _hex.HasFocus;

	public UiColor Value
	{
		get => _value;
		set => SetValue(value, notify: true);
	}

	public void SetValue(UiColor value, bool notify)
	{
		if (value == _value)
		{
			return;
		}

		_value = value;
		_updating = true;

		try
		{
			_hex.Text = Format(_value);
		}

		finally
		{
			_updating = false;
		}

		InvalidateVisual();

		if (notify)
		{
			ValueChanged?.Invoke(this);
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiSize field = MeasureChild(
			_hex,
			new UiSize(
				float.IsFinite(contentAvailable.Width)
					? MathF.Max(0f, contentAvailable.Width - 30f)
					: 160f,
				contentAvailable.Height));

		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: field.Width + 30f,
			MathF.Max(20f, field.Height));
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		ArrangeChildExact(
			_hex,
			new UiRect(
				contentBounds.X + 30f,
				contentBounds.Y,
				MathF.Max(0f, contentBounds.Width - 30f),
				contentBounds.Height));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect content = GlobalContentBounds;
		float size = MathF.Min(22f, content.Height);

		UiRect swatch = new(
			content.X,
			content.Y + ((content.Height - size) * 0.5f),
			size,
			size);

		drawList.AddRoundedRect(
			swatch,
			theme.Metrics.SmallCornerRadius,
			_value);

		drawList.AddBorder(
			swatch,
			theme.Palette.BorderStrong,
			1f,
			theme.Metrics.SmallCornerRadius);
	}

	private void CommitHex()
	{
		if (_updating)
		{
			return;
		}

		if (!TryParse(_hex.Text, out UiColor parsed))
		{
			_hex.Text = Format(_value);
			return;
		}

		if (parsed == _value)
		{
			_hex.Text = Format(_value);
			return;
		}

		_value = parsed;
		InvalidateVisual();
		_hex.Text = Format(_value);
		ValueChanged?.Invoke(this);
	}

	private static string Format(UiColor color)
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"#{color.R:X2}{color.G:X2}{color.B:X2}");
	}

	private static bool TryParse(string text, out UiColor color)
	{
		color = default;

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string value = text.Trim();

		if (value.StartsWith('#'))
		{
			value = value[1..];
		}

		if (value.Length is not (6 or 8) ||
			!uint.TryParse(
				value,
				NumberStyles.HexNumber,
				CultureInfo.InvariantCulture,
				out uint packed))
		{
			return false;
		}

		color = value.Length == 6
			? UiColor.FromRgb(packed)
			: UiColor.FromArgb(packed);

		return true;
	}
}
