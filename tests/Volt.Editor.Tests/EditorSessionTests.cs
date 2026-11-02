using System.Numerics;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies the editor's project, authoring, and play-mode behavior
/// without opening a window.
/// </summary>
public sealed class EditorSessionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-editor-tests-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EditorSessionTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file on some platforms must not fail a test
			// that already completed.
		}
	}

	[Fact]
	public void CreateEntity_UndoAndRedoRestoreSelectionContext()
	{
		_session.NewProject(Path.Combine(_root, "CreateUndo"), "CreateUndo");
		SceneDocument scene = _session.Scene!;
		SceneEntity previous = scene.Entities.First();
		_session.Select(previous.Id);

		SceneEntity created = _session.CreateEntity("Created")!;
		Assert.Equal(created.Id, _session.ActiveEntityId);

		_session.UndoLast();
		Assert.Null(scene.Find(created.Id));
		Assert.Equal(previous.Id, _session.ActiveEntityId);
		Assert.Contains(previous.Id, _session.Selection);

		_session.RedoLast();
		Assert.NotNull(scene.Find(created.Id));
		Assert.Equal(created.Id, _session.ActiveEntityId);
		Assert.Contains(created.Id, _session.Selection);
	}

	[Fact]
	public void RemovingHierarchyRoot_ClearsSelectionOfRemovedDescendants()
	{
		_session.NewProject(Path.Combine(_root, "RemoveSubtree"), "RemoveSubtree");
		SceneDocument scene = _session.Scene!;
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;
		_session.Select(child.Id);

		scene.RemoveEntity(parent.Id);

		Assert.Null(scene.Find(parent.Id));
		Assert.Null(scene.Find(child.Id));
		Assert.Empty(_session.Selection);
		Assert.Null(_session.ActiveEntityId);
		Assert.Null(_session.ActiveEntity);
	}

	[Fact]
	public void FailedOpenProject_PreservesTheCurrentProjectAndSelection()
	{
		_session.NewProject(Path.Combine(_root, "Current"), "Current");
		SceneEntity selected = _session.CreateEntity("Selected")!;
		_session.Select(selected.Id);
		EditorProject originalProject = _session.Project!;
		SceneDocument originalScene = _session.Scene!;
		string invalidProject = Path.Combine(_root, "NotAProject");
		Directory.CreateDirectory(invalidProject);

		Assert.Throws<FileNotFoundException>(
			() => _session.OpenProject(invalidProject));

		Assert.Same(originalProject, _session.Project);
		Assert.Same(originalScene, _session.Scene);
		Assert.Equal(selected.Id, _session.ActiveEntityId);
		Assert.Same(selected, _session.ActiveEntity);
	}

	[Fact]
	public void FailedNewProject_PreservesTheCurrentProjectAndSelection()
	{
		_session.NewProject(Path.Combine(_root, "Current"), "Current");
		SceneEntity selected = _session.CreateEntity("Selected")!;
		_session.Select(selected.Id);
		EditorProject originalProject = _session.Project!;
		SceneDocument originalScene = _session.Scene!;

		Assert.Throws<InvalidOperationException>(
			() => _session.NewProject(originalProject.RootPath, "Collision"));

		Assert.Same(originalProject, _session.Project);
		Assert.Same(originalScene, _session.Scene);
		Assert.Equal(selected.Id, _session.ActiveEntityId);
		Assert.Same(selected, _session.ActiveEntity);
	}

	[Fact]
	public void ProjectWithMissingStartupScene_PreservesTheCurrentProject()
	{
		_session.NewProject(Path.Combine(_root, "Current"), "Current");
		SceneEntity selected = _session.CreateEntity("Selected")!;
		_session.Select(selected.Id);
		EditorProject originalProject = _session.Project!;
		SceneDocument originalScene = _session.Scene!;

		EditorProject invalidProject = EditorProject.Create(
			Path.Combine(_root, "MissingStartup"),
			"MissingStartup");
		File.Delete(invalidProject.ResolvePath(
			invalidProject.StartupScenePath));

		Assert.Throws<FileNotFoundException>(
			() => _session.OpenProject(invalidProject.RootPath));

		Assert.Same(originalProject, _session.Project);
		Assert.Same(originalScene, _session.Scene);
		Assert.Equal(selected.Id, _session.ActiveEntityId);
	}

	[Fact]
	public void FailedSceneOpen_DoesNotStopTheCurrentPlaySession()
	{
		_session.NewProject(Path.Combine(_root, "OpenScene"), "OpenScene");
		SceneDocument originalScene = _session.Scene!;
		_session.StartPlay();
		PlaySession originalPlay = _session.Play!;

		Assert.Throws<FileNotFoundException>(
			() => _session.OpenScene("Scenes/Missing.volt.scene.json"));

		Assert.Same(originalScene, _session.Scene);
		Assert.Same(originalPlay, _session.Play);
		Assert.Equal(PlayState.Playing, _session.PlayState);
	}

	[Fact]
	public void CreateScene_StopsTheCurrentPlaySessionAfterSceneCreationSucceeds()
	{
		_session.NewProject(Path.Combine(_root, "CreateScene"), "CreateScene");
		SceneDocument originalScene = _session.Scene!;
		_session.StartPlay();
		PlaySession originalPlay = _session.Play!;

		SceneDocument created = _session.CreateScene(
			"Scenes/New.volt.scene.json",
			"New")!;

		Assert.NotSame(originalScene, created);
		Assert.Same(created, _session.Scene);
		Assert.Null(_session.Play);
		Assert.Equal(PlayState.Stopped, _session.PlayState);
		Assert.Equal(PlayState.Stopped, originalPlay.State);
	}

	[Fact]
	public void NewProject_OpensStarterSceneThatReopensWithSameIds()
	{
		string projectRoot = Path.Combine(_root, "Game");

		_session.NewProject(projectRoot, "Game");

		Assert.NotNull(_session.Project);
		Assert.NotNull(_session.Scene);
		Assert.NotNull(_session.ScenePath);
		Assert.True(
			File.Exists(Path.Combine(projectRoot, "project.volt.json")));

		Guid sceneId = _session.Scene!.Id;
		Guid[] entityIds = [.. _session.Scene.Entities
			.Select(entity => entity.Id)];

		_session.SaveScene();
		_session.CloseProject();
		_session.OpenProject(projectRoot);

		Assert.NotNull(_session.Scene);
		Assert.Equal(sceneId, _session.Scene!.Id);
		Assert.Equal(
			entityIds,
			_session.Scene.Entities.Select(entity => entity.Id));

		Assert.Contains(
			_session.Scene.Entities,
			entity => entity.Name == "Cube" &&
				entity.GetComponent<SpinBehaviourData>() is not null);
	}

	[Fact]
	public void PlayMode_RunsRuntimeAndStopPreservesAuthoredState()
	{
		_session.NewProject(
			Path.Combine(_root, "Play"),
			"Play");

		SceneDocument scene = _session.Scene!;
		SceneEntity cube = Assert.Single(
			scene.Entities,
			entity => entity.Name == "Cube");

		_session.Select(cube.Id);
		_session.SetTransform(
			cube.Id,
			new TransformData
			{
				Position = new Vector3(1f, 2f, 3f),
				Scale = Vector3.One
			});

		TransformData authored = cube.Transform;
		int authoredCount = scene.EntityCount;

		_session.StartPlay();
		_session.Update(0.4);

		Assert.Equal(PlayState.Playing, _session.PlayState);
		Assert.NotNull(_session.Play);
		Assert.NotNull(_session.Play!.Runtime);
		Assert.True(_session.Play.Runtime!.TickCount > 0);
		Assert.Equal(authoredCount, scene.EntityCount);
		Assert.Equal(authored.Position, cube.Transform.Position);

		_session.Stop();

		Assert.Equal(PlayState.Stopped, _session.PlayState);
		Assert.Null(_session.Play);
		Assert.Equal(authoredCount, scene.EntityCount);
		Assert.Equal(authored.Position, cube.Transform.Position);
		Assert.Equal(
			new Vector3(1f, 2f, 3f),
			cube.Transform.Position);
	}

	[Fact]
	public void TransformGesture_IsOneUndoableAction()
	{
		_session.NewProject(
			Path.Combine(_root, "Undo"),
			"Undo");

		SceneEntity cube = Assert.Single(
			_session.Scene!.Entities,
			entity => entity.Name == "Cube");

		Vector3 original = cube.Transform.Position;

		// The viewport previews a drag by mutating the authored
		// transform, then commits the gesture once.
		cube.Transform.Position = new Vector3(9f, 9f, 9f);

		_session.CommitTransform(cube.Id, new TransformData
		{
			Position = original,
			EulerAngles = Vector3.Zero,
			Scale = Vector3.One
		});

		Assert.Equal(new Vector3(9f, 9f, 9f), cube.Transform.Position);

		_session.UndoLast();

		Assert.Equal(original, cube.Transform.Position);

		_session.RedoLast();

		Assert.Equal(new Vector3(9f, 9f, 9f), cube.Transform.Position);
	}

	[Fact]
	public void AssetRenameAndDelete_UpdateTheOpenScene()
	{
		_session.NewProject(
			Path.Combine(_root, "Assets"),
			"Assets");

		string meshId = "Assets/Meshes/cube.mesh";

		Directory.CreateDirectory(
			Path.Combine(_session.Project!.AssetsPath, "Meshes"));
		File.WriteAllText(_session.Project.ResolvePath(meshId), "mesh");
		_session.Project.AssetCatalog.Refresh();

		SceneEntity cube = _session.Scene!.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = cube.GetComponent<MeshRendererData>()!;
		renderer.Mesh = meshId;
		_session.SaveScene();

		string renamed = _session.RenameAsset(meshId, "box");

		Assert.Equal("Assets/Meshes/box.mesh", renamed);
		Assert.Equal(renamed, renderer.Mesh);
		Assert.False(_session.IsSceneDirty);

		SceneDocument reloaded = _session.Project.LoadStartupScene();
		SceneEntity reloadedCube = reloaded.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			renamed,
			reloadedCube.GetComponent<MeshRendererData>()!.Mesh);

		_session.DeleteAsset(renamed);

		Assert.Equal(string.Empty, renderer.Mesh);
		Assert.False(_session.IsSceneDirty);

		SceneDocument afterDelete = _session.Project.LoadStartupScene();
		SceneEntity afterDeleteCube = afterDelete.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			string.Empty,
			afterDeleteCube.GetComponent<MeshRendererData>()!.Mesh);
	}

	[Fact]
	public void AssetRename_RemapsUndoAndRedoPropertyValues()
	{
		_session.NewProject(
			Path.Combine(_root, "UndoAssets"),
			"Undo Assets");

		EditorProject project = _session.Project!;

		Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Meshes"));

		const string MeshId = "Assets/Meshes/cube.mesh";

		File.WriteAllText(project.ResolvePath(MeshId), "mesh");
		project.AssetCatalog.Refresh();

		SceneEntity cube = _session.Scene!.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = cube.GetComponent<MeshRendererData>()!;
		EditorPropertyDescriptor property = ComponentCatalog.Default
			.Get(MeshRendererData.ComponentTypeId)
			.Properties.Single(candidate =>
				candidate.Name == nameof(MeshRendererData.Mesh));

		renderer.Mesh = "builtin:cube";
		_session.SetComponentProperty(cube.Id, renderer, property, MeshId);

		string renamed = _session.RenameAsset(MeshId, "box");

		Assert.Equal(renamed, renderer.Mesh);

		_session.UndoLast();

		Assert.Equal("builtin:cube", renderer.Mesh);

		_session.RedoLast();

		Assert.Equal(renamed, renderer.Mesh);
	}

	[Fact]
	public void FolderRename_UpdatesTheOpenScenePathAndReferences()
	{
		_session.NewProject(
			Path.Combine(_root, "FolderRename"),
			"Folder Rename");

		EditorProject project = _session.Project!;

		project.CreateFolder("Scenes/Levels");
		project.CreateFolder("Assets/Materials");

		string meshId = "Assets/Materials/cube.mesh";

		File.WriteAllText(project.ResolvePath(meshId), "mesh");
		project.AssetCatalog.Refresh();

		Assert.NotNull(_session.CreateScene(
			"Scenes/Levels/Level.volt.scene.json",
			"Level"));

		SceneDocument level = _session.Scene!;
		SceneEntity cube = level.CreateEntity("Cube");
		cube.AddComponent(new MeshRendererData { Mesh = meshId });
		_session.SaveScene();

		string textures = _session.RenameFolder("Assets/Materials", "Textures");

		Assert.Equal("Assets/Textures", textures);
		Assert.Equal(
			"Assets/Textures/cube.mesh",
			cube.GetComponent<MeshRendererData>()!.Mesh);
		Assert.False(_session.IsSceneDirty);

		string maps = _session.RenameFolder("Scenes/Levels", "Maps");

		Assert.Equal("Scenes/Maps", maps);
		Assert.Equal(
			"Scenes/Maps/Level.volt.scene.json",
			_session.ScenePath);
		Assert.NotNull(_session.ScenePath);
		Assert.True(File.Exists(project.ResolvePath(_session.ScenePath)));

		SceneDocument reloaded = project.LoadScene(_session.ScenePath);
		SceneEntity reloadedCube = reloaded.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			"Assets/Textures/cube.mesh",
			reloadedCube.GetComponent<MeshRendererData>()!.Mesh);
	}

	[Fact]
	public void DeleteAssets_ClearsReferencesAndProtectsTheOpenScene()
	{
		_session.NewProject(
			Path.Combine(_root, "ManyAssets"),
			"Many Assets");

		string first = "Assets/Meshes/first.mesh";
		string second = "Assets/Meshes/second.mesh";

		Directory.CreateDirectory(
			Path.Combine(_session.Project!.AssetsPath, "Meshes"));
		File.WriteAllText(_session.Project.ResolvePath(first), "mesh");
		File.WriteAllText(_session.Project.ResolvePath(second), "mesh");
		_session.Project.AssetCatalog.Refresh();

		SceneEntity cube = _session.Scene!.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = cube.GetComponent<MeshRendererData>()!;
		renderer.Mesh = first;
		_session.SaveScene();

		Assert.Throws<InvalidOperationException>(() =>
			_session.DeleteAssets([_session.ScenePath!, second]));

		Assert.True(File.Exists(_session.Project.ResolvePath(first)));
		Assert.True(File.Exists(_session.Project.ResolvePath(second)));
		Assert.Equal(first, renderer.Mesh);

		int deleted = _session.DeleteAssets([first, second]);

		Assert.Equal(2, deleted);
		Assert.False(File.Exists(_session.Project.ResolvePath(first)));
		Assert.False(File.Exists(_session.Project.ResolvePath(second)));
		Assert.Equal(string.Empty, renderer.Mesh);
		Assert.False(_session.IsSceneDirty);

		SceneDocument reloaded = _session.Project.LoadStartupScene();
		SceneEntity reloadedCube = reloaded.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			string.Empty,
			reloadedCube.GetComponent<MeshRendererData>()!.Mesh);
	}

	[Fact]
	public void Reparent_RejectsHierarchyCycles()
	{
		_session.NewProject(
			Path.Combine(_root, "Hierarchy"),
			"Hierarchy");

		SceneDocument scene = _session.Scene!;
		SceneEntity parent = scene.CreateEntity("Parent");
		SceneEntity child = scene.CreateEntity("Child", parent.Id);
		SceneEntity grandChild = scene.CreateEntity("Grandchild", child.Id);

		Assert.False(scene.CanReparent(parent.Id, grandChild.Id));
		Assert.Throws<InvalidOperationException>(
			() => scene.ReparentEntity(parent.Id, grandChild.Id));

		Assert.Null(parent.ParentId);
	}
}
