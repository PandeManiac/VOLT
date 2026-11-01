using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;

namespace Volt.Editor.Core.Tests.Projects;

/// <summary>
/// Covers the material half of authoring: creating a file without
/// overwriting one, loading the colour it sets, and reporting what
/// cannot be read.
/// </summary>
public sealed class ProjectMaterialTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-materials-{Guid.NewGuid():N}");

	public void Dispose()
	{
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
	public void CreatingMaterialsNamesAgainstExistingOnes()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");

		string first = project.CreateMaterial();
		string second = project.CreateMaterial();

		Assert.Equal("Assets/Materials/Material.material", first);
		Assert.Equal("Assets/Materials/Material 1.material", second);
		Assert.True(project.AssetCatalog.FileExists(first));
		Assert.True(project.AssetCatalog.FileExists(second));
		Assert.Equal(
			ProjectAssetKind.Material,
			project.AssetCatalog.Find(first)!.Kind);

		Assert.True(project.TryLoadMaterial(
			first,
			out MaterialAsset material,
			out string? diagnostic));

		Assert.Null(diagnostic);
		Assert.Equal(
			MaterialLoader.DefaultAlbedo.X,
			material.Albedo.X,
			3);
	}

	[Fact]
	public void AnEditedMaterialReplacesTheCachedColour()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");
		string id = project.CreateMaterial();
		string path = project.ResolvePath(id);

		File.WriteAllText(path, """{ "albedo": "#FF0000" }""");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
		project.AssetCatalog.Refresh();

		Assert.True(project.TryLoadMaterial(
			id,
			out MaterialAsset material,
			out _));

		Assert.Equal(1f, material.Albedo.X, 4);
		Assert.Equal(0f, material.Albedo.Y, 4);
	}

	[Fact]
	public void ASceneIsNotAMaterial()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");

		project.CreateScene("Scenes/Other.volt.scene.json", "Other");
		project.AssetCatalog.Refresh();

		Assert.False(project.TryLoadMaterial(
			"Scenes/Other.volt.scene.json",
			out _,
			out string? diagnostic));

		Assert.Contains("not a material file", diagnostic);
	}

	[Fact]
	public void ABrokenMaterialIsReportedRatherThanThrown()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");
		string path = Path.Combine(project.AssetsPath, "Broken.material");

		Directory.CreateDirectory(project.AssetsPath);
		File.WriteAllText(path, "{ albedo: }");
		project.AssetCatalog.Refresh();

		Assert.False(project.TryLoadMaterial(
			"Assets/Broken.material",
			out _,
			out string? diagnostic));

		Assert.Contains("could not be read", diagnostic);
	}

	[Fact]
	public void AMaterialCanBeCreatedFromATexture()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");
		string texturePath = Path.Combine(
			project.AssetsPath,
			"Textures",
			"wood.png");

		Directory.CreateDirectory(
			Path.GetDirectoryName(texturePath)!);
		File.WriteAllBytes(texturePath, [0x89, 0x50, 0x4E, 0x47]);
		project.AssetCatalog.Refresh();

		string? id = project.CreateMaterialFromTexture(
			"Assets/Textures/wood.png");

		Assert.Equal("Assets/Materials/wood Material.material", id);

		Assert.True(project.TryLoadMaterial(
			id!,
			out MaterialAsset material,
			out string? diagnostic));

		Assert.Null(diagnostic);
		Assert.Equal("Assets/Textures/wood.png", material.Texture);

		// White albedo, so the image appears unchanged.
		Assert.Equal(1f, material.Albedo.X, 4);
		Assert.Equal(1f, material.Albedo.Y, 4);
		Assert.Equal(1f, material.Albedo.Z, 4);

		// A second material from the same image is numbered, not written
		// over the first.
		Assert.Equal(
			"Assets/Materials/wood Material 1.material",
			project.CreateMaterialFromTexture(
				"Assets/Textures/wood.png"));
	}

	[Fact]
	public void OnlyATextureCanBecomeATexturedMaterial()
	{
		EditorProject project = EditorProject.Create(_root, "Materials");
		string material = project.CreateMaterial();

		project.AssetCatalog.Refresh();

		Assert.Null(project.CreateMaterialFromTexture(material));
	}

	[Fact]
	public void RenamingATextureRepointsMaterialsThatUseIt()
	{
		EditorProject project = EditorProject.Create(_root, "Rename");
		WriteTextureFile(project, "Assets/Textures/wood.png");

		string materialId = project.CreateMaterialFromTexture(
			"Assets/Textures/wood.png")!;

		Assert.Equal(
			"Assets/Textures/oak.png",
			project.RenameAsset("Assets/Textures/wood.png", "oak"));

		Assert.True(project.TryLoadMaterial(
			materialId,
			out MaterialAsset material,
			out string? diagnostic));

		Assert.Null(diagnostic);
		Assert.Equal("Assets/Textures/oak.png", material.Texture);

		// The rename rewrites the path, not the rest of the material.
		Assert.Equal(1f, material.Albedo.X, 4);
		Assert.Equal(1f, material.Albedo.Y, 4);
		Assert.Equal(1f, material.Albedo.Z, 4);
	}

	[Fact]
	public void AHandEditedFieldSurvivesTheRenameRewrite()
	{
		EditorProject project = EditorProject.Create(_root, "Rename");
		WriteTextureFile(project, "Assets/Textures/wood.png");

		string materialId = project.CreateMaterialFromTexture(
			"Assets/Textures/wood.png")!;

		// A field this build does not understand must survive: the file
		// is patched, never re-serialized from known values.
		string path = project.ResolvePath(materialId);
		File.WriteAllText(
			path,
			"""
			{
			  "albedo": "#FFFFFF",
			  "texture": "Assets/Textures/wood.png",
			  "note": "hand edited"
			}
			""");

		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
		project.AssetCatalog.Refresh();

		project.RenameAsset("Assets/Textures/wood.png", "oak");

		string rewritten = File.ReadAllText(path);

		Assert.Contains("hand edited", rewritten);
		Assert.Contains("Assets/Textures/oak.png", rewritten);
		Assert.DoesNotContain("Assets/Textures/wood.png", rewritten);
	}

	[Fact]
	public void MaterialsUsingOtherImagesAreLeftAlone()
	{
		EditorProject project = EditorProject.Create(_root, "Rename");
		WriteTextureFile(project, "Assets/Textures/wood.png");
		WriteTextureFile(project, "Assets/Textures/stone.png");

		string stone = project.CreateMaterialFromTexture(
			"Assets/Textures/stone.png")!;

		project.RenameAsset("Assets/Textures/wood.png", "oak");

		Assert.True(project.TryLoadMaterial(
			stone,
			out MaterialAsset material,
			out _));

		Assert.Equal("Assets/Textures/stone.png", material.Texture);
	}

	[Fact]
	public void RenamingAMeshDoesNotRewriteMaterials()
	{
		EditorProject project = EditorProject.Create(_root, "Rename");
		WriteTextureFile(project, "Assets/Textures/wood.png");

		string materialId = project.CreateMaterialFromTexture(
			"Assets/Textures/wood.png")!;

		string meshPath = Path.Combine(project.AssetsPath, "Meshes", "quad.obj");
		Directory.CreateDirectory(Path.GetDirectoryName(meshPath)!);
		File.WriteAllText(
			meshPath,
			"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");

		project.AssetCatalog.Refresh();
		project.RenameAsset("Assets/Meshes/quad.obj", "plane");

		Assert.True(project.TryLoadMaterial(
			materialId,
			out MaterialAsset material,
			out _));

		Assert.Equal("Assets/Textures/wood.png", material.Texture);
	}

	private static void WriteTextureFile(
		EditorProject project,
		string relativePath)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(
			Path.GetDirectoryName(path) ?? project.RootPath);

		File.WriteAllBytes(
			path,
			[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		project.AssetCatalog.Refresh();
	}
}
