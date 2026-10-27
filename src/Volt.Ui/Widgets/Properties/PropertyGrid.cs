using System.Globalization;
using Volt.Ui.Layout;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets.Properties;

/// <summary>
/// A reusable inspector that builds typed editors for cached property
/// descriptors.
/// </summary>
/// <remarks>
/// Reflection is not used here. An editor adapter creates descriptors
/// for the selected object once and updates the values behind them.
/// <see cref="RefreshValues"/> pulls external changes into idle
/// controls without overwriting a field that is being edited.
/// </remarks>
public sealed class PropertyGrid : ScrollView
{
	private readonly StackPanel _stack;
	private readonly List<Row> _rows = [];

	private IReadOnlyList<UiPropertyGroup> _groups = [];

	private sealed record Row(
		UiProperty Property,
		Widget Editor,
		Action Sync);

	public PropertyGrid()
	{
		_stack = new StackPanel
		{
			Spacing = 2f
		};

		Content = _stack;
	}

	public IReadOnlyList<UiPropertyGroup> Groups => _groups;

	public void SetProperties(IEnumerable<UiProperty> properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		SetGroups(
			[
				new UiPropertyGroup("Properties", [.. properties])
			]);
	}

	public void SetGroups(IReadOnlyList<UiPropertyGroup> groups)
	{
		ArgumentNullException.ThrowIfNull(groups);
		_groups = groups;
		_rows.Clear();

		for (int index = _stack.Children.Count - 1; index >= 0; index--)
		{
			_stack.Children[index].Dispose();
		}

		_stack.ClearChildren();

		for (int groupIndex = 0;
			groupIndex < groups.Count;
			groupIndex++)
		{
			UiPropertyGroup group = groups[groupIndex];

			if (group.Name.Length > 0)
			{
				Label header = new(group.Name)
				{
					Font = Context?.Theme.Fonts.Small,
					TextColor = Context?.Theme.Palette.TextMuted,
					Margin = new UiThickness(2f, groupIndex == 0 ? 2f : 8f, 2f, 2f)
				};

				_stack.Add(header);
			}

			for (int index = 0; index < group.Properties.Count; index++)
			{
				BuildRow(group.Properties[index]);
			}
		}

		RefreshValues();
		InvalidateMeasure();
	}

	/// <summary>
	/// Pulls current property values into controls that are not being
	/// edited. The editor calls this once per frame.
	/// </summary>
	public void RefreshValues()
	{
		for (int index = 0; index < _rows.Count; index++)
		{
			Row row = _rows[index];

			if (IsBeingEdited(row.Editor))
			{
				continue;
			}

			row.Sync();
		}
	}

	private void BuildRow(UiProperty property)
	{
		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 6f,
			Padding = new UiThickness(2f, 1f)
		};

		Label label = new(property.Name)
		{
			Width = UiLength.Points(108f),
			HorizontalAlignment = UiAlignment.Start,
			VerticalAlignment = UiAlignment.Center,
			TooltipText = property.Tooltip,
			ClipToBounds = true
		};

		row.Add(label);

		Widget editor = CreateEditor(property, out Action sync);
		editor.FlexGrow = 1f;
		editor.MinWidth = 80f;
		editor.HorizontalAlignment = UiAlignment.Stretch;
		row.Add(editor);
		_stack.Add(row);
		_rows.Add(new Row(property, editor, sync));
	}

	private Widget CreateEditor(
		UiProperty property,
		out Action sync)
	{
		ArgumentNullException.ThrowIfNull(property);

		switch (property)
		{
			case BoolProperty boolProperty:
			{
				CheckBox check = new(boolProperty.Name);
				check.CheckedChanged += _ =>
				{
					if (!property.IsReadOnly)
					{
						boolProperty.Set(check.IsChecked);
					}
				};

				sync = () => check.IsChecked = boolProperty.Get();

				return check;
			}

			case IntProperty intProperty:
			{
				NumericField field = new()
				{
					Format = "0",
					Step = intProperty.Step,
					Minimum = intProperty.Minimum,
					Maximum = intProperty.Maximum
				};

				field.ValueChanged += _ =>
				{
					if (!property.IsReadOnly)
					{
						intProperty.Set((int)MathF.Round(field.Value));
					}
				};

				sync = () => field.SetValue(intProperty.Get(), notify: false);

				return field;
			}

			case FloatProperty floatProperty:
			{
				NumericField field = new()
				{
					Format = floatProperty.Format,
					Step = floatProperty.Step,
					Minimum = floatProperty.Minimum,
					Maximum = floatProperty.Maximum
				};

				field.ValueChanged += _ =>
				{
					if (!property.IsReadOnly)
					{
						floatProperty.Set(field.Value);
					}
				};

				sync = () => field.SetValue(
					floatProperty.Get(),
					notify: false);

				return field;
			}

			case StringProperty stringProperty:
			{
				TextField field = new(stringProperty.Get())
				{
					IsReadOnly = property.IsReadOnly
				};

				field.EditingEnded += _ =>
				{
					if (!property.IsReadOnly)
					{
						stringProperty.Set(field.Text);
					}
				};

				sync = () => field.Text = stringProperty.Get();

				return field;
			}

			case EnumProperty enumProperty:
			{
				Dropdown dropdown = new();
				dropdown.SetItems(
					enumProperty.Options,
					enumProperty.Get());

				dropdown.SelectionChanged += _ =>
				{
					if (!property.IsReadOnly &&
						dropdown.SelectedIndex >= 0)
					{
						enumProperty.Set(dropdown.SelectedIndex);
					}
				};

				sync = () => dropdown.SelectedIndex = enumProperty.Get();

				return dropdown;
			}

			case Vector3Property vectorProperty:
			{
				Vector3Field field = new();
				field.SetValue(vectorProperty.Get(), notify: false);

				field.ValueChanged += _ =>
				{
					if (!property.IsReadOnly)
					{
						vectorProperty.Set(field.Value);
					}
				};

				sync = () => field.SetValue(
					vectorProperty.Get(),
					notify: false);

				return field;
			}

			case ColorProperty colorProperty:
			{
				ColorField field = new()
				{
					Value = colorProperty.Get()
				};

				field.ValueChanged += _ =>
				{
					if (!property.IsReadOnly)
					{
						colorProperty.Set(field.Value);
					}
				};

				sync = () => field.SetValue(
					colorProperty.Get(),
					notify: false);

				return field;
			}

			case AssetProperty assetProperty:
			{
				TextField field = new(assetProperty.Get() ?? string.Empty)
				{
					Placeholder = "None",
					IsReadOnly = property.IsReadOnly
				};

				field.EditingEnded += _ =>
				{
					if (!property.IsReadOnly)
					{
						assetProperty.Set(
							field.Text.Length == 0 ? null : field.Text);
					}
				};

				sync = () => field.Text = assetProperty.Get() ?? string.Empty;

				return field;
			}

			case ReadOnlyTextProperty readOnlyProperty:
			{
				Label value = new(readOnlyProperty.Get())
				{
					TextColor = Context?.Theme.Palette.TextMuted
				};

				sync = () => value.Text = readOnlyProperty.Get();

				return value;
			}

			default:
				throw new NotSupportedException(
					$"Property kind {property.Kind} is not supported.");
		}
	}

	private static bool IsBeingEdited(Widget editor)
	{
		if (editor.HasFocus)
		{
			return true;
		}

		if (editor is Vector3Field vector)
		{
			return vector.IsEditing;
		}

		if (editor is ColorField color)
		{
			return color.IsEditing;
		}

		return false;
	}
}
