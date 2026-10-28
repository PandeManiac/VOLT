using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;
using Volt.Ui.Widgets.Properties;

namespace Volt.Editor.Panels;

public sealed class InspectorPanel : Panel
{
	private readonly EditorSession _session;
	private readonly StackPanel _stack;
	private readonly StackPanel _componentBar;
	private readonly StackPanel _addBar;
	private readonly TextField _name;
	private readonly PropertyGrid _grid;
	private readonly Dropdown _componentTypes;
	private readonly EmptyStateView _empty;
	private readonly IconView _entityIcon;
	private readonly Label _entitySubtitle;

	private Guid? _boundEntityId;
	private string _componentSignature = string.Empty;
	private bool _updating;
	private bool _disposed;

	public InspectorPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(6f);

		_stack = new StackPanel
		{
			Spacing = 4f
		};

		_name = new TextField
		{
			Height = UiLength.Points(26f)
		};

		_name.EditingEnded += OnNameCommitted;

		_entityIcon = new IconView(UiIcon.Cube)
		{
			Width = UiLength.Points(22f),
			Height = UiLength.Points(22f),
			Color = Context?.Theme.Palette.Success,
			VerticalAlignment = UiAlignment.Center
		};

		_entitySubtitle = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small,
			TextColor = Context?.Theme.Palette.TextMuted,
			VerticalAlignment = UiAlignment.Center,
			ClipToBounds = true
		};

		StackPanel nameRow = new(UiOrientation.Horizontal)
		{
			Spacing = 6f
		};

		nameRow.Add(_entityIcon);
		nameRow.Add(_name);

		_stack.Add(nameRow);
		_stack.Add(_entitySubtitle);

		_componentBar = new StackPanel
		{
			Spacing = 2f
		};

		_stack.Add(_componentBar);

		_addBar = new StackPanel(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_componentTypes = new Dropdown
		{
			FlexGrow = 1f,
			MinWidth = 80f
		};

		_componentTypes.SetItems(
			ComponentCatalog.Default.All.Select(
				descriptor => descriptor.DisplayName),
			0);

		Button add = new("Add Component")
		{
			MinHeight = 24f
		};

		add.Clicked += _ => AddSelectedComponent();

		_addBar.Add(_componentTypes);
		_addBar.Add(add);
		_stack.Add(_addBar);

		_grid = new PropertyGrid
		{
			FlexGrow = 1f,
			MinHeight = 80f
		};

		_stack.Add(_grid);

		_empty = new EmptyStateView(
			UiIcon.Inspector,
			"Nothing selected",
			"Select an object in the Hierarchy or Scene view to edit its components.");

		Add(_stack);
		Add(_empty);

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.ProjectChanged += OnSessionChanged;

		Rebuild();
	}

	protected override void OnDetached()
	{
		Unsubscribe();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Unsubscribe();
		base.Dispose();
	}

	private void OnSessionChanged(EditorSession session)
	{
		SceneEntity? entity = _session.ActiveEntity;

		if (entity is null)
		{
			if (_boundEntityId is not null)
			{
				Rebuild();
			}

			return;
		}

		string componentSignature = BuildComponentSignature(entity);

		if (_boundEntityId != entity.Id ||
			_componentSignature != componentSignature)
		{
			Rebuild();
			return;
		}

		if (!_name.HasFocus)
		{
			_name.Text = entity.Name;
		}

		// A box selection changes how many objects are selected without
		// changing the object being edited.
		UpdateSubtitle(entity);

		_grid.RefreshValues();
	}

	private void Rebuild()
	{
		if (_updating)
		{
			return;
		}

		_updating = true;

		SceneEntity? entity = _session.ActiveEntity;

		_boundEntityId = entity?.Id;
		_componentSignature = entity is null
			? string.Empty
			: BuildComponentSignature(entity);

		_name.IsVisible = entity is not null;
		_componentBar.IsVisible = entity is not null;
		_addBar.IsVisible = entity is not null;
		_grid.IsVisible = entity is not null;
		_empty.IsVisible = entity is null;

		if (entity is null)
		{
			_name.Text = string.Empty;
			_componentBar.ClearChildren();
			_grid.SetGroups([]);
			_updating = false;
			return;
		}

		_name.Text = entity.Name;

		UiPalette palette = Context?.Theme.Palette ??
			UiTheme.CreateDark().Palette;

		_entityIcon.Icon = EditorIcons.ForEntity(entity);
		_entityIcon.Color = EditorIcons.EntityColor(entity, palette);
		UpdateSubtitle(entity);

		RebuildComponentBar(entity);
		_grid.SetGroups(BuildGroups(entity));

		_updating = false;
	}

	/// <summary>
	/// Updates the line under the name: how many objects are selected,
	/// what a transform edit reaches, and what this one is made of.
	/// </summary>
	/// <remarks>
	/// The values in the grid are the active object's, but a transform
	/// edit applies to the whole selection, so the line says so rather
	/// than leaving the reach to guesswork.
	/// </remarks>
	private void UpdateSubtitle(SceneEntity entity)
	{
		int selectedCount = _session.Selection.Count;

		string selected = selectedCount > 1
			? $"{selectedCount} selected  ·  edits apply to all  ·  "
			: string.Empty;

		_entitySubtitle.Text =
			$"{selected}{entity.Components.Count} component" +
			$"{(entity.Components.Count == 1 ? string.Empty : "s")}" +
			$"  ·  {entity.Id.ToString()[..8]}";
	}

	private void RebuildComponentBar(SceneEntity entity)
	{
		for (int index = _componentBar.Children.Count - 1;
			index >= 0;
			index--)
		{
			_componentBar.Children[index].Dispose();
		}

		_componentBar.ClearChildren();

		foreach (AuthoredComponent component in entity.Components)
		{
			StackPanel row = new(UiOrientation.Horizontal)
			{
				Spacing = 4f
			};

			Label label = new(component.DisplayName)
			{
				FlexGrow = 1f,
				VerticalAlignment = UiAlignment.Center,
				TooltipText = component.TypeId,
				Font = Context?.Theme.Fonts.Small
			};

			IconButton remove = new(
				UiIcon.Close,
				$"Remove {component.DisplayName}")
			{
				Width = UiLength.Points(20f),
				Height = UiLength.Points(20f),
				MinWidth = 20f,
				MinHeight = 20f
			};

			AuthoredComponent captured = component;

			remove.Clicked += _ =>
			{
				try
				{
					_session.RemoveComponent(entity.Id, captured);
				}

				catch (Exception exception)
				{
					_session.Log(exception.Message, EditorLogLevel.Error);
				}
			};

			row.Add(label);
			row.Add(remove);
			_componentBar.Add(row);
		}
	}

	private IReadOnlyList<UiPropertyGroup> BuildGroups(
		SceneEntity entity)
	{
		List<UiPropertyGroup> groups = [];

		List<UiProperty> transformProperties =
		[
			new Vector3Property(
				"Position",
				() => entity.Transform.Position,
				value => ApplyTransform(entity, transform =>
					transform.Position = value)),
			new Vector3Property(
				"Rotation",
				() => entity.Transform.EulerAngles,
				value => ApplyTransform(entity, transform =>
					transform.EulerAngles = value)),
			new Vector3Property(
				"Scale",
				() => entity.Transform.Scale,
				value => ApplyTransform(entity, transform =>
					transform.Scale = value))
		];

		groups.Add(new UiPropertyGroup("Transform", transformProperties)
		{
			Icon = UiIcon.Move
		});

		foreach (AuthoredComponent component in entity.Components)
		{
			List<UiProperty> properties = [];

			if (component is UnknownComponentData unknown)
			{
				properties.Add(new ReadOnlyTextProperty(
					"Unsupported type",
					() => unknown.TypeId));

				properties.Add(new ReadOnlyTextProperty(
					"Preserved data",
					() => unknown.Data.GetRawText()));
			}

			else if (ComponentCatalog.Default.TryGet(
				component.TypeId,
				out EditorComponentDescriptor descriptor))
			{
				foreach (EditorPropertyDescriptor property in
					descriptor.Properties)
				{
					UiProperty? uiProperty = CreateProperty(
						entity,
						component,
						property);

					if (uiProperty is not null)
					{
						properties.Add(uiProperty);
					}
				}
			}

			else
			{
				properties.Add(new ReadOnlyTextProperty(
					"Component type",
					() => component.TypeId));
			}

			groups.Add(new UiPropertyGroup(
				component.DisplayName,
				properties)
			{
				Icon = EditorIcons.ForComponent(component.TypeId)
			});
		}

		groups.Add(new UiPropertyGroup(
			"Scene",
			[
				new ReadOnlyTextProperty("Entity Id", () => entity.Id.ToString()),
				new ReadOnlyTextProperty(
					"Parent",
					() => entity.ParentId is Guid parent
						? _session.Scene?.Find(parent)?.Name ?? parent.ToString()
						: "None")
			])
		{
			Icon = UiIcon.Info
		});

		return groups;
	}

	private UiProperty? CreateProperty(
		SceneEntity entity,
		AuthoredComponent component,
		EditorPropertyDescriptor property)
	{
		try
		{
			return property.Kind switch
			{
				EditorPropertyKind.Boolean => new BoolProperty(
					property.DisplayName,
					() => property.GetValue(component) is true,
					value => SetProperty(entity, component, property, value)),

				EditorPropertyKind.Integer => new IntProperty(
					property.DisplayName,
					() => property.GetValue(component) is int integer
						? integer
						: 0,
					value => SetProperty(entity, component, property, value))
				{
					Minimum = ToInt(property.Minimum, int.MinValue),
					Maximum = ToInt(property.Maximum, int.MaxValue)
				},

				EditorPropertyKind.Float => new FloatProperty(
					property.DisplayName,
					() => property.GetValue(component) is float number
						? number
						: 0f,
					value => SetProperty(entity, component, property, value))
				{
					Minimum = property.Minimum,
					Maximum = property.Maximum
				},

				EditorPropertyKind.Enum => new EnumProperty(
					property.DisplayName,
					property.EnumNames,
					() => FindEnumIndex(
						property.EnumNames,
						property.GetValue(component)?.ToString()),
					index => SetProperty(
						entity,
						component,
						property,
						Enum.ToObject(
							property.ValueType,
							Math.Clamp(
								index,
								0,
								Math.Max(0, property.EnumNames.Count - 1))))),

				EditorPropertyKind.Vector3 => new Vector3Property(
					property.DisplayName,
					() => property.GetValue(component) is Vector3 vector
						? vector
						: Vector3.Zero,
					value => SetProperty(entity, component, property, value)),

				EditorPropertyKind.Asset => new AssetProperty(
					property.DisplayName,
					() => property.GetValue(component) as string,
					value => SetProperty(entity, component, property, value)),

				EditorPropertyKind.Text => new StringProperty(
					property.DisplayName,
					() => property.GetValue(component) as string ?? string.Empty,
					value => SetProperty(entity, component, property, value)),

				_ => new ReadOnlyTextProperty(
					property.DisplayName,
					() => property.GetValue(component)?.ToString() ?? "None")
			};
		}

		catch (NotSupportedException exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Warning);
			return new ReadOnlyTextProperty(
				property.DisplayName,
				() => property.GetValue(component)?.ToString() ?? "None");
		}
	}

	private void SetProperty(
		SceneEntity entity,
		AuthoredComponent component,
		EditorPropertyDescriptor property,
		object? value)
	{
		try
		{
			_session.SetComponentProperty(
				entity.Id,
				component,
				property,
				value);
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void ApplyTransform(
		SceneEntity entity,
		Action<TransformData> mutate)
	{
		try
		{
			TransformData next = entity.Transform.Clone();
			mutate(next);

			if (!next.Equals(entity.Transform))
			{
				// The rest of the selection follows the edited object,
				// so a multi-object edit is still one undo step.
				_session.SetSelectionTransform(entity.Id, next);
			}
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void OnNameCommitted(TextField field)
	{
		if (_updating || _session.ActiveEntity is not SceneEntity entity)
		{
			return;
		}

		if (string.Equals(
			entity.Name,
			field.Text,
			StringComparison.Ordinal))
		{
			return;
		}

		try
		{
			_session.RenameActiveEntity(field.Text);
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private void AddSelectedComponent()
	{
		if (_session.ActiveEntity is not SceneEntity entity ||
			_componentTypes.SelectedIndex < 0 ||
			_componentTypes.SelectedIndex >=
				ComponentCatalog.Default.All.Count)
		{
			return;
		}

		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.All[_componentTypes.SelectedIndex];

		try
		{
			_session.AddComponent(entity.Id, descriptor.TypeId);
		}

		catch (Exception exception)
		{
			_session.Log(exception.Message, EditorLogLevel.Error);
		}
	}

	private static string BuildComponentSignature(SceneEntity entity)
	{
		return string.Join(
			'|',
			entity.Components.Select(component => component.TypeId));
	}

	private static int ToInt(float value, int fallback)
	{
		if (!float.IsFinite(value))
		{
			return fallback;
		}

		return (int)MathF.Round(value);
	}

	private static int FindEnumIndex(
		IReadOnlyList<string> names,
		string? value)
	{
		for (int index = 0; index < names.Count; index++)
		{
			if (string.Equals(names[index], value, StringComparison.Ordinal))
			{
				return index;
			}
		}

		return 0;
	}

	private void Unsubscribe()
	{
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
		_session.ProjectChanged -= OnSessionChanged;
	}
}
