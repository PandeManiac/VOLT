using System.Numerics;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor;

/// <summary>
/// The editor's authored-state coordinator.
/// </summary>
/// <remarks>
/// This type has no platform or widget references, so project,
/// selection, undo, and Play Mode behavior can be tested headlessly.
/// The UI observes its events and never owns the underlying data.
/// </remarks>
public sealed class EditorSession : IDisposable
{
	private const int MaximumLogEntries = 500;

	private readonly List<EditorLogEntry> _log = [];
	private readonly HashSet<Guid> _selection = [];
	private PlaySession? _play;
	private readonly Func<long> _allocatedBytes;
	private long _lastAllocatedBytes;
	private bool _disposed;

	/// <summary>
	/// Creates a session.
	/// </summary>
	/// <param name="allocatedBytesProvider">
	/// Reads the process's cumulative managed allocation counter. A
	/// caller can supply its own monotonic counter so frame allocation
	/// accounting is deterministic in tests; the default reads the
	/// runtime's counter.
	/// </param>
	public EditorSession(Func<long>? allocatedBytesProvider = null)
	{
		_allocatedBytes = allocatedBytesProvider ??
			(() => GC.GetTotalAllocatedBytes(precise: false));

		_lastAllocatedBytes = _allocatedBytes();

		Undo.Changed += _ => OnHistoryChanged();
	}

	public event Action<EditorSession>? ProjectChanged;

	public event Action<EditorSession>? SceneChanged;

	public event Action<EditorSession>? SelectionChanged;

	public event Action<EditorSession>? PlayStateChanged;

	public event Action<EditorSession>? RuntimeUpdated;

	public event Action<EditorSession>? StatusChanged;

	public event Action<EditorSession>? LogChanged;

	/// <summary>
	/// Raised when a setting the whole editor shares changed: the
	/// active tool, the gizmo space, grid snapping, or the snap
	/// increment.
	/// </summary>
	/// <remarks>
	/// The toolbar and a viewport's floating chrome both edit these, so
	/// every surface listens instead of assuming it is the only one the
	/// user touches.
	/// </remarks>
	public event Action<EditorSession>? SettingsChanged;

	public EditorProject? Project { get; private set; }

	public SceneDocument? Scene { get; private set; }

	public string? ScenePath { get; private set; }

	public UndoStack Undo { get; } = new();

	/// <summary>
	/// Gets the rolling record of editor frame intervals the profiler
	/// reads.
	/// </summary>
	public FrameTimeline Frames { get; } = new();

	/// <summary>
	/// Gets the rolling record of managed bytes allocated between
	/// editor frames.
	/// </summary>
	public MemoryTimeline Allocations { get; } = new();

	/// <summary>
	/// Records one editor frame interval.
	/// </summary>
	/// <param name="deltaSeconds">
	/// The real interval in seconds. Values that are not finite and
	/// positive are ignored.
	/// </param>
	public void RecordFrame(double deltaSeconds)
	{
		Frames.Record(deltaSeconds);

		long total = _allocatedBytes();
		long sampled = total - _lastAllocatedBytes;

		_lastAllocatedBytes = total;

		Allocations.Record(sampled > 0 ? sampled : 0);
	}

	public PlaySession? Play => _play;

	public RuntimeInputState Input { get; } = new();

	/// <summary>
	/// Gets or sets the guard consulted before an operation would
	/// discard a dirty scene.
	/// </summary>
	/// <remarks>
	/// The guard receives the operation as a continuation. It returns
	/// true when it ran the continuation immediately, and false when it
	/// deferred it, for example while the editor asks the user whether
	/// to save. A null guard lets the operation proceed unconditionally,
	/// which is the shape headless callers and tests get.
	/// </remarks>
	public Func<Action, bool>? UnsavedChangesGuard { get; set; }

	public IReadOnlyCollection<Guid> Selection => _selection;

	public Guid? ActiveEntityId { get; private set; }

	private EditorTool _tool = EditorTool.Translate;
	private bool _localTransform = true;
	private bool _gridSnapping;
	private float _snapStep = 0.25f;
	private int _targetFrameRate = EditorSettings.DefaultTargetFrameRate;

	public EditorTool Tool
	{
		get => _tool;
		set
		{
			if (_tool == value)
			{
				return;
			}

			_tool = value;
			SettingsChanged?.Invoke(this);
		}
	}

	public bool LocalTransform
	{
		get => _localTransform;
		set
		{
			if (_localTransform == value)
			{
				return;
			}

			_localTransform = value;
			SettingsChanged?.Invoke(this);
		}
	}

	public bool GridSnapping
	{
		get => _gridSnapping;
		set
		{
			if (_gridSnapping == value)
			{
				return;
			}

			_gridSnapping = value;
			SettingsChanged?.Invoke(this);
		}
	}

	/// <summary>
	/// Gets or sets whether a viewport currently owns the camera keys.
	/// </summary>
	/// <remarks>
	/// Tool shortcuts are suppressed while a viewport is navigating, so
	/// flying the scene camera with W A S D cannot also switch tools.
	/// The viewport sets this while its look button is held.
	/// </remarks>
	public bool ViewportNavigating { get; set; }

	public float SnapStep
	{
		get => _snapStep;
		set
		{
			float next = float.IsFinite(value)
				? MathF.Max(0.001f, value)
				: _snapStep;

			if (MathF.Abs(next - _snapStep) < 1e-6f)
			{
				return;
			}

			_snapStep = next;
			SettingsChanged?.Invoke(this);
		}
	}

	/// <summary>
	/// Gets or sets the frame rate the editor aims for, which is the
	/// budget the profiler graphs against.
	/// </summary>
	/// <remarks>
	/// A rate outside the supported range falls back to the default
	/// rather than being clamped silently: a hand-edited settings file
	/// that says zero should behave like one that says nothing.
	/// </remarks>
	public int TargetFrameRate
	{
		get => _targetFrameRate;
		set
		{
			int next = EditorSettings.IsValidTargetFrameRate(value)
				? value
				: EditorSettings.DefaultTargetFrameRate;

			if (next == _targetFrameRate)
			{
				return;
			}

			_targetFrameRate = next;
			SettingsChanged?.Invoke(this);
		}
	}

	/// <summary>
	/// Gets the frame budget in milliseconds that
	/// <see cref="TargetFrameRate"/> implies.
	/// </summary>
	public double TargetFrameMilliseconds => 1000.0 / _targetFrameRate;

	public string Status { get; private set; } = "Ready";

	public bool IsSceneDirty { get; private set; }

	public IReadOnlyList<EditorLogEntry> LogEntries => _log;

	public SceneEntity? ActiveEntity =>
		ActiveEntityId is Guid id && Scene is not null
			? Scene.Find(id)
			: null;

	public PlayState PlayState => _play?.State ?? PlayState.Stopped;

	public bool IsPlaying =>
		PlayState is PlayState.Playing or PlayState.Paused;

	public bool NewProject(string projectRoot, string name)
	{
		ThrowIfDisposed();

		return RunGuarded(() =>
		{
			CloseProjectCore();

			EditorProject project = EditorProject.Create(projectRoot, name);
			OpenProjectCore(project);
			Log($"Created project '{project.Name}'.");
		});
	}

	public bool OpenProject(string projectRoot)
	{
		ThrowIfDisposed();

		return RunGuarded(() =>
		{
			CloseProjectCore();

			EditorProject project = EditorProject.Open(projectRoot);
			OpenProjectCore(project);
			Log($"Opened project '{project.Name}'.");
		});
	}

	public bool CloseProject()
	{
		ThrowIfDisposed();
		return RunGuarded(CloseProjectCore);
	}

	private void CloseProjectCore()
	{
		Stop();

		if (Scene is not null)
		{
			Scene.Changed -= OnSceneDocumentChanged;
		}

		Scene = null;
		ScenePath = null;
		Project = null;
		IsSceneDirty = false;
		_selection.Clear();
		ActiveEntityId = null;
		Undo.Clear();

		ProjectChanged?.Invoke(this);
		SceneChanged?.Invoke(this);
		SelectionChanged?.Invoke(this);
		SetStatus("No project open");
	}

	public bool OpenScene(string relativePath)
	{
		ThrowIfDisposed();

		return RunGuarded(() =>
		{
			EditorProject project = RequireProject();
			Stop();

			SceneDocument scene = project.LoadScene(relativePath);
			SetScene(scene, relativePath, dirty: false);
			Log($"Opened scene '{scene.Name}'.");
		});
	}

	public SceneDocument? CreateScene(string relativePath, string name)
	{
		ThrowIfDisposed();

		SceneDocument? created = null;

		RunGuarded(() =>
		{
			EditorProject project = RequireProject();
			SceneDocument scene = project.CreateScene(relativePath, name);

			SetScene(scene, relativePath, dirty: false);
			Log($"Created scene '{scene.Name}'.");
			created = scene;
		});

		return created;
	}

	public void SaveScene()
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		SceneDocument scene = RequireScene();
		string path = ScenePath ??
			throw new InvalidOperationException(
				"No scene path is selected.");

		project.SaveScene(scene, path);
		IsSceneDirty = false;
		SceneChanged?.Invoke(this);
		SetStatus($"Saved {Path.GetFileName(path)}");
		Log($"Saved scene '{scene.Name}'.");
	}

	public void SaveSceneAs(string relativePath)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		SceneDocument scene = RequireScene();

		project.SaveScene(scene, relativePath);
		ScenePath = ProjectAssetCatalog.NormalizeId(relativePath);
		IsSceneDirty = false;
		SceneChanged?.Invoke(this);
		Log($"Saved scene '{scene.Name}'.");
	}

	public void SetStartupScene()
	{
		EditorProject project = RequireProject();

		if (ScenePath is null)
		{
			throw new InvalidOperationException(
				"No scene is open.");
		}

		project.StartupScenePath = ScenePath;
		project.SaveManifest();
		SetStatus("Startup scene updated");
		Log($"Startup scene set to '{ScenePath}'.");
	}

	public void Select(Guid? entityId, bool additive = false)
	{
		if (entityId is Guid id && Scene?.Find(id) is null)
		{
			entityId = null;
		}

		if (!additive)
		{
			_selection.Clear();
		}

		if (entityId is Guid selected)
		{
			if (additive && _selection.Contains(selected))
			{
				_selection.Remove(selected);
				ActiveEntityId = _selection.Count > 0
					? _selection.Last()
					: null;
			}

			else
			{
				_selection.Add(selected);
				ActiveEntityId = selected;
			}
		}

		else
		{
			ActiveEntityId = _selection.Count == 1
				? _selection.First()
				: null;
		}

		SelectionChanged?.Invoke(this);
	}

	public void SelectMany(
		IEnumerable<Guid> entities,
		Guid? activeEntityId = null)
	{
		ArgumentNullException.ThrowIfNull(entities);

		_selection.Clear();

		foreach (Guid id in entities)
		{
			if (Scene?.Find(id) is not null)
			{
				_selection.Add(id);
			}
		}

		ActiveEntityId = activeEntityId is Guid active &&
			_selection.Contains(active)
				? active
				: _selection.Count == 1
					? _selection.First()
					: null;

		SelectionChanged?.Invoke(this);
	}

	public void ClearSelection()
	{
		_selection.Clear();
		ActiveEntityId = null;
		SelectionChanged?.Invoke(this);
	}

	public SceneEntity? CreateEntity(
		string name,
		Guid? parentId = null)
	{
		SceneDocument scene = RequireScene();
		AddEntityCommand command = new(scene, name, parentId);

		Undo.Execute(command);
		Select(command.Entity.Id);
		SetStatus($"Created {command.Entity.Name}");

		return command.Entity;
	}

	/// <summary>
	/// Creates a directional light and selects it as one undo step.
	/// </summary>
	/// <remarks>
	/// The default rotation reproduces the key light earlier builds
	/// baked into the scene pipeline, so adding the first authored light
	/// does not change the scene's look until the user edits it.
	/// </remarks>
	public SceneEntity CreateDirectionalLight(
		string name = "Directional Light")
	{
		SceneDocument scene = RequireScene();
		AddEntityCommand command = new(scene, name);

		command.Entity.Transform.Position = new Vector3(0f, 3f, 0f);
		command.Entity.Transform.EulerAngles = LightData.DefaultEulerAngles;
		command.Entity.AddComponent(new LightData());

		Undo.Execute(command);
		Select(command.Entity.Id);
		SetStatus($"Created {command.Entity.Name}");

		return command.Entity;
	}

	public bool DeleteActiveEntity()
	{
		if (Scene is null || ActiveEntityId is not Guid id)
		{
			return false;
		}

		SceneEntity? entity = Scene.Find(id);

		if (entity is null)
		{
			return false;
		}

		DeleteEntityCommand command = new(Scene, id);
		Undo.Execute(command);
		ClearSelection();
		SetStatus($"Deleted {entity.Name}");

		return true;
	}

	/// <summary>
	/// Deletes every selected object as one undo step.
	/// </summary>
	/// <remarks>
	/// A selected object that sits inside another selected object is
	/// left to its parent: removing the parent already removes the
	/// subtree, so only the roots of the selection carry a command.
	/// </remarks>
	/// <returns>The number of delete commands executed.</returns>
	public int DeleteSelection()
	{
		if (Scene is not SceneDocument scene || _selection.Count == 0)
		{
			return 0;
		}

		List<SceneEntity> targets = [.. SceneSelection.Roots(scene, _selection)];

		if (targets.Count == 0)
		{
			return 0;
		}

		List<IEditorCommand> commands = [];

		foreach (SceneEntity entity in targets)
		{
			commands.Add(new DeleteEntityCommand(scene, entity.Id));
		}

		Undo.Execute(commands.Count == 1
			? commands[0]
			: new CompositeCommand(
				$"Delete {commands.Count} objects",
				commands));

		ClearSelection();

		SetStatus(commands.Count == 1
			? $"Deleted {targets[0].Name}"
			: $"Deleted {commands.Count} objects");

		return commands.Count;
	}

	public SceneEntity? DuplicateActiveEntity()
	{
		if (Scene is not SceneDocument scene || ActiveEntity is not SceneEntity active)
		{
			return null;
		}

		IReadOnlyList<SceneEntity> copies = DuplicateEntities(scene, [active]);

		if (copies.Count == 0)
		{
			return null;
		}

		Select(copies[0].Id);
		SetStatus($"Duplicated {copies[0].Name}");

		return copies[0];
	}

	/// <summary>
	/// Selects every object in the scene.
	/// </summary>
	/// <remarks>
	/// The active object stays active when it is still part of the
	/// scene, so a selection command does not move the inspector to an
	/// unrelated object.
	/// </remarks>
	/// <returns>The number of objects selected.</returns>
	public int SelectAll()
	{
		if (Scene is not SceneDocument scene)
		{
			return 0;
		}

		SelectMany(
			scene.Entities.Select(entity => entity.Id),
			ActiveEntityId);

		SetStatus($"Selected {_selection.Count} objects");

		return _selection.Count;
	}

	/// <summary>
	/// Clears the selection, as the Select None command does.
	/// </summary>
	public void SelectNone()
	{
		ClearSelection();
		SetStatus("Selection cleared");
	}

	/// <summary>
	/// Inverts the selection: every unselected object becomes selected
	/// and every selected one is dropped.
	/// </summary>
	/// <remarks>
	/// The objects keep scene order, so the hierarchy, the inspector,
	/// and any later group edit see the same list a Select All would.
	/// </remarks>
	/// <returns>The number of objects selected afterwards.</returns>
	public int InvertSelection()
	{
		if (Scene is not SceneDocument scene)
		{
			return 0;
		}

		List<Guid> inverted = [.. scene.Entities
			.Where(entity => !_selection.Contains(entity.Id))
			.Select(entity => entity.Id)];

		SelectMany(inverted, ActiveEntityId);

		SetStatus($"Selected {_selection.Count} objects");

		return _selection.Count;
	}

	/// <summary>
	/// Duplicates every selected object as one undo step.
	/// </summary>
	/// <remarks>
	/// A selected object that sits inside another selected object is
	/// left to its parent: duplicating the parent already copies the
	/// subtree. The copies become the selection, so a second duplicate
	/// or a follow-up transform acts on what the developer just made.
	/// </remarks>
	/// <returns>The copies, in the order their sources were selected.</returns>
	public IReadOnlyList<SceneEntity> DuplicateSelection()
	{
		if (Scene is not SceneDocument scene || _selection.Count == 0)
		{
			return [];
		}

		List<SceneEntity> roots = [.. SceneSelection.Roots(scene, _selection)];
		IReadOnlyList<SceneEntity> copies = DuplicateEntities(scene, roots);

		if (copies.Count == 0)
		{
			return copies;
		}

		// Copies come back in source order, so the copy of the active
		// object is the one at the active source's position.
		int activeIndex = roots.FindIndex(
			source => source.Id == ActiveEntityId);

		Guid? activeCopy = activeIndex >= 0
			? copies[activeIndex].Id
			: null;

		SelectMany(copies.Select(copy => copy.Id), activeCopy);

		SetStatus(copies.Count == 1
			? $"Duplicated {copies[0].Name}"
			: $"Duplicated {copies.Count} objects");

		return copies;
	}

	/// <summary>
	/// Gets the entity clipboard shared by every scene the editor
	/// opens, so objects can be copied from one scene and pasted into
	/// another.
	/// </summary>
	public EntityClipboard Clipboard { get; } = new();

	/// <summary>
	/// Copies the selected objects, and everything under them, to the
	/// clipboard.
	/// </summary>
	/// <returns>The number of copied objects, including children.</returns>
	public int CopySelection()
	{
		if (Scene is not SceneDocument scene || _selection.Count == 0)
		{
			return 0;
		}

		int copied = Clipboard.Capture(scene, _selection);

		SetStatus(copied == 1
			? "Copied 1 object"
			: $"Copied {copied} objects");

		return copied;
	}

	/// <summary>
	/// Copies the selected objects and deletes them.
	/// </summary>
	/// <remarks>
	/// The copy is taken before the delete, so an undo of the cut
	/// restores the objects while the clipboard still holds them.
	/// </remarks>
	/// <returns>The number of copied objects, including children.</returns>
	public int CutSelection()
	{
		if (Scene is not SceneDocument scene || _selection.Count == 0)
		{
			return 0;
		}

		int cut = Clipboard.Capture(scene, _selection);
		DeleteSelection();

		if (cut > 0)
		{
			SetStatus(cut == 1
				? "Cut 1 object"
				: $"Cut {cut} objects");
		}

		return cut;
	}

	/// <summary>
	/// Pastes the clipboard into the open scene as one undo step.
	/// </summary>
	/// <returns>The pasted objects, in the clipboard's order.</returns>
	public IReadOnlyList<SceneEntity> PasteClipboard()
	{
		SceneDocument scene = RequireScene();

		if (Clipboard.IsEmpty)
		{
			return [];
		}

		PasteEntitiesCommand command = new(scene, Clipboard.CreateEntities());

		Undo.Execute(command);

		Guid? active = command.RootIds.Count > 0
			? command.RootIds[0]
			: null;

		SelectMany(command.RootIds, active);

		SetStatus(command.Roots.Count == 1
			? $"Pasted {command.Roots[0].Name}"
			: $"Pasted {command.Roots.Count} objects");

		return command.Roots;
	}

	private IReadOnlyList<SceneEntity> DuplicateEntities(
		SceneDocument scene,
		IReadOnlyList<SceneEntity> sources)
	{
		List<DuplicateEntityCommand> commands = [];

		foreach (SceneEntity source in sources)
		{
			commands.Add(new DuplicateEntityCommand(scene, source.Id));
		}

		if (commands.Count == 0)
		{
			return [];
		}

		Undo.Execute(commands.Count == 1
			? commands[0]
			: new CompositeCommand(
				$"Duplicate {commands.Count} objects",
				commands));

		return [.. commands.Select(command => command.Copy!)];
	}

	public void RenameActiveEntity(string name)
	{
		SceneDocument scene = RequireScene();

		if (ActiveEntityId is not Guid id)
		{
			throw new InvalidOperationException(
				"No entity is selected.");
		}

		Undo.Execute(new RenameEntityCommand(scene, id, name));
		SetStatus("Renamed entity");
	}

	public void ReparentActiveEntity(Guid? parentId)
	{
		SceneDocument scene = RequireScene();

		if (ActiveEntityId is not Guid id)
		{
			throw new InvalidOperationException(
				"No entity is selected.");
		}

		Undo.Execute(new ReparentEntityCommand(scene, id, parentId));
		SetStatus("Reparented entity");
	}

	/// <summary>
	/// Re-parents several objects as one undo step.
	/// </summary>
	/// <remarks>
	/// An object that would create a cycle, or that already has the
	/// requested parent, is skipped rather than aborting the whole
	/// move: dragging a mixed selection onto one of its own members
	/// still moves the rest.
	/// </remarks>
	/// <param name="entityIds">The objects to move.</param>
	/// <param name="parentId">The new parent, or null for the root.</param>
	/// <returns>The number of objects re-parented.</returns>
	public int ReparentEntities(
		IEnumerable<Guid> entityIds,
		Guid? parentId)
	{
		ArgumentNullException.ThrowIfNull(entityIds);

		SceneDocument scene = RequireScene();
		List<IEditorCommand> commands = [];

		foreach (Guid id in entityIds)
		{
			if (scene.Find(id) is not SceneEntity entity ||
				entity.ParentId == parentId ||
				!scene.CanReparent(id, parentId))
			{
				continue;
			}

			commands.Add(new ReparentEntityCommand(scene, id, parentId));
		}

		if (commands.Count == 0)
		{
			return 0;
		}

		Undo.Execute(commands.Count == 1
			? commands[0]
			: new CompositeCommand(
				$"Reparent {commands.Count} objects",
				commands));

		SetStatus(commands.Count == 1
			? "Reparented object"
			: $"Reparented {commands.Count} objects");

		return commands.Count;
	}

	public void SetTransform(Guid entityId, TransformData transform)
	{
		SceneDocument scene = RequireScene();
		Undo.Execute(new SetTransformCommand(scene, entityId, transform));
	}

	/// <summary>
	/// Commits a viewport gesture after its live preview transformed
	/// the entity. The original transform becomes the undo state.
	/// </summary>
	public void CommitTransform(Guid entityId, TransformData before)
	{
		ArgumentNullException.ThrowIfNull(before);

		SceneDocument scene = RequireScene();

		if (scene.Find(entityId) is null)
		{
			throw new InvalidOperationException(
				"The transformed entity no longer exists.");
		}

		CommitTransforms(new Dictionary<Guid, TransformData>
		{
			[entityId] = before
		});
	}

	/// <summary>
	/// Commits a viewport gesture that previewed several objects at
	/// once. The whole gesture is a single undo step.
	/// </summary>
	/// <param name="before">
	/// Every transformed object's transform when the gesture began.
	/// </param>
	public void CommitTransforms(
		IReadOnlyDictionary<Guid, TransformData> before)
	{
		ArgumentNullException.ThrowIfNull(before);

		SceneDocument scene = RequireScene();
		List<IEditorCommand> commands = [];

		foreach (KeyValuePair<Guid, TransformData> entry in before)
		{
			ArgumentNullException.ThrowIfNull(entry.Value);

			if (scene.Find(entry.Key) is not SceneEntity entity ||
				entity.Transform.Equals(entry.Value))
			{
				continue;
			}

			commands.Add(new SetTransformCommand(
				scene,
				entry.Key,
				entity.Transform,
				entry.Value));
		}

		if (commands.Count == 0)
		{
			return;
		}

		Undo.Execute(commands.Count == 1
			? commands[0]
			: new CompositeCommand(
				$"Transform {commands.Count} objects",
				commands));
	}

	/// <summary>
	/// Applies one object's new transform to the whole selection.
	/// </summary>
	/// <remarks>
	/// The edit is replayed as a delta, so the other objects keep their
	/// own placement instead of collapsing onto the edited one. An
	/// object nested inside another selected object is left out: its
	/// parent already carries it, and transforming both would move it
	/// twice.
	/// </remarks>
	/// <param name="sourceId">The object the developer edited.</param>
	/// <param name="transform">Its new authored transform.</param>
	/// <returns>The number of objects the edit changed.</returns>
	public int SetSelectionTransform(Guid sourceId, TransformData transform)
	{
		ArgumentNullException.ThrowIfNull(transform);

		SceneDocument scene = RequireScene();
		SceneEntity source = scene.Find(sourceId) ??
			throw new InvalidOperationException(
				"The edited entity no longer exists.");

		TransformDelta delta = TransformDelta.Between(
			source.Transform,
			transform);

		List<IEditorCommand> commands =
		[
			new SetTransformCommand(scene, sourceId, transform)
		];

		foreach (SceneEntity other in SceneSelection.Roots(scene, _selection))
		{
			if (other.Id == sourceId)
			{
				continue;
			}

			TransformData next = other.Transform.Clone();
			delta.ApplyTo(next);

			if (!next.Equals(other.Transform))
			{
				commands.Add(new SetTransformCommand(scene, other.Id, next));
			}
		}

		if (commands.Count == 1)
		{
			Undo.Execute(commands[0]);
			return 1;
		}

		Undo.Execute(new CompositeCommand(
			$"Transform {commands.Count} objects",
			commands));

		return commands.Count;
	}

	public void SetComponentProperty(
		Guid entityId,
		AuthoredComponent component,
		EditorPropertyDescriptor property,
		object? value)
	{
		SceneDocument scene = RequireScene();

		Undo.Execute(new ComponentPropertyCommand(
			scene,
			entityId,
			component,
			property,
			value));
	}

	public void AddComponent(Guid entityId, string typeId)
	{
		SceneDocument scene = RequireScene();
		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.Get(typeId);

		Undo.Execute(new AddComponentCommand(
			scene,
			entityId,
			descriptor.Create()));

		SetStatus($"Added {descriptor.DisplayName}");
	}

	public void RemoveComponent(
		Guid entityId,
		AuthoredComponent component)
	{
		SceneDocument scene = RequireScene();
		Undo.Execute(new RemoveComponentCommand(
			scene,
			entityId,
			component));
		SetStatus($"Removed {component.DisplayName}");
	}

	public void UndoLast()
	{
		if (!Undo.Undo())
		{
			return;
		}

		ValidateSelection();
		SetStatus("Undo");
	}

	public void RedoLast()
	{
		if (!Undo.Redo())
		{
			return;
		}

		ValidateSelection();
		SetStatus("Redo");
	}

	public void StartPlay()
	{
		ThrowIfDisposed();

		SceneDocument scene = RequireScene();
		Stop();

		_play = new PlaySession(
			scene,
			Input,
			assetExists: Project is EditorProject project
				? project.AssetCatalog.FileExists
				: null);

		_play.StateChanged += OnPlayStateChanged;

		try
		{
			RuntimeScene runtime = _play.Start();
			Log("Entered Play Mode.");

			foreach (string diagnostic in runtime.Diagnostics)
			{
				Log(diagnostic, EditorLogLevel.Warning);
			}
		}

		catch
		{
			_play.Dispose();
			_play = null;
			throw;
		}

		PlayStateChanged?.Invoke(this);
		SetStatus("Playing");
	}

	public void Pause()
	{
		_play?.Pause();
		SetStatus(PlayState == PlayState.Paused ? "Paused" : Status);
	}

	public void Resume()
	{
		_play?.Resume();
		SetStatus(PlayState == PlayState.Playing ? "Playing" : Status);
	}

	public void Step()
	{
		if (_play?.Step() == true)
		{
			RuntimeUpdated?.Invoke(this);
			SetStatus("Stepped one fixed tick");
		}
	}

	public void Stop()
	{
		if (_play is null)
		{
			return;
		}

		_play.StateChanged -= OnPlayStateChanged;
		_play.Dispose();
		_play = null;
		Input.Clear();
		PlayStateChanged?.Invoke(this);
		RuntimeUpdated?.Invoke(this);
		SetStatus("Stopped");
		Log("Exited Play Mode; authored scene is unchanged.");
	}

	public void Update(double realDeltaSeconds)
	{
		if (_play is null || _play.State != PlayState.Playing)
		{
			return;
		}

		int ticks = _play.Update((float)realDeltaSeconds);

		if (ticks > 0)
		{
			RuntimeUpdated?.Invoke(this);
		}
	}

	public void ClearLog()
	{
		_log.Clear();
		LogChanged?.Invoke(this);
	}

	public void Log(
		string message,
		EditorLogLevel level = EditorLogLevel.Info)
	{
		ArgumentNullException.ThrowIfNull(message);

		_log.Add(new EditorLogEntry(DateTime.Now, level, message));

		if (_log.Count > MaximumLogEntries)
		{
			_log.RemoveRange(0, _log.Count - MaximumLogEntries);
		}

		LogChanged?.Invoke(this);
	}

	public void SetStatus(string message)
	{
		Status = message ?? string.Empty;
		StatusChanged?.Invoke(this);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Stop();

		if (Scene is not null)
		{
			Scene.Changed -= OnSceneDocumentChanged;
		}

		_disposed = true;
	}

	private bool RunGuarded(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		if (!IsSceneDirty ||
			Scene is null ||
			UnsavedChangesGuard is not Func<Action, bool> guard)
		{
			action();
			return true;
		}

		return guard(action);
	}

	private void OpenProjectCore(EditorProject project)
	{
		Project = project;
		ProjectChanged?.Invoke(this);

		SceneDocument startup = project.LoadStartupScene();
		SetScene(
			startup,
			project.StartupScenePath,
			dirty: false);

		Log($"Startup scene '{startup.Name}' loaded.");
	}

	private void SetScene(
		SceneDocument scene,
		string? path,
		bool dirty)
	{
		if (Scene is not null)
		{
			Scene.Changed -= OnSceneDocumentChanged;
		}

		Scene = scene;
		ScenePath = path is null
			? null
			: ProjectAssetCatalog.NormalizeId(path);
		IsSceneDirty = dirty;
		_selection.Clear();
		ActiveEntityId = null;
		Undo.Clear();

		Scene.Changed += OnSceneDocumentChanged;

		SceneChanged?.Invoke(this);
		SelectionChanged?.Invoke(this);
		SetStatus(scene.Name);
	}

	private void OnSceneDocumentChanged(
		SceneDocument scene,
		SceneChange change)
	{
		if (change.Kind != SceneChangeKind.Reloaded)
		{
			IsSceneDirty = true;
		}

		SceneChanged?.Invoke(this);

		if (change.EntityId is Guid id &&
			scene.Find(id) is null)
		{
			_selection.Remove(id);

			if (ActiveEntityId == id)
			{
				ActiveEntityId = _selection.Count == 1
					? _selection.First()
					: null;
			}

			SelectionChanged?.Invoke(this);
		}
	}

	private void OnPlayStateChanged(PlaySession session)
	{
		PlayStateChanged?.Invoke(this);

		if (session.State == PlayState.Stopped)
		{
			SetStatus("Stopped");
		}
	}

	private void OnHistoryChanged()
	{
		StatusChanged?.Invoke(this);
	}

	private void ValidateSelection()
	{
		if (Scene is null)
		{
			return;
		}

		_selection.RemoveWhere(id => Scene.Find(id) is null);

		if (ActiveEntityId is Guid active &&
			Scene.Find(active) is null)
		{
			ActiveEntityId = _selection.Count == 1
				? _selection.First()
				: null;
		}

		SelectionChanged?.Invoke(this);
	}

	private EditorProject RequireProject()
	{
		return Project ?? throw new InvalidOperationException(
			"No project is open.");
	}

	private SceneDocument RequireScene()
	{
		return Scene ?? throw new InvalidOperationException(
			"No scene is open.");
	}

	private void ThrowIfDespacedProject(out EditorProject project)
	{
		ThrowIfDisposed();
		project = RequireProject();
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
