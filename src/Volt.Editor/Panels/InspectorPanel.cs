using System.Numerics;
using Volt.Editor.Core.Editing;
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
	private readonly Button _addButton;
	private readonly List<EditorComponentDescriptor> _addableComponents = [];
	private readonly EmptyStateView _empty;
	private readonly IconView _entityIcon;
	private readonly Label _entitySubtitle;

	private Guid? _boundEntityId;
	private string _componentSignature = string.Empty;
	private bool _updating;
	private bool _committingName;
	private float? _restoreScrollOffset;
	private bool _disposed;

	public InspectorPanel(EditorSession session)
		: this(session, state: null)
	{
	}

	internal InspectorPanel(EditorSession session, InspectorPanelState? state)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;
		_restoreScrollOffset = state?.ScrollOffset;

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

		_componentTypes.SetItems(Array.Empty<string>());

		_addButton = new Button("Add Component")
		{
			MinHeight = 24f,
			IsEnabled = false
		};

		_addButton.Clicked += _ => AddSelectedComponent();

		_addBar.Add(_componentTypes);
		_addBar.Add(_addButton);
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

		Rebuild();
	}

	protected override void OnAttached()
	{
		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.ProjectChanged += OnSessionChanged;
		OnSessionChanged(_session);
	}

	protected override void OnDetached()
	{
		// A rebuild replaces the name field, so a rename that is still
		// being typed is committed to the object it was typed on before
		// the panel goes. Without this the typed text is dropped with the
		// old field and the object silently keeps its old name.
		CommitPendingNameEdit();
		Unsubscribe();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		// Detaching is not guaranteed before disposal, so this is the last
		// point where the field still owns a typed rename. The panel is
		// unsubscribed first so the command it raises cannot rebuild a
		// panel that is being torn down.
		_disposed = true;
		Unsubscribe();
		CommitPendingNameEdit();
		base.Dispose();
	}

	private void OnSessionChanged(EditorSession session)
	{
		SceneEntity? entity = _session.ActiveEntity;

		// A rebuild replaces the name field, so an edit that is still in
		// progress is committed before the inspector moves on. A change
		// that keeps the same object bound leaves the typed text alone,
		// so an external rename cannot overwrite it.
		if (entity is null ||
			entity.Id != _boundEntityId ||
			_componentSignature != BuildComponentSignature(entity))
		{
			CommitPendingNameEdit();
		}

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

		if (!_name.HasUncommittedText)
		{
			_name.SetTextFromSource(entity.Name);
		}

		UiPalette palette = Context?.Theme.Palette ??
			UiTheme.CreateDark().Palette;
		_entityIcon.Icon = EditorIcons.ForEntity(entity);
		_entityIcon.Color = EditorIcons.EntityColor(entity, palette);

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
		_entitySubtitle.IsVisible = entity is not null;
		_componentBar.IsVisible = entity is not null;
		_addBar.IsVisible = entity is not null;
		_grid.IsVisible = entity is not null;
		_empty.IsVisible = entity is null;

		if (entity is null)
		{
			_name.SetTextFromSource(string.Empty);
			_entitySubtitle.Text = string.Empty;
			_componentBar.ClearChildren();
			_addableComponents.Clear();
			_componentTypes.SetItems(Array.Empty<string>());
			_componentTypes.IsEnabled = false;
			_addButton.IsEnabled = false;
			_grid.SetGroups([]);
			_updating = false;
			return;
		}

		_name.SetTextFromSource(entity.Name);

		UiPalette palette = Context?.Theme.Palette ??
			UiTheme.CreateDark().Palette;

		_entityIcon.Icon = EditorIcons.ForEntity(entity);
		_entityIcon.Color = EditorIcons.EntityColor(entity, palette);
		UpdateSubtitle(entity);

		RebuildAddableComponents(entity);
		RebuildComponentBar(entity);
		_grid.SetGroups(BuildGroups(entity));

		_updating = false;
	}

	/// <summary>
	/// Refreshes the add list so it only offers components the
	/// selected object does not already have.
	/// </summary>
	/// <remarks>
	/// A duplicate is rejected by the scene model, so offering one
	/// would only produce an error. When every registered component is
	/// present the list and the button go empty and disabled rather
	/// than leaving an action that cannot succeed.
	/// </remarks>
	private void RebuildAddableComponents(SceneEntity entity)
	{
		string? selected = _componentTypes.SelectedItem;

		_addableComponents.Clear();

		foreach (EditorComponentDescriptor descriptor in
			ComponentCatalog.Default.All)
		{
			if (!entity.HasComponent(descriptor.TypeId))
			{
				_addableComponents.Add(descriptor);
			}
		}

		int index = selected is null
			? 0
			: _addableComponents.FindIndex(descriptor =>
				string.Equals(
					descriptor.DisplayName,
					selected,
					StringComparison.Ordinal));

		_componentTypes.SetItems(
			_addableComponents.Select(
				descriptor => descriptor.DisplayName),
			index < 0 ? 0 : index);

		_componentTypes.IsEnabled = _addableComponents.Count > 0;
		_addButton.IsEnabled = _addableComponents.Count > 0;
	}

	/// <summary>
	/// Updates the line under the name: how many objects are selected,
	/// what a transform edit reaches, and what this one is made of.
	/// </summary>
	/// <remarks>
	/// The values in the grid are the active object's, while a transform
	/// or component edit applies to every selected object that carries
	/// the same thing, so the line says so rather than leaving the reach
	/// to guesswork.
	/// </remarks>
	private void UpdateSubtitle(SceneEntity entity)
	{
		int selectedCount = _session.Selection.Count;

		string selected = selectedCount > 1
			? $"{selectedCount} selected  ·  edits: every object with the " +
				"same component  ·  "
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
					// The row stands for the component across the selection,
					// so removing it follows the same rule the fields do.
					_session.RemoveComponentFromSelection(captured.TypeId);
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

				EditorPropertyKind.Color => new ColorProperty(
					property.DisplayName,
					() => property.GetValue(component) is Vector4 color
						? ToUiColor(color)
						: UiColor.White,
					value => SetProperty(
						entity,
						component,
						property,
						value.ToVector4())),

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
			// The selection shares the edit, the way it already shares
			// a transform edit; objects without the component are left
			// alone by the session.
			_session.SetSelectionComponentProperty(
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

	private static UiColor ToUiColor(Vector4 color)
	{
		return new UiColor(
			ToColorByte(color.X),
			ToColorByte(color.Y),
			ToColorByte(color.Z),
			ToColorByte(color.W));
	}

	private static byte ToColorByte(float value)
	{
		float clamped = float.IsFinite(value)
			? Math.Clamp(value, 0f, 1f)
			: 0f;

		return (byte)Math.Clamp(
			(int)MathF.Round(clamped * 255f),
			0,
			255);
	}

	/// <summary>
	/// Focuses and selects the active object's name for renaming.
	/// </summary>
	/// <returns>
	/// False when the inspector is hidden or no object is selected.
	/// </returns>
	internal bool FocusNameField()
	{
		if (!IsVisible || !_name.IsVisible || _session.ActiveEntity is null)
		{
			return false;
		}

		if (!_name.Focus())
		{
			return false;
		}

		_name.SelectAll();
		return true;
	}

	internal InspectorPanelState CaptureState()
	{
		return new InspectorPanelState
		{
			ScrollOffset = _grid.ScrollOffset.Y
		};
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		base.ArrangeContent(contentBounds);

		// A scroll view clamps a request against the content height it
		// has arranged, so a restored offset waits for this pass instead
		// of being clamped to the top of rows that are still empty.
		if (_restoreScrollOffset is float offset)
		{
			_restoreScrollOffset = null;
			_grid.SetScrollOffset(offset);
		}
	}

	private void OnNameCommitted(TextField field)
	{
		if (_updating)
		{
			return;
		}

		CommitPendingNameEdit();
	}

	/// <summary>
	/// Commits the name field's uncommitted text to the object it was
	/// typed on.
	/// </summary>
	/// <remarks>
	/// The field is rebuilt whenever the inspector moves to another
	/// object, and a component edit on the same object rebuilds it too.
	/// Committing first keeps a typed rename from being replaced by the
	/// new values, and it targets the bound object rather than whichever
	/// object the session made active afterwards.
	/// </remarks>
	private void CommitPendingNameEdit()
	{
		if (_updating ||
			_committingName ||
			_boundEntityId is not Guid id ||
			_session.Scene is not SceneDocument scene ||
			scene.Find(id) is not SceneEntity entity)
		{
			return;
		}

		string text = _name.Text;

		// The field only reports an uncommitted edit while it has focus,
		// and a panel being rebuilt for an interface scale or a preference
		// change has already lost it. Comparing the text with the object's
		// name keeps that last edit from being dropped; a field showing the
		// object's own name has nothing to commit.
		if (string.Equals(entity.Name, text, StringComparison.Ordinal))
		{
			return;
		}

		_committingName = true;

		try
		{
			// Rename commands normalize names (currently by trimming
			// whitespace). The notification the command raises runs while
			// the flag above is set, so it cannot re-enter this method;
			// pull the committed value back explicitly instead.
			_session.Undo.Execute(new RenameEntityCommand(scene, id, text));
			_name.SetTextFromSource(entity.Name);
			_session.SetStatus("Renamed entity");
		}

		catch (Exception exception)
		{
			_name.SetTextFromSource(entity.Name);
			_session.Log(exception.Message, EditorLogLevel.Error);
		}

		finally
		{
			_committingName = false;
		}
	}

	private void AddSelectedComponent()
	{
		if (_session.ActiveEntity is not SceneEntity entity)
		{
			return;
		}

		int index = _componentTypes.SelectedIndex;

		if (index < 0 || index >= _addableComponents.Count)
		{
			return;
		}

		EditorComponentDescriptor descriptor = _addableComponents[index];

		try
		{
			_session.AddComponentToSelection(descriptor.TypeId);
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
