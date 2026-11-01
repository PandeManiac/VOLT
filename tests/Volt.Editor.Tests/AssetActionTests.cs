using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the actions the Project Browser offers on an asset: placing an
/// imported mesh in the scene and assigning a material to what is
/// selected, without a detour through the Inspector.
/// </summary>
public sealed class AssetActionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-asset-actions-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public AssetActionTests()
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
			// A leftover file must not fail a test that already ran.
		}
	}

	[Fact]
	public void AMeshIsPlacedAsOneUndoStep()
	{
		EditorProject project = OpenProject();
		WriteMesh(project);

		SceneEntity? placed = _session.PlaceMeshAsset("Assets/Meshes/triangle.obj");

		Assert.NotNull(placed);
		Assert.Equal("triangle", placed!.Name);
		Assert.Equal(_session.ActiveEntityId, placed.Id);

		MeshRendererData renderer =
			Assert.IsType<MeshRendererData>(
				placed.GetComponent(MeshRendererData.ComponentTypeId));

		Assert.Equal("Assets/Meshes/triangle.obj", renderer.Mesh);
		Assert.Equal(MaterialLoader.DefaultId, renderer.Material);

		_session.UndoLast();

		Assert.Null(_session.Scene!.Find(placed.Id));
	}

	[Fact]
	public void ABrokenMeshIsNotPlaced()
	{
		EditorProject project = OpenProject();
		Write(project, "Assets/Meshes/broken.obj", "v 0 0 0\nf 1 2 3\n");

		int before = _session.Scene!.Entities.Count;

		Assert.Null(_session.PlaceMeshAsset("Assets/Meshes/broken.obj"));
		Assert.Equal(before, _session.Scene.Entities.Count);
		Assert.Contains(
			_session.LogEntries,
			entry => entry.Message.Contains(
				"could not be read",
				StringComparison.Ordinal));
	}

	[Fact]
	public void ASceneIsNotAMesh()
	{
		EditorProject project = OpenProject();
		project.CreateScene("Scenes/Other.volt.scene.json", "Other");
		project.AssetCatalog.Refresh();

		Assert.Null(
			_session.PlaceMeshAsset("Scenes/Other.volt.scene.json"));
		Assert.Equal("That asset is not a mesh", _session.Status);
	}

	[Fact]
	public void AMaterialIsAppliedToEverySelectedRenderer()
	{
		EditorProject project = OpenProject();
		string material = WriteMaterial(project);

		SceneEntity first = AddCube("First");
		SceneEntity second = AddCube("Second");
		SceneEntity empty = _session.CreateEntity("Empty")!;

		_session.SelectMany(
			[first.Id, second.Id, empty.Id],
			empty.Id);

		Assert.Equal(2, _session.AssignMaterialToSelection(material));

		Assert.Equal(
			material,
			first.GetComponent<MeshRendererData>()!.Material);
		Assert.Equal(
			material,
			second.GetComponent<MeshRendererData>()!.Material);
		Assert.Null(empty.GetComponent(MeshRendererData.ComponentTypeId));
	}

	[Fact]
	public void AMissingMaterialIsReported()
	{
		OpenProject();
		AddCube("Cube");

		Assert.Equal(
			0,
			_session.AssignMaterialToSelection(
				"Assets/Materials/Missing.material"));

		Assert.Contains(
			_session.LogEntries,
			entry => entry.Message.Contains(
				"not part of this project",
				StringComparison.Ordinal));
	}

	private EditorProject OpenProject()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		return Assert.IsType<EditorProject>(_session.Project);
	}

	private SceneEntity AddCube(string name)
	{
		SceneEntity entity = _session.CreateEntity(name)!;

		_session.AddComponent(
			entity.Id,
			MeshRendererData.ComponentTypeId);

		return entity;
	}

	private static string WriteMesh(EditorProject project)
	{
		return Write(
			project,
			"Assets/Meshes/triangle.obj",
			"""
			v 0 0 0
			v 1 0 0
			v 0 1 0
			f 1 2 3
			""");
	}

	private static string WriteMaterial(EditorProject project)
	{
		Write(
			project,
			"Assets/Materials/Red.material",
			"""{ "albedo": "#FF0000" }""");

		project.AssetCatalog.Refresh();

		return "Assets/Materials/Red.material";
	}

	private static string Write(
		EditorProject project,
		string relativePath,
		string content)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? project.RootPath);
		File.WriteAllText(path, content);
		project.AssetCatalog.Refresh();

		return path;
	}
}
