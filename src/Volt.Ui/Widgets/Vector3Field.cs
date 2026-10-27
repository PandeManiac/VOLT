using System.Numerics;
using Volt.Ui.Layout;

namespace Volt.Ui.Widgets;

/// <summary>
/// Three numeric fields editing a vector's X, Y, and Z components.
/// </summary>
public sealed class Vector3Field : Widget
{
	private readonly NumericField _x;
	private readonly NumericField _y;
	private readonly NumericField _z;
	private readonly StackPanel _row;
	private Vector3 _value;
	private bool _updating;

	public Vector3Field()
	{
		_x = CreateComponent("X");
		_y = CreateComponent("Y");
		_z = CreateComponent("Z");

		_row = new StackPanel(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_row.Add(_x);
		_row.Add(_y);
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

	private NumericField CreateComponent(string name)
	{
		NumericField field = new()
		{
			Format = "0.###",
			Step = 0.1f,
			MinHeight = 20f,
			Name = name,
			MinWidth = 40f,
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
