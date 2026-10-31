using System.Globalization;
using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;
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
	private const int QueryPageSize = 8;

	// Shown under the result rows so the keys the rows answer are
	// discoverable without reading the guide.
	private const string QueryKeyHints =
		"Rows: Up and Down move, Page Up and Page Down change the page, " +
		"Home and End jump to the ends, and Ctrl+R resets the view.";

	private readonly EditorSession _session;
	private readonly Label _world;
	private readonly Label _state;
	private readonly Label _entityCount;
	private readonly Label _archetypeCount;
	private readonly Label _entityName;
	private readonly Label _entityHandle;
	private readonly Label _entityArchetype;
	private readonly Button _applyTransform;
	private readonly Label _divergence;
	private readonly StackPanel _componentRows;
	private readonly StackPanel _archetypeRows;
	private readonly StackPanel _queryFilters;
	private readonly Label _queryHints;
	private readonly Label _queryResult;
	private readonly Button _runQuery;
	private readonly TextField _queryFilter;
	private readonly Button _sortScene;
	private readonly Button _sortName;
	private readonly Button _sortArchetype;
	private readonly StackPanel _queryRows;
	private readonly StackPanel _queryPager;
	private readonly Label _queryPageLabel;
	private readonly Button _previousPage;
	private readonly Button _nextPage;
	private readonly Button _copyResults;
	private readonly Button _resetView;
	private readonly EmptyStateView _empty;
	private readonly StackPanel _content;
	private readonly List<(ComponentType Type, CheckBox Box)> _filters = [];
	private readonly List<RuntimeQueryEntity> _queryMatches = [];
	private readonly List<RuntimeQueryEntity> _queryView = [];
	private EcsQuerySort _querySort;
	private int _queryPage;
	private bool _queryIntent;
	private bool _wasRunning;
	private readonly List<Vector3Field> _editors = [];
	private readonly List<Button> _applyButtons = [];
	private readonly Label _editStatus;

	// The text the selected entity and archetype rows were built from, so
	// an unchanged world does not tear the rows down on every tick.
	private string _selectedKey = string.Empty;
	private string _archetypesKey = string.Empty;
	private string _queryKey = string.Empty;

	// The values the world and count labels were last built from, so a
	// tick that finds them unchanged does not format them again.
	private string _worldName = string.Empty;
	private PlayState? _shownState;
	private int _shownEntityCount = -1;
	private int _shownArchetypeCount = -1;

	// Set when the authored scene changes: a rename changes a name a query
	// lists without changing the world's structure.
	private bool _sceneDirty;

	private double _elapsed;
	private bool _disposed;

	public EcsInspectorPanel(EditorSession session)
		: this(session, null)
	{
	}

	internal EcsInspectorPanel(
		EditorSession session,
		EcsInspectorPanelState? state)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		// The sort is read while the toggle buttons are built, so it has
		// to be restored before they exist to get their highlight right.
		_querySort = state?.Sort ?? EcsQuerySort.SceneOrder;

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

		_divergence = MutedLabel();

		doc.Add(_entityName);
		doc.Add(_entityHandle);
		doc.Add(_entityArchetype);
		doc.Add(_applyTransform);
		doc.Add(_divergence);

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
			"every checked component required, then lists the matching " +
			"entities a page at a time. Click a row to select its authored " +
			"object. The list can be filtered by name and sorted by scene " +
			"order, name, or archetype.")
		{
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap
		});

		_queryFilters = new StackPanel
		{
			Spacing = 1f
		};

		HashSet<ComponentType> remembered = state is null
			? []
			: [.. state.RequiredComponents];

		foreach (ComponentRegistry.ComponentInfo info in
			ComponentRegistry.GetRegisteredComponents())
		{
			CheckBox box = new(ShortName(info.ClrType))
			{
				Font = Theme.Fonts.Small,
				IsChecked = remembered.Contains(info.Type)
			};

			_filters.Add((info.Type, box));
			_queryFilters.Add(box);
		}

		// Remembered checks mean the query is expected back rather than
		// waiting for another click: the panel is rebuilt by an
		// interface-scale change, and the runtime panel is also built
		// before Play Mode has a world to query.
		_queryIntent = remembered.Count > 0;

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

		_queryFilter = new TextField
		{
			Name = "QueryFilter",
			Placeholder = "Filter by name",
			Font = Theme.Fonts.Small,
			MinHeight = 20f
		};

		_queryFilter.TextChanged += _ => ApplyQueryView(resetPage: true);

		if (state is not null && state.Filter.Length > 0)
		{
			_queryFilter.Text = state.Filter;
		}

		_sortScene = CreateSortButton("Scene order", EcsQuerySort.SceneOrder);
		_sortName = CreateSortButton("Name", EcsQuerySort.Name);
		_sortArchetype = CreateSortButton("Archetype", EcsQuerySort.Archetype);

		_resetView = new Button("Reset")
		{
			Font = Theme.Fonts.Small,
			MinHeight = 20f,
			TooltipText = "Clear the filter, the sort, and the checked " +
				"components (Ctrl+R)"
		};

		_resetView.Clicked += _ => ResetQueryView();

		StackPanel sortRow = new(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		sortRow.Add(_sortScene);
		sortRow.Add(_sortName);
		sortRow.Add(_sortArchetype);
		sortRow.Add(_resetView);

		_previousPage = new Button("Prev")
		{
			Font = Theme.Fonts.Small,
			MinHeight = 20f
		};

		_previousPage.Clicked += _ => ChangeQueryPage(-1);

		_nextPage = new Button("Next")
		{
			Font = Theme.Fonts.Small,
			MinHeight = 20f
		};

		_nextPage.Clicked += _ => ChangeQueryPage(1);

		_queryPageLabel = MutedLabel();

		_copyResults = new Button("Copy Results")
		{
			Font = Theme.Fonts.Small,
			MinHeight = 20f,
			TooltipText = "Copy the filtered results to the clipboard"
		};

		_copyResults.Clicked += _ => CopyResults();

		_queryPager = new StackPanel(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		_queryPager.Add(_previousPage);
		_queryPager.Add(_nextPage);
		_queryPager.Add(_queryPageLabel);
		_queryPager.Add(_copyResults);
		_queryPager.IsVisible = false;

		_queryRows = new StackPanel
		{
			Spacing = 1f
		};

		_queryHints = new Label(QueryKeyHints)
		{
			Name = "QueryKeyHints",
			Font = Theme.Fonts.Small,
			TextColor = Theme.Palette.TextDisabled,
			Wrap = UiTextWrap.Wrap,
			IsVisible = false
		};

		_queryResult = new Label(string.Empty)
		{
			Font = Theme.Fonts.Small,
			Wrap = UiTextWrap.Wrap
		};

		doc.Add(_queryFilters);
		doc.Add(_runQuery);
		doc.Add(_queryFilter);
		doc.Add(sortRow);
		doc.Add(_queryRows);
		doc.Add(_queryHints);
		doc.Add(_queryResult);

		ScrollView scroll = new()
		{
			FlexGrow = 1f,
			MinHeight = 100f,
			Content = doc
		};

		_content = new StackPanel();
		_content.Add(scroll);

		// The pager sits below the document rather than at the end of
		// it, so changing pages and copying the list stay in reach no
		// matter how far the panel has been scrolled.
		_content.Add(_queryPager);

		_empty = new EmptyStateView(
			UiIcon.Inspector,
			"No runtime world",
			"Press Play to build an ECS world from the scene, then inspect its entities, components, and archetypes here.");

		Add(_content);
		Add(_empty);

		_session.SceneChanged += OnSceneChanged;
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
		_session.SceneChanged -= OnSceneChanged;
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

	private void OnSceneChanged(EditorSession session)
	{
		_sceneDirty = true;
		Refresh();
	}

	private void OnSessionChanged(EditorSession session)
	{
		Refresh();
	}

	private void Refresh()
	{
		RuntimeScene? runtime = _session.Play?.Runtime;

		// A world appearing is the moment a remembered query becomes
		// answerable again, so the transition is what triggers it.
		bool started = runtime is not null && !_wasRunning;
		_wasRunning = runtime is not null;

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
			ClearQuery();
			_editStatus.Text = string.Empty;
			_divergence.Text = string.Empty;
			_selectedKey = string.Empty;
			_archetypesKey = string.Empty;
			return;
		}

		if (_worldName != snapshot.WorldName)
		{
			_worldName = snapshot.WorldName;
			_world.Text = $"World: {snapshot.WorldName}";
		}

		if (_shownState != snapshot.State)
		{
			_shownState = snapshot.State;
			_state.Text = $"State: {snapshot.State}";
		}

		if (_shownEntityCount != snapshot.EntityCount)
		{
			_shownEntityCount = snapshot.EntityCount;
			_entityCount.Text = string.Format(
				CultureInfo.InvariantCulture,
				"Entities: {0:N0}",
				snapshot.EntityCount);
		}

		if (_shownArchetypeCount != snapshot.Archetypes.Count)
		{
			_shownArchetypeCount = snapshot.Archetypes.Count;
			_archetypeCount.Text = string.Format(
				CultureInfo.InvariantCulture,
				"Archetypes: {0:N0}",
				snapshot.Archetypes.Count);
		}

		RuntimeEntitySnapshot? entity = snapshot.SelectedEntity;

		RuntimeTransformComparison? comparison = entity is not null &&
			_session.Scene is SceneDocument scene
				? RuntimeWorldSnapshot.CompareTransform(entity, scene)
				: null;

		string selectedKey = SelectedKey(entity) + '\u0003' +
			ComparisonKey(comparison);

		// The keys are built from the text the rows display, so a tick
		// that finds the same world leaves them alone instead of
		// rebuilding them: a rebuild costs a frame of layout and would
		// take focus from a field the developer just reached.
		if (selectedKey != _selectedKey &&
			RefreshSelectedEntity(entity, comparison))
		{
			_selectedKey = selectedKey;
		}

		string archetypesKey = ArchetypesKey(snapshot.Archetypes);
		bool structureChanged = archetypesKey != _archetypesKey;

		if (structureChanged)
		{
			_archetypesKey = archetypesKey;
			RefreshArchetypes(snapshot.Archetypes);
		}

		// An entity appearing or leaving, or an authored edit such as a
		// rename, can change the rows a running query already lists, so the
		// query is re-run then rather than on every tick.
		bool resync = started || structureChanged || _sceneDirty;
		_sceneDirty = false;

		if (_queryIntent && resync)
		{
			RunQuery();
		}
	}

	private bool RefreshSelectedEntity(
		RuntimeEntitySnapshot? entity,
		RuntimeTransformComparison? comparison)
	{
		if (_editors.Any(editor => editor.IsEditing))
		{
			// Rebuilding the rows would destroy the field the developer
			// is typing into, so the panel holds its refresh until the
			// edit commits or focus moves on.
			return false;
		}

		DisposeEditors();
		_componentRows.ClearChildren();

		if (entity is null)
		{
			_entityName.Text = "Nothing selected";
			_entityHandle.Text =
				"Select an authored object to find its runtime entity.";
			_entityArchetype.Text = string.Empty;
			_divergence.Text = string.Empty;
			return true;
		}

		_entityName.Text = DisplayName(entity);
		_entityHandle.Text = $"ECS handle: {entity.Handle}";
		_entityArchetype.Text = $"Archetype: {entity.Archetype}";

		UpdateDivergence(comparison);

		if (entity.Components.Count == 0)
		{
			_componentRows.Add(new Label("No components.")
			{
				Font = Theme.Fonts.Small,
				TextColor = Theme.Palette.TextDisabled
			});

			return true;
		}

		foreach (RuntimeComponentSnapshot component in entity.Components)
		{
			bool editable = component.Editable is RuntimeEditChannel &&
				component.Value is Vector3;

			if (!editable && component.Fields.Count == 0 &&
				component.Note is string compact)
			{
				// Nothing here can be edited or read as a value, so the
				// component is one fact rather than a section: its name and
				// the reason it has no editor share a single line instead of
				// reserving a heading and a note of their own.
				_componentRows.Add(new Label(
					$"{component.DisplayName} - {compact}")
				{
					Font = Theme.Fonts.Small,
					TextColor = Theme.Palette.TextDisabled,
					Wrap = UiTextWrap.Wrap
				});

				continue;
			}

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

				if (comparison?.For(channel) is RuntimeChannelComparison entry)
				{
					_componentRows.Add(new Label(entry.Diverged
						? $"Authored: {FormatVector(entry.AuthoredValue)} - " +
							"differs from the runtime value. Apply to copy it back."
						: $"Authored: {FormatVector(entry.AuthoredValue)} - " +
							"matches the runtime value.")
					{
						Font = Theme.Fonts.Small,
						TextColor = entry.Diverged
							? Theme.Palette.Warning
							: Theme.Palette.TextDisabled,
						Wrap = UiTextWrap.Wrap
					});
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

		return true;
	}

	/// <summary>
	/// The name to show for a runtime entity: the authored one while the
	/// scene still has it, because a rename during Play leaves the name the
	/// runtime world was built with behind.
	/// </summary>
	private string DisplayName(RuntimeEntitySnapshot entity)
	{
		return _session.Scene?.Find(entity.AuthoredId)?.Name ?? entity.Name;
	}

	/// <summary>
	/// Builds the text a selected entity's rows display, so the panel can
	/// tell a real change from another tick of the same world.
	/// </summary>
	private string SelectedKey(RuntimeEntitySnapshot? entity)
	{
		return entity is null
			? string.Empty
			: string.Join(
				'\u0001',
				DisplayName(entity),
				entity.Handle.ToString(),
				entity.Archetype,
				string.Join('\u0002', entity.Components.Select(ComponentKey)));
	}

	private static string ComponentKey(RuntimeComponentSnapshot component)
	{
		return string.Join(
			'\u0001',
			component.DisplayName,
			component.Note ?? string.Empty,
			component.Editable?.ToString() ?? string.Empty,
			component.Value is Vector3 value ? FormatVector(value) : string.Empty,
			string.Join(
				'\u0002',
				component.Fields.Select(field => $"{field.Label}={field.Value}")));
	}

	private static string ComparisonKey(RuntimeTransformComparison? comparison)
	{
		return comparison is null
			? string.Empty
			: string.Join(
				'\u0002',
				comparison.Channels.Select(channel =>
					$"{channel.Channel}\u0001" +
					$"{FormatVector(channel.AuthoredValue)}\u0001" +
					$"{channel.Diverged}"));
	}

	private static string ArchetypesKey(
		IReadOnlyList<RuntimeArchetypeSnapshot> archetypes)
	{
		return string.Join(
			'\u0002',
			archetypes.Select(archetype => string.Join(
				'\u0001',
				archetype.Signature,
				archetype.EntityCount.ToString(
					"N0",
					CultureInfo.InvariantCulture),
				archetype.ChunkCount.ToString(
					"N0",
					CultureInfo.InvariantCulture),
				archetype.Occupancy.ToString(
					"P0",
					CultureInfo.InvariantCulture),
				(archetype.AllocatedBytes / 1024.0).ToString(
					"N1",
					CultureInfo.InvariantCulture))));
	}

	private void UpdateDivergence(RuntimeTransformComparison? comparison)
	{
		if (comparison is null || comparison.Channels.Count == 0)
		{
			_divergence.Text = string.Empty;
			return;
		}

		if (!comparison.AnyDiverged)
		{
			_divergence.Text =
				"Position and scale match the authored scene.";

			_divergence.TextColor = Theme.Palette.TextDisabled;
			return;
		}

		string labels = string.Join(
			", ",
			comparison.Channels
				.Where(channel => channel.Diverged)
				.Select(channel => channel.Label));

		_divergence.Text =
			$"Differs from the authored scene: {labels}. " +
			"Apply a value back to keep it.";

		_divergence.TextColor = Theme.Palette.Warning;
	}

	private static string FormatVector(Vector3 value)
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"({0:0.###}, {1:0.###}, {2:0.###})",
			value.X,
			value.Y,
			value.Z);
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

	internal EcsInspectorPanelState CaptureState()
	{
		return new EcsInspectorPanelState
		{
			Filter = _queryFilter.Text,
			Sort = _querySort,
			RequiredComponents = [
				.. _filters
					.Where(entry => entry.Box.IsChecked)
					.Select(entry => entry.Type)
			]
		};
	}

	private void CopyResults()
	{
		if (_queryView.Count == 0)
		{
			_session.SetStatus("Run a query before copying its results");
			return;
		}

		if (Context?.Clipboard is not IUiClipboard clipboard)
		{
			_session.SetStatus("The clipboard is not available");
			return;
		}

		// The filter and sort define the list, so copying it whole is more
		// useful than copying whichever page happens to be on screen.
		clipboard.SetText(string.Join(
			Environment.NewLine,
			_queryView.Select(FormatRow)));

		_session.SetStatus(_queryView.Count == 1
			? "Copied 1 query result"
			: $"Copied {_queryView.Count} query results");
	}

	private static string FormatRow(RuntimeQueryEntity match)
	{
		string name = match.Name.Length > 0
			? match.Name
			: "(runtime only)";

		return string.Format(
			CultureInfo.InvariantCulture,
			"{0}   {1}   {2}",
			name,
			HandleText(match.Handle),
			match.Archetype);
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
			ClearQuery();
			_queryIntent = false;
			_queryResult.Text =
				"Check at least one component to require it.";

			return;
		}

		try
		{
			RuntimeQueryResult result = RuntimeWorldSnapshot.RunQuery(
				runtime,
				required,
				id => _session.Scene?.Find(id)?.Name);

			_queryMatches.Clear();
			_queryMatches.AddRange(result.Entities);
			ApplyQueryView(resetPage: true);
			_queryIntent = true;

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
			ClearQuery();
			_queryResult.Text = $"Query failed: {exception.Message}";
		}
	}

	private void ChangeQueryPage(int delta)
	{
		if (_queryView.Count == 0)
		{
			return;
		}

		int pages = (_queryView.Count + QueryPageSize - 1) / QueryPageSize;
		int next = Math.Clamp(_queryPage + delta, 0, pages - 1);

		if (next == _queryPage)
		{
			return;
		}

		_queryPage = next;
		RenderQueryPage();
	}

	private void ApplyQueryView(bool resetPage)
	{
		IEnumerable<RuntimeQueryEntity> view = _queryMatches;
		string filter = _queryFilter.Text.Trim();

		if (filter.Length > 0)
		{
			view = view.Where(match => match.Name.Contains(
				filter,
				StringComparison.OrdinalIgnoreCase));
		}

		view = _querySort switch
		{
			EcsQuerySort.Name => view
				.OrderBy(match => match.Name.Length == 0 ? 1 : 0)
				.ThenBy(
					match => match.Name,
					StringComparer.OrdinalIgnoreCase)
				.ThenBy(match => match.Handle.Index),
			EcsQuerySort.Archetype => view
				.OrderBy(match => match.Archetype, StringComparer.Ordinal)
				.ThenBy(
					match => match.Name,
					StringComparer.OrdinalIgnoreCase)
				.ThenBy(match => match.Handle.Index),
			_ => view
		};

		_queryView.Clear();
		_queryView.AddRange(view);

		if (resetPage)
		{
			_queryPage = 0;
		}

		// The rows are rebuilt only when the list they show changes, so a
		// repeated apply with the same filter, sort, and matches leaves the
		// existing rows in place.
		string key = QueryViewKey();

		if (key != _queryKey)
		{
			_queryKey = key;
			RenderQueryPage();
		}
	}

	private string QueryViewKey()
	{
		return string.Join(
			'\u0001',
			_queryFilter.Text.Trim(),
			_querySort.ToString(),
			_queryPage.ToString(CultureInfo.InvariantCulture),
			string.Join('\u0002', _queryView.Select(FormatRow)));
	}

	private void SetQuerySort(EcsQuerySort sort)
	{
		if (_querySort == sort)
		{
			return;
		}

		_querySort = sort;
		_sortScene.IsSelected = sort == EcsQuerySort.SceneOrder;
		_sortName.IsSelected = sort == EcsQuerySort.Name;
		_sortArchetype.IsSelected = sort == EcsQuerySort.Archetype;
		ApplyQueryView(resetPage: true);
	}

	private void RenderQueryPage()
	{
		_queryRows.ClearChildren();

		if (_queryView.Count == 0)
		{
			_queryPager.IsVisible = false;
			_queryHints.IsVisible = false;
			_queryPageLabel.Text = string.Empty;

			if (_queryMatches.Count > 0)
			{
				_queryRows.Add(new Label("No entities match the filter.")
				{
					Name = "QueryEmpty",
					Font = Theme.Fonts.Small,
					TextColor = Theme.Palette.TextDisabled,
					Wrap = UiTextWrap.Wrap
				});
			}

			return;
		}

		int pages = (_queryView.Count + QueryPageSize - 1) / QueryPageSize;
		_queryPage = Math.Clamp(_queryPage, 0, pages - 1);

		int start = _queryPage * QueryPageSize;
		int end = Math.Min(_queryView.Count, start + QueryPageSize);

		for (int index = start; index < end; index++)
		{
			RuntimeQueryEntity match = _queryView[index];
			Guid? authoredId = match.AuthoredId;

			_queryRows.Add(new QueryResultRow(
				FormatRow(match),
				authoredId is Guid id ? () => _session.Select(id) : null,
				this)
			{
				Name = "QueryResultRow",
				Font = Theme.Fonts.MonoSmall,
				TextColor = authoredId is null
					? Theme.Palette.TextDisabled
					: Theme.Palette.Text,
				Wrap = UiTextWrap.Wrap
			});
		}

		_previousPage.IsEnabled = _queryPage > 0;
		_nextPage.IsEnabled = _queryPage + 1 < pages;

		string count = _queryView.Count == _queryMatches.Count
			? string.Format(
				CultureInfo.InvariantCulture,
				"{0:N0} entities",
				_queryView.Count)
			: string.Format(
				CultureInfo.InvariantCulture,
				"{0:N0} of {1:N0} entities",
				_queryView.Count,
				_queryMatches.Count);

		_queryPageLabel.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Page {0:N0} of {1:N0}  ({2})",
			_queryPage + 1,
			pages,
			count);

		_queryPager.IsVisible = true;
		_queryHints.IsVisible = true;
	}

	/// <summary>
	/// Carries an arrow key past the edge of the page onto the next one.
	/// </summary>
	/// <returns>
	/// True when a row on the adjacent page took focus and selection.
	/// </returns>
	private bool MoveSelectionToPage(int direction)
	{
		if (direction == 0)
		{
			return false;
		}

		int pages = (_queryView.Count + QueryPageSize - 1) / QueryPageSize;
		int target = _queryPage + direction;

		if (target < 0 || target >= pages)
		{
			return false;
		}

		int start = target * QueryPageSize;
		int end = Math.Min(_queryView.Count, start + QueryPageSize);

		// Down enters the next page at its first selectable row and Up
		// enters the previous page at its last, which is where the
		// arrow would have landed if the pages were one list.
		for (int index = direction > 0 ? start : end - 1;
			index >= start && index < end;
			index += direction)
		{
			if (_queryView[index].AuthoredId is not null)
			{
				return SelectRow(index);
			}
		}

		// A page holding only runtime entities cannot take the
		// selection, so the key does nothing instead of silently
		// flipping the page.
		return false;
	}

	private bool MoveSelectionToStart()
	{
		return MoveSelectionToEdge(fromEnd: false);
	}

	private bool MoveSelectionToEnd()
	{
		return MoveSelectionToEdge(fromEnd: true);
	}

	/// <summary>
	/// Moves focus and selection to the first or last selectable row of
	/// the whole result list, turning to the page it lives on.
	/// </summary>
	private bool MoveSelectionToEdge(bool fromEnd)
	{
		for (int index = fromEnd ? _queryView.Count - 1 : 0;
			index >= 0 && index < _queryView.Count;
			index += fromEnd ? -1 : 1)
		{
			if (_queryView[index].AuthoredId is not null)
			{
				return SelectRow(index);
			}
		}

		return false;
	}

	/// <summary>
	/// Focuses and selects one row of the whole result list, rendering
	/// the page it belongs to when that is not the page on screen.
	/// </summary>
	private bool SelectRow(int index)
	{
		if (index < 0 || index >= _queryView.Count)
		{
			return false;
		}

		int page = index / QueryPageSize;

		if (page != _queryPage || _queryRows.Children.Count == 0)
		{
			_queryPage = page;
			RenderQueryPage();
		}

		int row = index - (page * QueryPageSize);

		return _queryRows.Children.Count > row &&
			_queryRows.Children[row] is QueryResultRow target &&
			target.FocusAndSelect();
	}

	/// <summary>
	/// Returns the query section to its initial state: no filter, scene
	/// order, nothing required, and no results.
	/// </summary>
	private void ResetQueryView()
	{
		// The field raises TextChanged, which re-sorts the view, so it
		// is cleared before the components and the results go.
		_queryFilter.Text = string.Empty;

		foreach ((ComponentType _, CheckBox box) in _filters)
		{
			box.IsChecked = false;
		}

		SetQuerySort(EcsQuerySort.SceneOrder);
		ClearQuery();
		_queryIntent = false;
		_session.SetStatus("Query view reset");
	}

	private void ClearQuery()
	{
		_queryMatches.Clear();
		_queryView.Clear();
		_queryPage = 0;
		_queryRows.ClearChildren();
		_queryPager.IsVisible = false;
		_queryHints.IsVisible = false;
		_queryPageLabel.Text = string.Empty;
		_queryResult.Text = string.Empty;

		// The panel now shows an empty list, so that is the list a later
		// apply must differ from before it rebuilds the rows.
		_queryKey = QueryViewKey();
	}

	private Button CreateSortButton(string text, EcsQuerySort sort)
	{
		Button button = new(text)
		{
			Font = Theme.Fonts.Small,
			MinHeight = 20f,
			IsSelected = _querySort == sort
		};

		button.Clicked += _ => SetQuerySort(sort);

		return button;
	}

	private static string HandleText(Entity handle)
	{
		return string.Format(
			CultureInfo.InvariantCulture,
			"Entity {0}:{1}",
			handle.Index,
			handle.Generation);
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

	/// <summary>
	/// A query result row that selects the authored object it names.
	/// </summary>
	/// <remarks>
	/// Runtime-only entities have no authored object, so their rows are
	/// inert and muted rather than opening an Inspector target that does
	/// not exist.
	/// </remarks>
	private sealed class QueryResultRow : Label
	{
		private readonly Action? _select;
		private readonly EcsInspectorPanel _panel;

		public QueryResultRow(
			string text,
			Action? select,
			EcsInspectorPanel panel)
			: base(text)
		{
			_select = select;
			_panel = panel;
			IsEnabled = select is not null;
			IsFocusable = select is not null;
			Cursor = select is null ? UiCursor.Arrow : UiCursor.Hand;
		}

		/// <summary>
		/// Focuses this row and selects the object it names.
		/// </summary>
		public bool FocusAndSelect()
		{
			if (_select is null || !IsEnabled || !Focus())
			{
				return false;
			}

			OnClick(1);
			return true;
		}

		protected override void OnClick(int clickCount)
		{
			base.OnClick(clickCount);
			_select?.Invoke();
		}

		protected override bool OnKeyDown(UiKeyEvent key)
		{
			if (key.Key == UiKey.R && key.HasModifier(UiKeyModifiers.Control))
			{
				// Reset clears the results, so this row is removed while
				// it is handling the key. Nothing here touches the row
				// after that.
				_panel.ResetQueryView();
				return true;
			}

			if (_select is null)
			{
				return base.OnKeyDown(key);
			}

			if (key.Key is UiKey.Enter or UiKey.Space)
			{
				OnClick(1);
				return true;
			}

			if (key.Key is UiKey.Up or UiKey.Down)
			{
				return MoveToNeighbour(key.Key == UiKey.Down ? 1 : -1);
			}

			if (key.Key is UiKey.PageUp or UiKey.PageDown)
			{
				// A page key jumps straight to the neighbouring page, so
				// it uses the same entry point the arrow uses at an edge.
				return _panel.MoveSelectionToPage(
					key.Key == UiKey.PageDown ? 1 : -1);
			}

			if (key.Key is UiKey.Home or UiKey.End)
			{
				// Home and End reach the ends of the whole result list,
				// turning as many pages as that takes.
				return key.Key == UiKey.Home
					? _panel.MoveSelectionToStart()
					: _panel.MoveSelectionToEnd();
			}

			return base.OnKeyDown(key);
		}

		private bool MoveToNeighbour(int direction)
		{
			if (Parent is not Widget parent || direction == 0)
			{
				return false;
			}

			int index = -1;

			for (int candidate = 0; candidate < parent.Children.Count; candidate++)
			{
				if (ReferenceEquals(parent.Children[candidate], this))
				{
					index = candidate;
					break;
				}
			}

			if (index < 0)
			{
				return false;
			}

			// Runtime-only rows are not focusable, so they are skipped
			// rather than becoming a dead stop on the way down.
			for (int candidate = index + direction;
				candidate >= 0 && candidate < parent.Children.Count;
				candidate += direction)
			{
				if (parent.Children[candidate] is QueryResultRow row &&
					row.FocusAndSelect())
				{
					return true;
				}
			}

			// The page ended under the arrow, so the panel is asked for
			// the neighbouring page.
			return _panel.MoveSelectionToPage(direction);
		}
	}
}
