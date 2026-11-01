using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Validation;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what validation catches in a project that opens but would not
/// draw or run correctly: a missing mesh, an unreadable material, a broken
/// scene, and recorded game code that is not there.
/// </summary>
public sealed class ProjectValidationTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-validation-{Guid.NewGuid():N}");

	public ProjectValidationTests()
	{
		Directory.CreateDirectory(_root);
	}

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
	public void AProjectWithResolvableAssetsIsValid()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		Write(project, "Assets/Meshes/quad.obj",
			"""
			v -1 -1 0
			v 1 -1 0
			v 1 1 0
			v -1 1 0
			f 1 2 3 4
			""");
		Write(project, "Assets/Materials/Red.material",
			"""{ "albedo": "#FF0000" }""");
		project.AssetCatalog.Refresh();

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		MeshRendererData renderer = cube.GetComponent<MeshRendererData>()!;
		renderer.Mesh = "Assets/Meshes/quad.obj";
		renderer.Material = "Assets/Materials/Red.material";

		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.True(
			report.IsValid,
			string.Join(
				Environment.NewLine,
				report.Issues.Select(issue => issue.Message)));

		Assert.Equal(0, report.Errors);
		Assert.True(report.Checks >= 3);
	}

	[Fact]
	public void AMissingMeshIsAnError()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Mesh =
			"Assets/Meshes/missing.obj";
		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.False(report.IsValid);

		ProjectIssue issue = Assert.Single(
			report.Issues,
			candidate => candidate.Severity == ProjectIssueSeverity.Error);

		Assert.Contains("Cube", issue.Location);
		Assert.Contains("Assets/Meshes/missing.obj", issue.Message);
	}

	[Fact]
	public void AnUnreadableMaterialIsAWarning()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Material =
			"Assets/Materials/Missing.material";
		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.True(report.IsValid);
		Assert.Contains(
			report.Issues,
			issue => issue.Severity == ProjectIssueSeverity.Warning &&
				issue.Message.Contains(
					"material could not be loaded",
					StringComparison.Ordinal));
	}

	[Fact]
	public void ATextureOnAMeshWithoutCoordinatesIsAWarning()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		Write(
			project,
			"Assets/Materials/Textured.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/wood.png" }
			""");
		Write(project, "Assets/Textures/wood.png", "not a decodable image");
		project.AssetCatalog.Refresh();

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Material =
			"Assets/Materials/Textured.material";

		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.Contains(
			report.Issues,
			issue => issue.Severity == ProjectIssueSeverity.Warning &&
				issue.Message.Contains(
					"no texture coordinates",
					StringComparison.Ordinal));
	}

	[Fact]
	public void ATextureOnAMeshWithCoordinatesIsNotWarnedAbout()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		Write(
			project,
			"Assets/Meshes/quad.obj",
			"""
			v -1 -1 0
			v 1 -1 0
			v 1 1 0
			v -1 1 0
			vt 0 0
			vt 1 0
			vt 1 1
			vt 0 1
			f 1/1 2/2 3/3 4/4
			""");
		Write(
			project,
			"Assets/Materials/Textured.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/wood.png" }
			""");
		Write(project, "Assets/Textures/wood.png", "not a decodable image");
		project.AssetCatalog.Refresh();

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		MeshRendererData renderer =
			cube.GetComponent<MeshRendererData>()!;

		renderer.Mesh = "Assets/Meshes/quad.obj";
		renderer.Material = "Assets/Materials/Textured.material";

		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.DoesNotContain(
			report.Issues,
			issue => issue.Message.Contains(
				"no texture coordinates",
				StringComparison.Ordinal));
	}

	[Fact]
	public void AMissingMaterialTextureIsAWarning()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		Write(
			project,
			"Assets/Materials/Textured.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/missing.png" }
			""");

		project.AssetCatalog.Refresh();

		SceneEntity cube = session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Material =
			"Assets/Materials/Textured.material";

		session.SaveScene();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		// A missing image must not hide the object: the material still
		// loads and draws in its colour, so this stays a warning.
		Assert.True(report.IsValid);
		Assert.Contains(
			report.Issues,
			issue => issue.Severity == ProjectIssueSeverity.Warning &&
				issue.Message.Contains(
					"material texture could not be loaded",
					StringComparison.Ordinal));
	}

	[Fact]
	public void ABrokenSceneIsReportedWithoutHidingTheOthers()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		Directory.CreateDirectory(project.ScenesPath);
		File.WriteAllText(
			Path.Combine(project.ScenesPath, "Broken.volt.scene.json"),
			"{ not json");
		project.AssetCatalog.Refresh();

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.False(report.IsValid);
		Assert.Contains(
			report.Issues,
			issue => issue.Severity == ProjectIssueSeverity.Error &&
				issue.Location.Contains("Broken"));
	}

	[Fact]
	public void AFolderWithoutAProjectIsAnError()
	{
		ProjectValidationReport report = ProjectValidator.Validate(_root);

		Assert.False(report.IsValid);
		Assert.Contains(
			report.Issues,
			issue => issue.Message.Contains(
				"could not be opened",
				StringComparison.Ordinal));
	}

	[Fact]
	public void RecordedGameCodeThatIsMissingIsAnError()
	{
		using EditorSession session = CreateProject();
		EditorProject project = session.Project!;

		project.AttachGameplayProject("Scripts/Missing.csproj", null);

		ProjectValidationReport report =
			ProjectValidator.Validate(project.RootPath);

		Assert.False(report.IsValid);
		Assert.Contains(
			report.Issues,
			issue => issue.Message.Contains(
				"recorded but its file is missing",
				StringComparison.Ordinal));
	}

	private EditorSession CreateProject()
	{
		EditorSession session = new();

		session.NewProject(Path.Combine(_root, "Game"), "Game");

		return session;
	}

	private static void Write(
		EditorProject project,
		string relativePath,
		string content)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? project.RootPath);
		File.WriteAllText(path, content);
	}
}
