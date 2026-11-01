using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Editor.Core.Projects;
using Volt.Editor.Rendering;
using Volt.Editor.Viewport;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Rendering.Cameras;
using Volt.Rendering.Components;
using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what an authored material changes: the vertex colours both
/// renderers draw, for a built-in primitive and for an imported mesh, and
/// therefore the pixels in the viewport.
/// </summary>
public sealed class MaterialTintTests : IDisposable
{
	private const string RedMaterial = "Assets/Materials/Red.material";

	private const int Width = 160;

	private const int Height = 120;

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-material-tint-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public MaterialTintTests()
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
	public void AMaterialTintsABuiltInMesh()
	{
		EditorProject project = OpenProjectWithRedMaterial();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			out _,
			out VulkanSceneVertex[]? untinted));

		Assert.Null(untinted);

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			RedMaterial,
			out _,
			out VulkanSceneVertex[]? tinted));

		Assert.NotNull(tinted);
		Assert.All(tinted!, vertex =>
		{
			Assert.True(vertex.Color.X > vertex.Color.Y);
			Assert.True(vertex.Color.X > vertex.Color.Z);
			Assert.True(vertex.Color.X > 0.1f);
		});
	}

	[Fact]
	public void AMaterialTintsAnImportedMesh()
	{
		EditorProject project = OpenProjectWithRedMaterial();
		WriteQuad(project);
		project.AssetCatalog.Refresh();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			out _,
			out VulkanSceneVertex[]? plain));
		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			RedMaterial,
			out _,
			out VulkanSceneVertex[]? tinted));

		Assert.NotNull(plain);
		Assert.NotNull(tinted);
		Assert.Equal(plain!.Length, tinted!.Length);

		for (int index = 0; index < plain.Length; index++)
		{
			Assert.Equal(plain[index].Position, tinted[index].Position);
			Assert.Equal(plain[index].Color.X, tinted[index].Color.X, 4);
			Assert.Equal(0f, tinted[index].Color.Y, 4);
			Assert.Equal(0f, tinted[index].Color.Z, 4);
		}
	}

	[Fact]
	public void AnUnresolvableMaterialLeavesTheMeshUntinted()
	{
		EditorProject project = OpenProjectWithRedMaterial();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			"Assets/Materials/Missing.material",
			out _,
			out VulkanSceneVertex[]? vertices));

		Assert.Null(vertices);
	}

	[Fact]
	public void TheDefaultMaterialChangesNothing()
	{
		EditorProject project = OpenProjectWithRedMaterial();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			"builtin:default",
			out _,
			out VulkanSceneVertex[]? vertices));

		Assert.Null(vertices);
	}

	[Fact]
	public void TheTintReachesTheRasterizedImage()
	{
		EditorProject project = OpenProjectWithRedMaterial();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			out _,
			out VulkanSceneVertex[]? untinted));
		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			EditorMeshLibrary.Cube,
			RedMaterial,
			out _,
			out VulkanSceneVertex[]? tinted));

		UiColor plain = RenderCenter(untinted);
		UiColor red = RenderCenter(tinted);

		Assert.NotEqual(plain, red);
		Assert.True(
			red.R > red.G && red.R > red.B,
			"A red material must make the cube red on screen.");
	}

	private EditorProject OpenProjectWithRedMaterial()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		EditorProject project = Assert.IsType<EditorProject>(_session.Project);

		string path = Path.Combine(
			project.RootPath,
			RedMaterial.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, """{ "albedo": "#FF0000" }""");
		project.AssetCatalog.Refresh();

		return project;
	}

	private static void WriteQuad(EditorProject project)
	{
		string path = Path.Combine(
			project.RootPath,
			"Assets",
			"Meshes",
			"quad.obj");

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		File.WriteAllText(
			path,
			"""
			v -1 -1 0
			v 1 -1 0
			v 1 1 0
			v -1 1 0
			f 1 2 3 4
			""");
	}

	private static UiColor RenderCenter(VulkanSceneVertex[]? vertices)
	{
		SoftwareSceneRenderer renderer = new();

		ViewportRenderRequest request = new()
		{
			Kind = ViewportKind.Scene,
			HasContent = true,
			PixelWidth = Width,
			PixelHeight = Height,
			View = Matrix4x4.CreateLookAt(
				new Vector3(0f, 0f, -4f),
				Vector3.Zero,
				Vector3.UnitY),
			Projection = CameraProjection.CreatePerspective(
				new CameraComponent(MathF.PI / 3f, 0.05f, 500f),
				Width / (float)Height,
				ViewportYOrientation.YDown),
			ShowGrid = false
		};

		request.Instances.Add(new ViewportMeshInstance(
			EditorMeshLibrary.Cube,
			Matrix4x4.Identity,
			vertices));

		renderer.PublishRequest(request);
		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		return image!.GetPixel(Width / 2, Height / 2);
	}
}
