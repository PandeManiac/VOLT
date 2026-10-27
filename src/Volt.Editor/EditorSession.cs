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
	private bool _disposed;

	public EditorSession()
	{
		Undo.Changed += _ => OnHistoryChanged();
	}

	public event Action<EditorSession>? ProjectChanged;

	public event Action<EditorSession>? SceneChanged;

	public event Action<EditorSession>? SelectionChanged;

	public event Action<EditorSession>? PlayStateChanged;

	public event Action<EditorSession>? RuntimeUpdated;

	public event Action<EditorSession>? StatusChanged;

	public event Action<EditorSession>? LogChanged;

	public EditorProject? Project { get; private set; }

	public SceneDocument? Scene { get; private set; }

	public string? ScenePath { get; private set; }

	public UndoStack Undo { get; } = new();

	public PlaySession? Play => _play;

	public RuntimeInputState Input { get; } = new();

	public IReadOnlyCollection<Guid> Selection => _selection;

	public Guid? ActiveEntityId { get; private set; }

	public EditorTool Tool { get; set; } = EditorTool.Translate;

	public bool LocalTransform { get; set; } = true;

	public bool GridSnapping { get; set; }

	public float SnapStep { get; set; } = 0.25f;

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

	public void NewProject(string projectRoot, string name)
	{
		ThrowIfDisposed();

		CloseProject();

		EditorProject project = EditorProject.Create(projectRoot, name);
		OpenProjectCore(project);
		Log($"Created project '{project.Name}'.");
	}

	public void OpenProject(string projectRoot)
	{
		ThrowIfDisposed();

		CloseProject();

		EditorProject project = EditorProject.Open(projectRoot);
		OpenProjectCore(project);
		Log($"Opened project '{project.Name}'.");
	}

	public void CloseProject()
	{
		ThrowIfDisposed();
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

	public void OpenScene(string relativePath)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		Stop();

		SceneDocument scene = project.LoadScene(relativePath);
		SetScene(scene, relativePath, dirty: false);
		Log($"Opened scene '{scene.Name}'.");
	}

	public SceneDocument CreateScene(string relativePath, string name)
	{
		ThrowIfDespacedProject(out EditorProject project);

		SceneDocument scene = project.CreateScene(relativePath, name);
		SetScene(scene, relativePath, dirty: false);
		Log($"Created scene '{scene.Name}'.");
		return scene;
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

	public SceneEntity? DuplicateActiveEntity()
	{
		if (Scene is null || ActiveEntityId is not Guid id)
		{
			return null;
		}

		SceneEntity copy = Scene.DuplicateEntity(id);
		Select(copy.Id);
		SetStatus($"Duplicated {copy.Name}");

		return copy;
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
		SceneDocument scene = RequireScene();
		SceneEntity entity = scene.Find(entityId) ??
			throw new InvalidOperationException(
				"The transformed entity no longer exists.");

		Undo.Execute(new SetTransformCommand(
			scene,
			entityId,
			entity.Transform,
			before));
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
