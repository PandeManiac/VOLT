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
/// Covers the import path end to end: an OBJ file in the project becomes
/// geometry a viewport draws, with bounds picking can use.
/// </summary>
public sealed class ImportedMeshRenderTests : IDisposable
{
	private const int Width = 240;

	private const int Height = 160;

	private static readonly Vector3 ClearColor =
		new(0.1f, 0.11f, 0.12f);

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-imported-mesh-{Guid.NewGuid():N}");

	public ImportedMeshRenderTests()
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
	public void AnImportedMeshIsResolvedAndRasterized()
	{
		using EditorSession session = new();
		session.NewProject(Path.Combine(_root, "Game"), "Game");

		EditorProject project = Assert.IsType<EditorProject>(session.Project);

		WriteQuad(project, "Assets/Meshes/quad.obj", halfSize: 1f);
		project.AssetCatalog.Refresh();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			out EditorMeshInfo info,
			out VulkanSceneVertex[]? vertices));

		Assert.NotNull(vertices);
		Assert.Equal(6, vertices!.Length);
		Assert.Equal(Vector3.Zero, info.Center);
		Assert.Equal(1f, info.HalfExtent.X, 4);
		Assert.Equal("quad", info.DisplayName);

		UiRasterImage image = Render(
			new ViewportMeshInstance(info.Id, Matrix4x4.Identity, vertices));

		Assert.True(
			CountDifferent(image, ClearPixel()) > 1_000,
			"An imported quad must cover a meaningful part of the viewport.");
	}

	[Fact]
	public void AMissingMeshDoesNotResolve()
	{
		using EditorSession session = new();
		session.NewProject(Path.Combine(_root, "Game"), "Game");

		Assert.False(ProjectMeshResolver.TryResolve(
			session.Project,
			"Assets/Meshes/missing.obj",
			out _,
			out VulkanSceneVertex[]? vertices));

		Assert.Null(vertices);
	}

	[Fact]
	public void ABuiltInReferenceCarriesNoProjectGeometry()
	{
		using EditorSession session = new();
		session.NewProject(Path.Combine(_root, "Game"), "Game");

		Assert.True(ProjectMeshResolver.TryResolve(
			session.Project,
			EditorMeshLibrary.Cube,
			out EditorMeshInfo info,
			out VulkanSceneVertex[]? vertices));

		Assert.Equal(EditorMeshLibrary.Cube, info.Id);
		Assert.Null(vertices);
	}

	private static void WriteQuad(
		EditorProject project,
		string relativePath,
		float halfSize)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? project.RootPath);

		File.WriteAllText(
			path,
			$"""
			v {-halfSize} {-halfSize} 0
			v {halfSize} {-halfSize} 0
			v {halfSize} {halfSize} 0
			v {-halfSize} {halfSize} 0
			f 1 2 3 4
			""");
	}

	private static UiRasterImage Render(params ViewportMeshInstance[] instances)
	{
		SoftwareSceneRenderer renderer = new();

		ViewportRenderRequest request = new()
		{
			Kind = ViewportKind.Scene,
			HasContent = true,
			PixelWidth = Width,
			PixelHeight = Height,
			View = Matrix4x4.CreateLookAt(
				new Vector3(0f, 0f, -6f),
				Vector3.Zero,
				Vector3.UnitY),
			Projection = CameraProjection.CreatePerspective(
				new CameraComponent(MathF.PI / 3f, 0.05f, 500f),
				Width / (float)Height,
				ViewportYOrientation.YDown),
			ShowGrid = false,
			ClearColor = new Vector4(
				ClearColor.X,
				ClearColor.Y,
				ClearColor.Z,
				1f)
		};

		request.Instances.AddRange(instances);

		renderer.PublishRequest(request);
		renderer.RenderFrame();

		return renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image)
				? image!
				: throw new InvalidOperationException(
					"The renderer produced no image.");
	}

	private static UiColor ClearPixel()
	{
		return new UiColor(
			(byte)MathF.Round(ClearColor.X * 255f),
			(byte)MathF.Round(ClearColor.Y * 255f),
			(byte)MathF.Round(ClearColor.Z * 255f));
	}

	private static int CountDifferent(
		UiRasterImage image,
		UiColor reference)
	{
		int count = 0;

		for (int y = 0; y < image.Height; y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				if (image.GetPixel(x, y) != reference)
				{
					count++;
				}
			}
		}

		return count;
	}
}
