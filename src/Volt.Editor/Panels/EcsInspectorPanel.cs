using System.Globalization;
using System.Numerics;
using Volt.Ecs.Component;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

/// <summary>
/// Inspects the live ECS world Play Mode built: its entities, their
/// archetype, the values actually stored in their component columns,
/// and the archetypes and queries behind them.
/// </summary>
/// <remarks>
/// <para>
/// This panel deliberately reads the runtime world instead of the
/// authored scene. The Inspector edits serialized component data; this
/// panel reports what the simulation is holding right now, which is the
/// only way to tell a saved value from a simulated one.
/// </para>
/// <para>
/// Snapshots are captured on a timer rather than on every runtime
/// update. Play Mode raises an update at the fixed-step rate, and
/// rebuilding labels at 60 Hz would spend more time formatting a table
/// than anyone can read. Nothing here retains a reference into chunk
/// storage: <see cref="RuntimeWorldSnapshot"/> copies every value it
/// displays, so stopping Play Mode cannot leave a dangling view.
/// </para>
/// </remarks>
internal sealed class EcsInspectorPanel : Panel, IFrameTick
{
	private const double RefreshIntervalSeconds = 0.25;

	private readonly EditorSession _session;
	private readonly Label _world;
	private readonly Label _state;
	private readonly Label _entityCount;
	private readonly Label _archetypeCount;
	private readonly Label _entityName;
	private readonly Label _entityHandle;
	private readonly Label _entityArchetype;
	private readonly Button _applyTransform;
	private readonly StackPanel _componentRows;
	private readonly StackPanel _archetypeRows;
	private readonly StackPanel _queryFilters;
	private readonly Label _queryResult;
	private readonly Button _runQuery;
	private readonly EmptyStateView _empty;
	private readonly StackPanel _content;
	private readonly List<(ComponentType Type, CheckBox Box)> _filters = [];
	private readonly List<Vector3Field> _editors = [];
	private readonly List<Button> _applyButtons = [];
	private readonly Label _editStatus;

	private double _elapsed;
	private bool _disposed;

	public EcsInspectorPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(10f);

		StackPanel doc = new()
		{
			Spacing = 5f
		};

		doc.Add(new Label("Runtime ECS")
		{
			Font = Theme.Fonts.Heading
		});

		_world = MutedLabel();
		_state = MutedLabel();
		_entityCount = MutedLabel();
		_archetypeCount = MutedLabel();

		doc.Add(_world);
		doc.Add(_state);
		doc.Add(_entityCount);
		doc.Add(_archetypeCount);
		doc.Add(new Separator());

		doc.Add(new Label("Selected runtime entity")
		{
			Font = Theme.Fonts.PanelHeader,
			TextColor = Theme.Palette.TextMuted
		});

		_entityName = new Label("Nothing selected")
		{
			Font = Theme.Fonts.Large
		};

		_entityHandle = MutedLabel();
		_entityArchetype = MutedLabel();

		_applyTransform = new Button("Apply Transform to Scene")
		{
			HorizontalAlignment = UiAlignment.Start,
			MinHeight = 24f
		};

		_applyTransform.Clicked += _ => CopyTransformToScene();

		doc.Add(_entityName);
		doc.Add(_entityHandle);
		doc.Add(_entityArchetype);
		doc.Add(_applyTransform);

		doc.Add(new Label(
			"Position, scale, and existing motion values are editable. An " +
			"edit writes to the live ECS world only, and stopping Play " +
			"discards it. Apply to Scene copies one value, and Apply " +
			"Transform copies position and scale together, into the saved " +
			"transform as one undo step. Running systems may overwrite a " +
			"value on the next tick.")
		{
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		_componentRows = new StackPanel
		{
			Spacing = 2f
		};

		_editStatus = new Label(string.Empty)
		{
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextMuted,
			Wrap = UiTextWrap.Wrap
		};

		doc.Add(_componentRows);
		doc.Add(_editStatus);
		doc.Add(new Separator());

		doc.Add(new Label("Archetypes")
		{
			Font = Theme.Fonts.PanelHeader,
			TextColor = Theme.Palette.TextMuted
		});

		_archetypeRows = new StackPanel
		{
			Spacing = 1f
		};

		doc.Add(_archetypeRows);
		doc.Add(new Separator());

		doc.Add(new Label("Query")
		{
			Font = Theme.Fonts.PanelHeader,
			TextColor = Theme.Palette.TextMuted
		});

		doc.Add(new Label(
			"Runs a real World.Query over the runtime archetypes with " +
			"every checked component required.")
		{
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		_queryFilters = new StackPanel
		{
			Spacing = 1f
		};

		foreach (ComponentRegistry.ComponentInfo info in
			ComponentRegistry.GetRegisteredComponents())
		{
			CheckBox box = new(ShortName(info.ClrType))
			{
				Font = Theme.Fonts.Small
			};

			_filters.Add((info.Type, box));
			_queryFilters.Add(box);
		}

		if (_filters.Count == 0)
		{
			_queryFilters.Add(new Label("No component types are registered.")
			{
				Font = Theme.Fonts.Small,
				TextColor = Theme.Palette.TextDisabled
			});
		}

		_runQuery = new Button("Run Query")
		{
			HorizontalAlignment = UiAlignment.Start,
			MinHeight = 24f
		};

		_runQuery.Clicked += _ => RunQuery();

		_queryResult = new Label(string.Empty)
		{
			Font = Theme.Fonts.Small,
			Wrap = UiTextWrap.Wrap
		};

		doc.Add(_queryFilters);
		doc.Add(_runQuery);
		doc.Add(_queryResult);

		ScrollView scroll = new()
		{
			FlexGrow = 1f,
			MinHeight = 100f,
			Content = doc
		};

		_content = new StackPanel();
		_content.Add(scroll);

		_empty = new EmptyStateView(
			UiIcon.Inspector,
			"No runtime world",
			"Press Play to build an ECS world from the scene, then inspect its entities, components, and archetypes here.");

		Add(_content);
		Add(_empty);

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.PlayStateChanged += OnSessionChanged;
		_session.RuntimeUpdated += OnSessionChanged;

		Refresh();
	}

	public void Tick(double deltaSeconds, float dpiScale)
	{
		if (_disposed || !IsVisible || !double.IsFinite(deltaSeconds))
		{
			return;
		}

		_elapsed += deltaSeconds;

		if (_elapsed < RefreshIntervalSeconds)
		{
			return;
		}

		_elapsed = 0.0;
		Refresh();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
		_session.PlayStateChanged -= OnSessionChanged;
		_session.RuntimeUpdated -= OnSessionChanged;
		base.Dispose();
	}

	private UiTheme Theme => Context?.Theme ?? UiTheme.CreateDark();

	private Label MutedLabel()
	{
		return new Label(string.Empty)
		{
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextMuted,
			Wrap = UiTextWrap.Wrap
		};
	}

	private void OnSessionChanged(EditorSession session)
	{
		Refresh();
	}

	private void Refresh()
	{
		RuntimeScene? runtime = _session.Play?.Runtime;
		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			_session.PlayState,
			_session.ActiveEntityId);

		_content.IsVisible = runtime is not null;
		_empty.IsVisible = runtime is null;
		_runQuery.IsEnabled = runtime is not null;
		_applyTransform.IsEnabled = runtime is not null;

		if (runtime is null)
		{
			DisposeEditors();
			_componentRows.ClearChildren();
			_queryResult.Text = string.Empty;
			_editStatus.Text = string.Empty;
			return;
		}

		_world.Text = $"World: {snapshot.WorldName}";
		_state.Text = $"State: {snapshot.State}";
		_entityCount.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Entities: {0:N0}",
			snapshot.EntityCount);

		_archetypeCount.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Archetypes: {0:N0}",
			snapshot.Archetypes.Count);

		RefreshSelectedEntity(snapshot.SelectedEntity);
		RefreshArchetypes(snapshot.Archetypes);
	}

	private void RefreshSelectedEntity(RuntimeEntitySnapshot? entity)
	{
		if (_editors.Any(editor => editor.IsEditing))
		{
			// Rebuilding the rows would destroy the field the developer
			// is typing into, so the panel holds its refresh until the
			// edit commits or focus moves on.
			return;
		}

		DisposeEditors();
		_componentRows.ClearChildren();

		if (entity is null)
		{
			_entityName.Text = "Nothing selected";
			_entityHandle.Text =
				"Select an authored object to find its runtime entity.";
			_entityArchetype.Text = string.Empty;
			return;
		}

		_entityName.Text = entity.Name;
		_entityHandle.Text = $"ECS handle: {entity.Handle}";
		_entityArchetype.Text = $"Archetype: {entity.Archetype}";

		if (entity.Components.Count == 0)
		{
			_componentRows.Add(new Label("No components.")
			{
				Font = Theme.Fonts.Small,
				TextColor = Theme.Palette.TextDisabled
			});

			return;
		}

		foreach (RuntimeComponentSnapshot component in entity.Components)
		{
			_componentRows.Add(new Label(component.DisplayName)
			{
				Font = Theme.Fonts.PanelHeader,
				TextColor = Theme.Palette.Accent
			});

			if (component.Note is string note)
			{
				_componentRows.Add(new Label(note)
				{
					Font = Theme.Fonts.Small,
					TextColor = Theme.Palette.TextDisabled,
					Wrap = UiTextWrap.Wrap
				});
			}

			if (component.Editable is RuntimeEditChannel channel &&
				component.Value is Vector3 vector)
			{
				Vector3Field editor = new()
				{
					Format = "0.###",
					Step = 0.1f
				};

				editor.SetValue(vector, notify: false);

				Guid authoredId = entity.AuthoredId;

				editor.ValueChanged += field =>
					ApplyEdit(authoredId, channel, field.Value);

				_editors.Add(editor);

				if (channel is RuntimeEditChannel.Position or
					RuntimeEditChannel.Scale)
				{
					Button apply = new("Apply to Scene")
					{
						Font = Theme.Fonts.Small,
						MinHeight = 20f
					};

					apply.Clicked += _ => CopyToScene(authoredId, channel);
					_applyButtons.Add(apply);

					editor.FlexGrow = 1f;
					editor.FlexShrink = 1f;

					StackPanel row = new(UiOrientation.Horizontal)
					{
						Spacing = 4f
					};

					row.Add(editor);
					row.Add(apply);
					_componentRows.Add(row);
				}

				else
				{
					_componentRows.Add(editor);
				}

				continue;
			}

			foreach (RuntimeFieldSnapshot field in component.Fields)
			{
				_componentRows.Add(new Label($"{field.Label}: {field.Value}")
				{
					Font = Theme.Fonts.MonoSmall,
					TextColor = Theme.Palette.Text
				});
			}
		}
	}

	private void ApplyEdit(
		Guid authoredId,
		RuntimeEditChannel channel,
		Vector3 value)
	{
		if (_session.Play is not PlaySession play)
		{
			return;
		}

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			play,
			authoredId,
			channel,
			value);

		_editStatus.Text = result.Message;
		_editStatus.TextColor = result.Applied
			? Theme.Palette.TextMuted
			: Theme.Palette.Warning;
	}

	private void CopyToScene(Guid authoredId, RuntimeEditChannel channel)
	{
		if (_session.Play is not PlaySession play ||
			_session.Scene is not SceneDocument scene)
		{
			_editStatus.Text =
				"Start Play Mode to copy a runtime value into the scene.";

			_editStatus.TextColor = Theme.Palette.Warning;
			return;
		}

		RuntimeEditResult result = RuntimeWorldEditor.ApplyToScene(
			play,
			scene,
			_session.Undo,
			authoredId,
			channel);

		ReportEditResult(result);
	}

	private void CopyTransformToScene()
	{
		if (_session.ActiveEntityId is not Guid authoredId)
		{
			_editStatus.Text =
				"Select an object to copy its runtime transform.";

			_editStatus.TextColor = Theme.Palette.Warning;
			return;
		}

		if (_session.Play is not PlaySession play ||
			_session.Scene is not SceneDocument scene)
		{
			_editStatus.Text =
				"Start Play Mode to copy a runtime value into the scene.";

			_editStatus.TextColor = Theme.Palette.Warning;
			return;
		}

		ReportEditResult(RuntimeWorldEditor.ApplyTransformToScene(
			play,
			scene,
			_session.Undo,
			authoredId));
	}

	private void ReportEditResult(RuntimeEditResult result)
	{
		_editStatus.Text = result.Message;
		_editStatus.TextColor = result.Applied
			? Theme.Palette.TextMuted
			: Theme.Palette.Warning;

		if (result.Applied)
		{
			// The copy changed authored data, not the runtime world, so
			// the rows and the Inspector both need a refresh.
			Refresh();
		}
	}

	private void DisposeEditors()
	{
		foreach (Vector3Field editor in _editors)
		{
			editor.Dispose();
		}

		foreach (Button apply in _applyButtons)
		{
			apply.Dispose();
		}

		_editors.Clear();
		_applyButtons.Clear();
	}

	private void RefreshArchetypes(
		IReadOnlyList<RuntimeArchetypeSnapshot> archetypes)
	{
		_archetypeRows.ClearChildren();

		if (archetypes.Count == 0)
		{
			_archetypeRows.Add(new Label("The world has no archetypes.")
			{
				Font = Theme.Fonts.Small,
				TextColor = Theme.Palette.TextDisabled
			});

			return;
		}

		foreach (RuntimeArchetypeSnapshot archetype in archetypes)
		{
			_archetypeRows.Add(new Label(archetype.Signature)
			{
				Font = Theme.Fonts.MonoSmall,
				TextColor = Theme.Palette.Text
			});

			_archetypeRows.Add(new Label(string.Format(
				CultureInfo.InvariantCulture,
				"    {0:N0} entities   {1:N0} chunks   {2:P0} occupancy   {3:N1} KiB",
				archetype.EntityCount,
				archetype.ChunkCount,
				archetype.Occupancy,
				archetype.AllocatedBytes / 1024.0))
			{
				Font = Theme.Fonts.Small,
				TextColor = Theme.Palette.TextMuted
			});
		}
	}

	private void RunQuery()
	{
		RuntimeScene? runtime = _session.Play?.Runtime;

		if (runtime is null)
		{
			_queryResult.Text = "No runtime world is running.";
			return;
		}

		List<ComponentType> required = [];

		foreach ((ComponentType type, CheckBox box) in _filters)
		{
			if (box.IsChecked)
			{
				required.Add(type);
			}
		}

		if (required.Count == 0)
		{
			_queryResult.Text =
				"Check at least one component to require it.";

			return;
		}

		try
		{
			RuntimeQueryResult result =
				RuntimeWorldSnapshot.RunQuery(runtime, required);

			_queryResult.Text = string.Format(
				CultureInfo.InvariantCulture,
				"Matched {0:N0} entities in {1:N0} archetypes: {2}",
				result.EntityCount,
				result.ArchetypeCount,
				result.Signatures.Count == 0
					? "none"
					: string.Join("  |  ", result.Signatures));
		}

		catch (Exception exception)
		{
			// A structural change between the click and the query must
			// report itself rather than tear down the panel.
			_queryResult.Text = $"Query failed: {exception.Message}";
		}
	}

	private static string ShortName(Type type)
	{
		string name = type.Name;
		const string Suffix = "Component";

		return name.EndsWith(Suffix, StringComparison.Ordinal) &&
			name.Length > Suffix.Length
				? name[..^Suffix.Length]
				: name;
	}
}
