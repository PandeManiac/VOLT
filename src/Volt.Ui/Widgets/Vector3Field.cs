using System.Numerics;
using Volt.Ui.Layout;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// Three numeric fields editing a vector's X, Y, and Z components.
/// </summary>
/// <remarks>
/// Each component carries its axis letter in the conventional muted
/// red/green/blue, which is what lets a developer read a position row
/// without counting columns. The labels sit before the fields rather
/// than inside them so the numeric text stays on the same baseline as
/// every other inspector field.
/// </remarks>
public sealed class Vector3Field : Widget
{
	private readonly NumericField _x;
	private readonly NumericField _y;
	private readonly NumericField _z;
	private readonly Label _xLabel;
	private readonly Label _yLabel;
	private readonly Label _zLabel;
	private readonly StackPanel _row;
	private Vector3 _value;
	private bool _updating;

	public Vector3Field()
	{
		_x = CreateComponent("X");
		_y = CreateComponent("Y");
		_z = CreateComponent("Z");

		_xLabel = CreateAxisLabel("X");
		_yLabel = CreateAxisLabel("Y");
		_zLabel = CreateAxisLabel("Z");

		_xLabel.TextColor = UiAxisColors.X;
		_yLabel.TextColor = UiAxisColors.Y;
		_zLabel.TextColor = UiAxisColors.Z;

		_row = new StackPanel(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_row.Add(_xLabel);
		_row.Add(_x);
		_row.Add(_yLabel);
		_row.Add(_y);
		_row.Add(_zLabel);
		_row.Add(_z);

		Add(_row);
	}

	public event Action<Vector3Field>? ValueChanged;

	public bool IsEditing =>
		_x.HasFocus || _y.HasFocus || _z.HasFocus;

	public Vector3 Value
	{
		get => _value;
		set
		{
			if (value == _value)
			{
				return;
			}

			_value = value;
			RefreshFields();
			ValueChanged?.Invoke(this);
		}
	}

	public float Minimum
	{
		get => _x.Minimum;
		set
		{
			_x.Minimum = value;
			_y.Minimum = value;
			_z.Minimum = value;
		}
	}

	public float Maximum
	{
		get => _x.Maximum;
		set
		{
			_x.Maximum = value;
			_y.Maximum = value;
			_z.Maximum = value;
		}
	}

	public float Step
	{
		get => _x.Step;
		set
		{
			_x.Step = value;
			_y.Step = value;
			_z.Step = value;
		}
	}

	public string Format
	{
		get => _x.Format;
		set
		{
			_x.Format = value;
			_y.Format = value;
			_z.Format = value;
		}
	}

	public void SetValue(Vector3 value, bool notify)
	{
		_value = value;
		RefreshFields();

		if (notify)
		{
			ValueChanged?.Invoke(this);
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiSize desired = MeasureChild(
			_row,
			new UiSize(
				float.IsFinite(contentAvailable.Width)
					? contentAvailable.Width
					: 320f,
				contentAvailable.Height));

		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: desired.Width,
			desired.Height);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		ArrangeChildExact(
			_row,
			new UiRect(
				contentBounds.X,
				contentBounds.Y,
				contentBounds.Width,
				contentBounds.Height));
	}

	private static Label CreateAxisLabel(string axis)
	{
		return new Label(axis)
		{
			Width = UiLength.Points(12f),
			MinWidth = 10f,
			VerticalAlignment = UiAlignment.Center,
			TextAlignment = Volt.Ui.Text.UiTextAlignment.Start
		};
	}

	private NumericField CreateComponent(string name)
	{
		NumericField field = new()
		{
			Format = "0.###",
			Step = 0.1f,
			MinHeight = 20f,
			Name = name,
			MinWidth = 34f,
			FlexGrow = 1f,
			FlexShrink = 1f
		};

		field.ValueChanged += _ => OnComponentChanged();

		return field;
	}

	private void OnComponentChanged()
	{
		if (_updating)
		{
			return;
		}

		_value = new Vector3(_x.Value, _y.Value, _z.Value);
		ValueChanged?.Invoke(this);
	}

	private void RefreshFields()
	{
		_updating = true;

		try
		{
			_x.SetValue(_value.X, notify: false);
			_y.SetValue(_value.Y, notify: false);
			_z.SetValue(_value.Z, notify: false);
		}

		finally
		{
			_updating = false;
		}
	}
}

/// <summary>
/// The conventional axis colors used by transform editors.
/// </summary>
/// <remarks>
/// The values are muted so a row of three colored letters reads as a
/// hint rather than as three competing accents. They intentionally
/// match the viewport gizmo axes: red X, green Y, blue Z.
/// </remarks>
public static class UiAxisColors
{
	public static UiColor X => UiColor.FromRgb(0xD96A6A);

	public static UiColor Y => UiColor.FromRgb(0x7CC98A);

	public static UiColor Z => UiColor.FromRgb(0x6AA2E0);
}
