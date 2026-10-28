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
}
