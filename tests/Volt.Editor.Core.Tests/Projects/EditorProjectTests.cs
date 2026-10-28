using System.Text.Json;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Projects;

public sealed class EditorProjectTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}

	[Fact]
	public void CreateAndReopen_PreservesIdentityAndStartupScene()
	{
		EditorProject created = EditorProject.Create(_root, "Sample");
		SceneDocument scene = created.LoadStartupScene();

		Assert.Equal(2, scene.EntityCount);
		Assert.NotNull(scene.Entities[0].GetComponent<CameraData>());
		Assert.NotNull(scene.Entities[1].GetComponent<MeshRendererData>());

		EditorProject reopened = EditorProject.Open(_root);

		Assert.Equal(created.Id, reopened.Id);
		Assert.Equal("Sample", reopened.Name);
		Assert.Equal(
			created.StartupScenePath,
			reopened.StartupScenePath);
		Assert.Equal(scene.Id, reopened.LoadStartupScene().Id);
	}

	[Fact]
	public void Create_RefusesToOverwriteNonEmptyDirectory()
	{
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, "keep.txt"), "keep");

		Assert.Throws<InvalidOperationException>(
			() => EditorProject.Create(_root, "Sample"));

		Assert.Equal(
			"keep",
			File.ReadAllText(Path.Combine(_root, "keep.txt")));
	}

	[Fact]
	public void ResolvePath_RejectsAbsoluteAndTraversalPaths()
	{
		EditorProject project = EditorProject.Create(_root, "Paths");

		Assert.Throws<ArgumentException>(
			() => project.ResolvePath("../outside.volt.scene.json"));

		Assert.Throws<ArgumentException>(
			() => project.ResolvePath("/tmp/outside.volt.scene.json"));

		string scenePath = project.ResolvePath(
			"Scenes/Test.volt.scene.json");

		Assert.StartsWith(
			project.RootPath + Path.DirectorySeparatorChar,
			scenePath);
	}

	[Fact]
	public void AssetCatalog_RefreshFindsFilesAndResolvesMissingAssets()
	{
		EditorProject project = EditorProject.Create(_root, "Assets");
		string meshDirectory = Path.Combine(project.AssetsPath, "Meshes");
		Directory.CreateDirectory(meshDirectory);
		File.WriteAllText(
			Path.Combine(meshDirectory, "cube.mesh"),
			"fixture");

		project.AssetCatalog.Refresh();

		ProjectAsset asset = Assert.Single(
			project.AssetCatalog.Assets,
			found => found.FileName == "cube.mesh");

		Assert.Equal("Assets/Meshes/cube.mesh", asset.Id);
		Assert.Equal(ProjectAssetKind.Mesh, asset.Kind);
		Assert.True(project.AssetCatalog.FileExists(asset.Id));
		Assert.False(project.AssetCatalog.FileExists(
			"Assets/Meshes/missing.mesh"));
	}

	[Fact]
	public void CreateFolder_CreatesAnEmptyFolderAndCatalogReportsIt()
	{
		EditorProject project = EditorProject.Create(_root, "Folders");

		string folder = project.CreateFolder("Assets/Materials");

		Assert.Equal("Assets/Materials", folder);
		Assert.True(Directory.Exists(project.ResolvePath(folder)));
		Assert.Empty(Directory.EnumerateFiles(
			project.ResolvePath(folder),
			"*",
			SearchOption.AllDirectories));
		Assert.Contains("Assets", project.AssetCatalog.Folders);
		Assert.Contains("Scenes", project.AssetCatalog.Folders);
		Assert.Contains(folder, project.AssetCatalog.Folders);

		project.AssetCatalog.Refresh();

		Assert.Contains(folder, project.AssetCatalog.Folders);
	}

	[Fact]
	public void CreateFolder_RejectsProtectedRootsAndMissingParents()
	{
		EditorProject project = EditorProject.Create(_root, "Folder guards");
		project.CreateFolder("Assets/Materials");

		Assert.Throws<IOException>(() =>
			project.CreateFolder("Assets/Materials"));
		Assert.Throws<ArgumentException>(() =>
			project.CreateFolder("Tools/Generated"));
		Assert.Throws<ArgumentException>(() =>
			project.CreateFolder("Assets/../Escape"));
		Assert.Throws<ArgumentException>(() =>
			project.CreateFolder("Assets/"));
		Assert.Throws<DirectoryNotFoundException>(() =>
			project.CreateFolder("Assets/Missing/Child"));

		Assert.False(Directory.Exists(Path.Combine(
			project.RootPath,
			"Tools",
			"Generated")));
	}

	[Fact]
	public void SaveScene_WritesVersionedSceneAndReportsPath()
	{
		EditorProject project = EditorProject.Create(_root, "Scenes");
		SceneDocument scene = project.CreateScene(
			"Scenes/Level2.volt.scene.json",
			"Level 2");

		scene.CreateEntity("New Object");
		project.SaveScene(scene, "Scenes/Level2.volt.scene.json");

		SceneDocument restored = project.LoadScene(
			"Scenes/Level2.volt.scene.json");

		Assert.Equal(scene.Id, restored.Id);
		Assert.Equal(1, restored.EntityCount);
	}

	[Fact]
	public void DuplicateAsset_UsesNumberedSiblingsWithoutOverwriting()
	{
		EditorProject project = EditorProject.Create(_root, "Duplicates");
		project.CreateScene("Scenes/Level.volt.scene.json", "Level");

		string first = project.DuplicateAsset("Scenes/Level.volt.scene.json");
		string second = project.DuplicateAsset("Scenes/Level.volt.scene.json");

		Assert.Equal("Scenes/Level 1.volt.scene.json", first);
		Assert.Equal("Scenes/Level 2.volt.scene.json", second);
		Assert.True(File.Exists(project.ResolvePath(first)));
		Assert.True(File.Exists(project.ResolvePath(second)));
		Assert.True(project.AssetCatalog.FileExists(first));
		Assert.True(project.AssetCatalog.FileExists(second));
		Assert.True(File.Exists(project.ResolvePath(
			"Scenes/Level.volt.scene.json")));
	}

	[Fact]
	public void RenameAsset_MovesTheFileAndUpdatesTheStartupPath()
	{
		EditorProject project = EditorProject.Create(_root, "Rename");

		string renamed = project.RenameAsset(
			"Scenes/Main.volt.scene.json",
			"Start");

		Assert.Equal("Scenes/Start.volt.scene.json", renamed);
		Assert.False(File.Exists(project.ResolvePath(
			"Scenes/Main.volt.scene.json")));
		Assert.True(File.Exists(project.ResolvePath(renamed)));
		Assert.Equal(renamed, project.StartupScenePath);
		Assert.True(project.AssetCatalog.FileExists(renamed));

		EditorProject reopened = EditorProject.Open(_root);

		Assert.Equal(renamed, reopened.StartupScenePath);
		Assert.NotNull(reopened.LoadStartupScene());
	}

	[Fact]
	public void RenameAsset_RejectsInvalidNamesAndExistingFiles()
	{
		EditorProject project = EditorProject.Create(_root, "Rename guards");
		project.CreateScene("Scenes/Other.volt.scene.json", "Other");

		Assert.Throws<IOException>(() => project.RenameAsset(
			"Scenes/Main.volt.scene.json",
			"Other"));
		Assert.Throws<ArgumentException>(() => project.RenameAsset(
			"Scenes/Main.volt.scene.json",
			"../Escape"));
		Assert.Throws<ArgumentException>(() => project.RenameAsset(
			"Scenes/Main.volt.scene.json",
			"Main.volt.scene.json"));

		Assert.True(File.Exists(project.ResolvePath(
			"Scenes/Main.volt.scene.json")));
	}

	[Fact]
	public void RenameAsset_HandlesFilesWithoutAnExtension()
	{
		EditorProject project = EditorProject.Create(_root, "Extensionless");
		File.WriteAllText(
			Path.Combine(project.AssetsPath, "README"),
			"notes");
		project.AssetCatalog.Refresh();

		string renamed = project.RenameAsset("Assets/README", "NOTES");

		Assert.Equal("Assets/NOTES", renamed);
		Assert.False(File.Exists(project.ResolvePath("Assets/README")));
		Assert.True(File.Exists(project.ResolvePath(renamed)));
	}

	[Fact]
	public void DeleteAsset_RemovesAFileButProtectsTheStartupScene()
	{
		EditorProject project = EditorProject.Create(_root, "Delete");
		project.CreateScene("Scenes/Old.volt.scene.json", "Old");

		project.DeleteAsset("Scenes/Old.volt.scene.json");

		Assert.False(File.Exists(project.ResolvePath(
			"Scenes/Old.volt.scene.json")));
		Assert.False(project.AssetCatalog.FileExists(
			"Scenes/Old.volt.scene.json"));

		Assert.Throws<InvalidOperationException>(() =>
			project.DeleteAsset(project.StartupScenePath));
		Assert.True(File.Exists(project.ResolvePath(
			project.StartupScenePath)));
	}

	[Fact]
	public void RenameAsset_RewritesSceneReferences()
	{
		EditorProject project = EditorProject.Create(_root, "References");
		string meshId = "Assets/Meshes/cube.mesh";
		Directory.CreateDirectory(
			Path.Combine(project.AssetsPath, "Meshes"));
		File.WriteAllText(project.ResolvePath(meshId), "mesh");

		SceneDocument scene = project.LoadStartupScene();
		SceneEntity cube = scene.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = cube.GetComponent<MeshRendererData>()!;
		renderer.Mesh = meshId;

		using JsonDocument payload = JsonDocument.Parse(
			"""{"mesh":"Assets/Meshes/cube.mesh","label":"keep"}""");

		cube.AddComponent(new UnknownComponentData(
			"future.renderer",
			payload.RootElement));

		project.SaveScene(scene, project.StartupScenePath);

		string renamed = project.RenameAsset(meshId, "box");

		Assert.Equal("Assets/Meshes/box.mesh", renamed);

		SceneDocument reloaded = project.LoadStartupScene();
		SceneEntity reloadedCube = reloaded.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			renamed,
			reloadedCube.GetComponent<MeshRendererData>()!.Mesh);

		UnknownComponentData unknown = Assert.IsType<UnknownComponentData>(
			reloadedCube.Components.Single(
				component => component is UnknownComponentData));

		Assert.Equal(
			renamed,
			unknown.Data.GetProperty("mesh").GetString());
		Assert.Equal("keep", unknown.Data.GetProperty("label").GetString());
	}

	[Fact]
	public void DeleteAsset_ClearsSceneReferences()
	{
		EditorProject project = EditorProject.Create(_root, "Delete references");
		string meshId = "Assets/Meshes/cube.mesh";
		Directory.CreateDirectory(
			Path.Combine(project.AssetsPath, "Meshes"));
		File.WriteAllText(project.ResolvePath(meshId), "mesh");

		SceneDocument scene = project.LoadStartupScene();
		SceneEntity cube = scene.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);
		cube.GetComponent<MeshRendererData>()!.Mesh = meshId;
		project.SaveScene(scene, project.StartupScenePath);

		project.DeleteAsset(meshId);

		Assert.False(File.Exists(project.ResolvePath(meshId)));

		SceneDocument reloaded = project.LoadStartupScene();
		SceneEntity reloadedCube = reloaded.Entities.Single(entity =>
			entity.GetComponent<MeshRendererData>() is not null);

		Assert.Equal(
			string.Empty,
			reloadedCube.GetComponent<MeshRendererData>()!.Mesh);
	}
}
