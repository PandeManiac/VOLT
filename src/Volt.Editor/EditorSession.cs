using System.Numerics;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Validation;

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
	private readonly Func<string, bool> _openExternalFile;
	private long _lastAllocatedBytes;
	private GameplayAssembly? _gameplay;
	private int _runtimeDiagnosticCount;
	private int _runtimeLogCount;
	private int _reportedDroppedLogMessages;
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
	/// <param name="externalFileOpener">
	/// Opens a project file with the OS-registered application. A test
	/// can supply its own opener so no process is launched.
	/// </param>
	public EditorSession(
		Func<long>? allocatedBytesProvider = null,
		Func<string, bool>? externalFileOpener = null)
	{
		_allocatedBytes = allocatedBytesProvider ??
			(() => GC.GetTotalAllocatedBytes(precise: false));
		_openExternalFile = externalFileOpener ?? ExternalFileLauncher.Open;

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

	/// <summary>
	/// Raised when the asset selected in the Project Browser changes.
	/// </summary>
	/// <remarks>
	/// The Browser owns its multi-selection and keyboard state; this is
	/// only the single asset panels may preview, so a multi-selection
	/// reports nothing rather than guessing which one to show.
	/// </remarks>
	public event Action<EditorSession>? AssetSelectionChanged;

	/// <summary>
	/// Gets the asset the Project Browser has selected alone, or null.
	/// </summary>
	public string? SelectedAssetId { get; private set; }

	/// <summary>
	/// Records which single asset the Project Browser has selected.
	/// </summary>
	/// <param name="assetId">The project-relative asset, or null.</param>
	public void SelectAsset(string? assetId)
	{
		string? normalized = string.IsNullOrWhiteSpace(assetId)
			? null
			: assetId;

		if (string.Equals(
			SelectedAssetId,
			normalized,
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		SelectedAssetId = normalized;
		AssetSelectionChanged?.Invoke(this);
	}

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

	/// <summary>
	/// Gets the project's gameplay assembly while it is loaded.
	/// </summary>
	/// <remarks>
	/// The assembly is loaded by an explicit build or by starting Play,
	/// never by opening a folder. It stays loaded across Play sessions so
	/// stopping and starting again does not re-read the same code.
	/// </remarks>
	public GameplayAssembly? Gameplay => _gameplay;

	/// <summary>
	/// Gets the outcome of the most recent game-code build.
	/// </summary>
	public GameplayBuildResult? LastGameplayBuild { get; private set; }

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
	private EditorConsoleFilter _consoleFilter = EditorConsoleFilter.Default;

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
	/// <summary>
	/// Gets or sets which Console entries the panel shows.
	/// </summary>
	/// <remarks>
	/// The session owns it so the panel keeps its filters across the
	/// interface rebuild a scale change causes, and so the preferences
	/// coordinator can persist it.
	/// </remarks>
	public EditorConsoleFilter ConsoleFilter
	{
		get => _consoleFilter;
		set
		{
			if (_consoleFilter == value)
			{
				return;
			}

			_consoleFilter = value;
			SettingsChanged?.Invoke(this);
		}
	}

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

			// A project that was just written cannot have broken
			// references yet, so the project-wide check would only add
			// noise about game code that has not been built.
			OpenProjectCore(project, validate: false);
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
		UnloadGameplay();

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

	/// <summary>
	/// Opens a project file in the application the operating system
	/// associates with its extension.
	/// </summary>
	/// <remarks>
	/// The editor deliberately ships no code editor. Handing the path to
	/// the OS keeps the user's own tooling, and its settings, in charge of
	/// source files.
	/// </remarks>
	public bool OpenProjectFile(string relativePath)
	{
		ThrowIfDisposed();

		bool opened = false;

		RunGuarded(() =>
		{
			EditorProject project = RequireProject();
			ProjectAssetCatalog catalog = project.AssetCatalog;

			if (!catalog.TryResolvePath(relativePath, out string fullPath))
			{
				// A file appears as soon as it is written, which is often
				// before the browser's next refresh.
				catalog.Refresh();

				if (!catalog.TryResolvePath(relativePath, out fullPath))
				{
					Log(
						$"'{relativePath}' is not part of this project.",
						EditorLogLevel.Warning);
					SetStatus(
						$"{Path.GetFileName(relativePath)} was not found");
					return;
				}
			}

			if (!IsInsideProject(project.RootPath, fullPath))
			{
				Log(
					$"'{relativePath}' resolves outside the project.",
					EditorLogLevel.Error);
				SetStatus("The file resolves outside the project");
				return;
			}

			opened = _openExternalFile(fullPath);

			if (opened)
			{
				SetStatus($"Opening {Path.GetFileName(fullPath)} in your editor");
				return;
			}

			Log(
				$"No application is registered to open " +
					$"{Path.GetFileName(fullPath)}.",
				EditorLogLevel.Warning);
			SetStatus("Could not open the file");
		});

		return opened;
	}

	private static bool IsInsideProject(string root, string fullPath)
	{
		string prefix = Path.GetFullPath(root)
			.TrimEnd(Path.DirectorySeparatorChar) +
			Path.DirectorySeparatorChar;

		return Path.GetFullPath(fullPath).StartsWith(
			prefix,
			StringComparison.Ordinal);
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

	/// <summary>
	/// Renames a project asset and keeps the open scene's save path in
	/// step when that asset is the open scene.
	/// </summary>
	public string RenameAsset(string assetId, string newName)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		bool openScene = ScenePath is string current &&
			string.Equals(current, assetId, StringComparison.OrdinalIgnoreCase);
		bool wasDirty = IsSceneDirty;

		string renamed = project.RenameAsset(assetId, newName);

		if (openScene)
		{
			ScenePath = renamed;
		}

		int changed = Scene?.ReplaceAssetReference(assetId, renamed) ?? 0;

		if (changed > 0 && !wasDirty && Scene is not null &&
			ScenePath is not null)
		{
			project.SaveScene(Scene, ScenePath);
			IsSceneDirty = false;
		}

		ProjectChanged?.Invoke(this);
		return renamed;
	}

	/// <summary>
	/// Renames a project folder and keeps the open scene's save path,
	/// in-memory references, and disk scene references in sync.
	/// </summary>
	public string RenameFolder(string folderId, string newName)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		string id = ProjectAssetCatalog.NormalizeId(folderId).TrimEnd('/');
		string oldPrefix = id + "/";
		string? sceneSuffix = ScenePath is string current &&
			current.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
				? current[oldPrefix.Length..]
				: null;
		ProjectAsset[] affected =
		[
			.. project.AssetCatalog.Assets.Where(asset =>
				asset.Id.StartsWith(
					oldPrefix,
					StringComparison.OrdinalIgnoreCase))
		];
		bool wasDirty = IsSceneDirty;

		string renamed = project.RenameFolder(id, newName);
		string newPrefix = renamed + "/";

		if (sceneSuffix is not null)
		{
			ScenePath = newPrefix + sceneSuffix;
		}

		int changed = 0;

		foreach (ProjectAsset asset in affected)
		{
			changed += Scene?.ReplaceAssetReference(
				asset.Id,
				newPrefix + asset.Id[oldPrefix.Length..]) ?? 0;
		}

		if (changed > 0 && !wasDirty && Scene is not null &&
			ScenePath is not null)
		{
			project.SaveScene(Scene, ScenePath);
			IsSceneDirty = false;
		}

		ProjectChanged?.Invoke(this);
		return renamed;
	}

	/// <summary>
	/// Deletes several project assets in one project change.
	/// </summary>
	/// <returns>The number of distinct assets that were removed.</returns>
	/// <remarks>
	/// The open scene is checked before anything is removed. The project
	/// then validates every path before deleting, so a protected or
	/// missing asset refuses the whole selection instead of leaving a
	/// partial deletion.
	/// </remarks>
	public int DeleteAssets(IEnumerable<string> assetIds)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(assetIds);

		EditorProject project = RequireProject();
		List<string> ids = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

		foreach (string assetId in assetIds)
		{
			string id = ProjectAssetCatalog.NormalizeId(assetId);

			if (!seen.Add(id))
			{
				continue;
			}

			if (ScenePath is string current &&
				string.Equals(current, id, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					"Close the scene before deleting its file.");
			}

			ids.Add(id);
		}

		if (ids.Count == 0)
		{
			return 0;
		}

		// A deleted image cannot be repointed the way a renamed one can, so
		// the materials that still name it are collected while it is still
		// there and reported afterwards by name. Clearing them silently
		// would turn a broken reference into a material that quietly lost
		// its texture.
		List<(string Image, string Material)> stale = [];

		foreach (string id in ids)
		{
			foreach (string material in project.FindTextureReferences(id))
			{
				stale.Add((id, material));
			}
		}

		bool wasDirty = IsSceneDirty;

		project.DeleteAssets(ids);

		int changed = 0;

		foreach (string id in ids)
		{
			changed += Scene?.ReplaceAssetReference(id, string.Empty) ?? 0;
		}

		if (changed > 0 && !wasDirty && Scene is not null &&
			ScenePath is not null)
		{
			project.SaveScene(Scene, ScenePath);
			IsSceneDirty = false;
		}

		ProjectChanged?.Invoke(this);

		foreach ((string image, string material) in stale)
		{
			Log(
				$"'{material}' still names the deleted image '{image}'.",
				EditorLogLevel.Warning);
		}

		if (stale.Count > 0)
		{
			Log(
				$"{stale.Count} material reference(s) now name a deleted " +
				"image.",
				EditorLogLevel.Warning);
		}

		return ids.Count;
	}

	/// <summary>
	/// Deletes a project asset, refusing to remove the scene that is
	/// currently open.
	/// </summary>
	public void DeleteAsset(string assetId)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		bool wasDirty = IsSceneDirty;

		if (ScenePath is string current &&
			string.Equals(current, assetId, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"Close the scene before deleting its file.");
		}

		project.DeleteAsset(assetId);

		int changed = Scene?.ReplaceAssetReference(
			assetId,
			string.Empty) ?? 0;

		if (changed > 0 && !wasDirty && Scene is not null &&
			ScenePath is not null)
		{
			project.SaveScene(Scene, ScenePath);
			IsSceneDirty = false;
		}

		ProjectChanged?.Invoke(this);
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

		// A caller may pass the current selection as the new one, for
		// example to change only the active object. Snapshot before
		// clearing, or that caller would supply an empty sequence.
		List<Guid> requested = [.. entities];

		_selection.Clear();

		foreach (Guid id in requested)
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

	/// <summary>
	/// Places a project mesh in the open scene as one undo step.
	/// </summary>
	/// <remarks>
	/// The new object carries the mesh and the default material, so
	/// importing a model and putting it in the scene does not also require
	/// adding a renderer and choosing the same file twice. A mesh that
	/// cannot be read is reported instead of placing an invisible object.
	/// </remarks>
	/// <param name="meshId">The project-relative mesh asset.</param>
	/// <returns>The created object, or null when nothing was placed.</returns>
	public SceneEntity? PlaceMeshAsset(string meshId)
	{
		ThrowIfDisposed();

		SceneDocument scene = RequireScene();
		EditorProject project = RequireProject();
		ProjectAsset? asset = project.AssetCatalog.Find(meshId);

		if (asset is null || asset.Kind != ProjectAssetKind.Mesh)
		{
			SetStatus("That asset is not a mesh");
			return null;
		}

		if (!project.TryLoadMesh(asset.Id, out _, out string? diagnostic))
		{
			Log(
				diagnostic ?? $"'{asset.FileName}' could not be loaded.",
				EditorLogLevel.Warning);
			SetStatus($"Could not place {asset.FileName}");
			return null;
		}

		AddEntityCommand command = new(
			scene,
			Path.GetFileNameWithoutExtension(asset.FileName));

		command.Entity.AddComponent(new MeshRendererData
		{
			Mesh = asset.Id,
			Material = MaterialLoader.DefaultId
		});

		Undo.Execute(command);
		Select(command.Entity.Id);
		SetStatus($"Placed {asset.FileName}");

		return command.Entity;
	}

	/// <summary>
	/// Assigns a project material to every selected object that renders a
	/// mesh.
	/// </summary>
	/// <remarks>
	/// Assigning to a selection is the action a material is usually
	/// chosen for; doing it one object at a time in the Inspector is the
	/// slow path. Objects without a mesh renderer are left alone rather
	/// than gaining one.
	/// </remarks>
	/// <param name="materialId">The project-relative material asset.</param>
	/// <returns>The number of objects the material was applied to.</returns>
	public int AssignMaterialToSelection(string materialId)
	{
		ThrowIfDisposed();

		SceneDocument scene = RequireScene();
		EditorProject project = RequireProject();

		if (!project.TryLoadMaterial(
			materialId,
			out MaterialAsset material,
			out string? diagnostic))
		{
			Log(
				diagnostic ?? $"'{materialId}' could not be loaded.",
				EditorLogLevel.Warning);
			SetStatus($"Could not apply {Path.GetFileName(materialId)}");
			return 0;
		}

		EditorPropertyDescriptor property = ComponentCatalog.Default
			.Get(MeshRendererData.ComponentTypeId)
			.Properties
			.First(candidate => candidate.Name == "Material");

		int assigned = 0;

		foreach (SceneEntity entity in
			SceneSelection.Roots(scene, _selection))
		{
			if (entity.GetComponent<MeshRendererData>() is not
				MeshRendererData renderer)
			{
				continue;
			}

			SetComponentProperty(
				entity.Id,
				renderer,
				property,
				material.Id);

			assigned++;
		}

		SetStatus(assigned == 0
			? "No selected object renders a mesh"
			: $"Applied {Path.GetFileName(material.Id)} to " +
				$"{assigned} object{(assigned == 1 ? string.Empty : "s")}");

		return assigned;
	}

	/// <summary>
	/// Creates a material that shows one texture.
	/// </summary>
	/// <param name="textureId">The project-relative image asset.</param>
	/// <returns>
	/// The new material's identifier, or <see langword="null"/> when the
	/// reference is not an image.
	/// </returns>
	/// <remarks>
	/// The action only creates the file. Assigning it stays a separate
	/// step, exactly as it is for a flat material, so creating one cannot
	/// change the scene by accident.
	/// </remarks>
	public string? CreateMaterialFromTexture(string textureId)
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		string? materialId = project.CreateMaterialFromTexture(textureId);

		if (materialId is null)
		{
			Log(
				$"'{textureId}' is not an image this project can use as a " +
				"texture.",
				EditorLogLevel.Warning);

			SetStatus($"Could not create a material from {Path.GetFileName(textureId)}");

			return null;
		}

		Log($"Created '{materialId}' from '{textureId}'.");
		SetStatus($"Created {Path.GetFileName(materialId)}");

		return materialId;
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

	/// <summary>
	/// Compiles the project's game code and loads what it produces.
	/// </summary>
	/// <remarks>
	/// This is the explicit build workflow; nothing is compiled because a
	/// folder was opened. Parsed compiler output goes to the Console with
	/// its file, line, and column, and a failed build keeps the previously
	/// loaded code so the last working build can still be played. An
	/// unchanged scene is reloaded afterwards, which is what turns
	/// previously preserved unknown components into the project's own
	/// editable types; unsaved edits are never discarded for it.
	/// </remarks>
	public GameplayBuildResult? BuildGameCode()
	{
		ThrowIfDisposed();

		EditorProject project = RequireProject();
		string? projectFile = project.ResolveGameplayProjectPath();

		if (projectFile is null)
		{
			Log(
				"This project has no game code. Create a Scripts project " +
				"first, then build it.",
				EditorLogLevel.Warning);

			SetStatus("No game code to build");
			return null;
		}

		SetStatus("Building game code…");

		// The engine this editor is running is already built, so a game
		// build must not recompile it.
		GameplayBuildResult result = GameplayProjectBuilder.Build(
			projectFile,
			buildReferencedProjects: false);

		LastGameplayBuild = result;

		foreach (GameplayBuildDiagnostic diagnostic in result.Diagnostics)
		{
			Log(
				diagnostic.Describe(),
				diagnostic.IsError
					? EditorLogLevel.Error
					: EditorLogLevel.Warning);
		}

		if (!result.Succeeded)
		{
			Log(
				result.FailureMessage ?? "The game code did not build.",
				EditorLogLevel.Error);

			Log(
				_gameplay is null
					? "Play Mode will run the built-in behaviours only."
					: $"Play Mode keeps using '{_gameplay.AssemblyName}' " +
						"from the last successful build.",
				EditorLogLevel.Warning);

			SetStatus("Game code build failed");
			return result;
		}

		SetStatus($"Built {Path.GetFileName(result.AssemblyPath)}");

		if (result.AssemblyPath is string assemblyPath)
		{
			LoadGameplay(assemblyPath);
		}

		return result;
	}

	public void StartPlay()
	{
		ThrowIfDisposed();

		SceneDocument scene = RequireScene();
		Stop();

		// Play runs the scene as authored, so a reference that cannot be
		// drawn would silently leave a hole in the game. Report it and
		// stay in authoring instead of pretending the scene is sound.
		if (Project is EditorProject openProject &&
			!ValidateSceneForPlay(openProject, scene))
		{
			return;
		}

		// Play uses what is already built. Compiling is the developer's
		// explicit action, not a side effect of pressing Play.
		TryLoadProjectGameplay();

		_play = new PlaySession(
			scene,
			Input,
			assetExists: Project is EditorProject project
				? project.AssetCatalog.FileExists
				: null,
			componentBindings: _gameplay?.Registration.Bindings,
			systems: _gameplay?.Registration.Systems);

		_play.StateChanged += OnPlayStateChanged;
		_runtimeLogCount = 0;
		_reportedDroppedLogMessages = 0;

		try
		{
			RuntimeScene runtime = _play.Start();
			Log("Entered Play Mode.");

			foreach (string diagnostic in runtime.Diagnostics)
			{
				Log(diagnostic, EditorLogLevel.Warning);
			}

			_runtimeDiagnosticCount = runtime.Diagnostics.Count;

			// Systems may write while starting, so drain what OnStart
			// already produced instead of waiting for the first tick.
			ReportRuntimeLog();

			if (_gameplay is not null)
			{
				Log(
					$"Running project game code '{_gameplay.AssemblyName}'.");
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

	private bool ValidateSceneForPlay(
		EditorProject project,
		SceneDocument scene)
	{
		ProjectValidationReport report = ProjectValidator.ValidateScene(
			project,
			ScenePath ?? project.StartupScenePath,
			scene);

		foreach (ProjectIssue issue in report.Issues)
		{
			Log(
				$"{issue.Location}: {issue.Message}",
				issue.Severity == ProjectIssueSeverity.Error
					? EditorLogLevel.Error
					: EditorLogLevel.Warning,
				EditorLogSource.Validation);
		}

		if (report.IsValid)
		{
			return true;
		}

		SetStatus(
			$"Play refused: {report.Errors} broken " +
			$"reference{(report.Errors == 1 ? string.Empty : "s")}");

		return false;
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
		_runtimeDiagnosticCount = 0;
		_runtimeLogCount = 0;
		_reportedDroppedLogMessages = 0;
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

		// A system that fails mid-run reports once, through the same
		// Console every other editor message uses.
		ReportRuntimeDiagnostics();

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
		EditorLogLevel level = EditorLogLevel.Info,
		EditorLogSource source = EditorLogSource.Editor)
	{
		ArgumentNullException.ThrowIfNull(message);

		_log.Add(new EditorLogEntry(DateTime.Now, level, message, source));

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

		UnloadGameplay();

		_disposed = true;
	}

	/// <summary>
	/// Loads a built gameplay assembly and reports what it declared.
	/// </summary>
	private void LoadGameplay(string assemblyPath)
	{
		UnloadGameplay();

		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			assemblyPath,
			ComponentCatalog.Default);

		foreach (string diagnostic in result.Diagnostics)
		{
			Log(diagnostic, EditorLogLevel.Warning);
		}

		if (!result.Succeeded)
		{
			Log(
				result.Error ?? "The game code did not load.",
				EditorLogLevel.Error);

			SetStatus("Game code did not load");
			return;
		}

		_gameplay = result.Assembly;

		GameplayRegistrar registration = _gameplay!.Registration;

		Log(
			$"Loaded game code '{_gameplay.AssemblyName}': " +
			$"{registration.Components.Count} component(s), " +
			$"{registration.Bindings.Count} binding(s), " +
			$"{registration.Systems.Count} system(s), " +
			$"{registration.Descriptions.Count} runtime description(s).");

		if (IsSceneDirty)
		{
			Log(
				"Save or reopen the scene to edit components this build " +
				"introduced.",
				EditorLogLevel.Warning);

			return;
		}

		if (ScenePath is string path)
		{
			OpenScene(path);
		}
	}

	private void UnloadGameplay()
	{
		if (_gameplay is null)
		{
			return;
		}

		_gameplay.Dispose();
		_gameplay = null;
	}

	/// <summary>
	/// Loads the project's already-built game code for Play Mode.
	/// </summary>
	/// <remarks>
	/// A project with no build yet still plays; it runs the built-in
	/// behaviours and the Console says how to produce game code.
	/// </remarks>
	private void TryLoadProjectGameplay()
	{
		if (_gameplay is not null || Project is not EditorProject project)
		{
			return;
		}

		if (!project.TryResolveGameplayAssembly(out string assemblyPath))
		{
			if (project.GameplayProjectPath is not null)
			{
				Log(
					"No built game code was found. Use Assets → Build Game " +
					$"Code to compile '{project.GameplayProjectPath}'.",
					EditorLogLevel.Warning);
			}

			return;
		}

		LoadGameplay(assemblyPath);
	}

	private void ReportRuntimeDiagnostics()
	{
		if (_play?.Runtime is not RuntimeScene runtime)
		{
			return;
		}

		IReadOnlyList<string> diagnostics = runtime.Diagnostics;

		while (_runtimeDiagnosticCount < diagnostics.Count)
		{
			Log(diagnostics[_runtimeDiagnosticCount], EditorLogLevel.Warning);
			_runtimeDiagnosticCount++;
		}

		ReportRuntimeLog();
	}

	/// <summary>
	/// Copies messages the running game wrote into the Console.
	/// </summary>
	/// <remarks>
	/// The world owns the log and the editor only reads what it has not
	/// seen, so a message appears once no matter how often a frame is
	/// pumped. Messages dropped past the world's buffer limit are reported
	/// once, because a game logging every tick must not bury everything
	/// else.
	/// </remarks>
	private void ReportRuntimeLog()
	{
		if (_play?.Runtime is not RuntimeScene runtime)
		{
			return;
		}

		IReadOnlyList<RuntimeLogEntry> entries = runtime.Log.Entries;

		while (_runtimeLogCount < entries.Count)
		{
			RuntimeLogEntry entry = entries[_runtimeLogCount++];

			Log(
				$"Game: {entry.Message}",
				entry.Level == RuntimeLogLevel.Warning
					? EditorLogLevel.Warning
					: EditorLogLevel.Info,
				EditorLogSource.Game);
		}

		int dropped = runtime.Log.DroppedCount;

		if (dropped > _reportedDroppedLogMessages)
		{
			_reportedDroppedLogMessages = dropped;

			Log(
				$"Game logged {dropped} more messages than Play Mode " +
					"keeps; the rest were dropped.",
				EditorLogLevel.Warning,
				EditorLogSource.Game);
		}
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

	private void OpenProjectCore(EditorProject project, bool validate = true)
	{
		Project = project;
		ProjectChanged?.Invoke(this);

		SceneDocument startup = project.LoadStartupScene();
		SetScene(
			startup,
			project.StartupScenePath,
			dirty: false);

		Log($"Startup scene '{startup.Name}' loaded.");

		if (validate)
		{
			ReportProjectValidation(project);
		}
	}

	/// <summary>
	/// Reports what the project that was just opened cannot draw or run.
	/// </summary>
	/// <remarks>
	/// Opening is when a developer learns that a file was moved or an
	/// assembly was never built. Play validates the scene it runs, but this
	/// is the project-wide answer, including scenes that are not open.
	/// </remarks>
	private void ReportProjectValidation(EditorProject project)
	{
		ProjectValidationReport report = ProjectValidator.Validate(project);

		foreach (ProjectIssue issue in report.Issues)
		{
			Log(
				$"{issue.Location}: {issue.Message}",
				issue.Severity == ProjectIssueSeverity.Error
					? EditorLogLevel.Error
					: EditorLogLevel.Warning,
				EditorLogSource.Validation);
		}

		if (report.Issues.Count == 0)
		{
			return;
		}

		Log(
			$"Project validation: {report.Errors} " +
			$"error{(report.Errors == 1 ? string.Empty : "s")}, " +
			$"{report.Warnings} " +
			$"warning{(report.Warnings == 1 ? string.Empty : "s")}.");

		if (report.Errors > 0)
		{
			SetStatus(
				$"Opened with {report.Errors} project " +
				$"error{(report.Errors == 1 ? string.Empty : "s")} — see Console");
		}
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
