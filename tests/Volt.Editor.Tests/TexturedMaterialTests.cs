using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
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
/// Covers the textured half of the import path: a material's image and
/// the mesh's corner mapping reach the software renderer together, and
/// the pixels it draws come from the image.
/// </summary>
/// <remarks>
/// The project is scaffolded through the editor, which registers its
/// component types process-wide, so this class joins the gameplay
/// collection like every other test that builds a project.
/// </remarks>
[Collection(GameplayProjectCollection.Name)]
public sealed class TexturedMaterialTests : IDisposable
{
	private const int Width = 200;

	private const int Height = 160;

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-texture-{Guid.NewGuid():N}");

	public TexturedMaterialTests()
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
	public void ATexturedMaterialCarriesItsImageAndCoordinates()
	{
		EditorProject project = CreateTexturedProject();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			"Assets/Materials/textured.material",
			out EditorMeshInfo info,
			out VulkanSceneVertex[]? vertices,
			out ViewportTexture? texture));

		Assert.NotNull(vertices);
		Assert.NotNull(texture);
		Assert.Equal(2, texture!.Value.Image.Width);
		Assert.Equal(2, texture.Value.Image.Height);
		Assert.Equal(vertices!.Length, texture.Value.Coordinates.Length);
		Assert.Equal("quad", info.DisplayName);
	}

	[Fact]
	public void TheRenderedSurfaceTakesItsColourFromTheImage()
	{
		EditorProject project = CreateTexturedProject();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			"Assets/Materials/textured.material",
			out EditorMeshInfo info,
			out VulkanSceneVertex[]? vertices,
			out ViewportTexture? texture));

		UiRasterImage image = Render(
			new ViewportMeshInstance(
				info.Id,
				Matrix4x4.Identity,
				vertices,
				texture));

		UiColor center = image.GetPixel(Width / 2, Height / 2);

		Assert.True(
			center.R > 100 && center.G < 80 && center.B < 80,
			$"Expected a red surface, got ({center.R}, {center.G}, " +
			$"{center.B}).");
	}

	[Fact]
	public void AMaterialWithoutAnImageLeavesTheSurfaceUntextured()
	{
		EditorProject project = CreateTexturedProject();

		Write(
			project,
			"Assets/Materials/plain.material",
			"""{ "albedo": "#FFFFFF" }""");

		project.AssetCatalog.Refresh();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			"Assets/Materials/plain.material",
			out _,
			out VulkanSceneVertex[]? vertices,
			out ViewportTexture? texture));

		Assert.NotNull(vertices);
		Assert.Null(texture);
	}

	[Fact]
	public void AMissingImageLeavesTheSurfaceUntextured()
	{
		EditorProject project = CreateTexturedProject();

		Write(
			project,
			"Assets/Materials/broken.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/missing.png" }
			""");

		project.AssetCatalog.Refresh();

		Assert.True(ProjectMeshResolver.TryResolve(
			project,
			"Assets/Meshes/quad.obj",
			"Assets/Materials/broken.material",
			out _,
			out VulkanSceneVertex[]? vertices,
			out ViewportTexture? texture));

		Assert.NotNull(vertices);
		Assert.Null(texture);
	}

	private EditorProject CreateTexturedProject()
	{
		using EditorSession session = new();

		Assert.True(session.NewProject(
			Path.Combine(_root, "Game"),
			"Game"));

		EditorProject project = Assert.IsType<EditorProject>(session.Project);

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

		WritePng(project, "Assets/Textures/red.png");

		Write(
			project,
			"Assets/Materials/textured.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/red.png" }
			""");

		project.AssetCatalog.Refresh();

		return project;
	}

	private static void WritePng(EditorProject project, string relativePath)
	{
		// A 2x2 RGB image whose every pixel is pure red.
		byte[] row = [255, 0, 0, 255, 0, 0];

		using MemoryStream output = new();
		output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 2u);
		BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 2u);
		header[8] = 8;
		header[9] = 2;

		Chunk(output, "IHDR", header);

		using MemoryStream raw = new();
		raw.WriteByte(0);
		raw.Write(row);
		raw.WriteByte(0);
		raw.Write(row);

		using MemoryStream compressed = new();

		using (ZLibStream deflate = new(
			compressed,
			CompressionLevel.Optimal,
			leaveOpen: true))
		{
			raw.Position = 0;
			raw.CopyTo(deflate);
		}

		Chunk(output, "IDAT", compressed.ToArray());
		Chunk(output, "IEND", []);

		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(
			Path.GetDirectoryName(path) ?? project.RootPath);

		File.WriteAllBytes(path, output.ToArray());
	}

	private static void Chunk(Stream output, string type, byte[] data)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);

		output.Write(length);
		output.Write(Encoding.ASCII.GetBytes(type));
		output.Write(data);
		output.Write(new byte[4]);
	}

	private static void Write(
		EditorProject project,
		string relativePath,
		string content)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(
			Path.GetDirectoryName(path) ?? project.RootPath);

		File.WriteAllText(path, content);
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
			ClearColor = new Vector4(0.1f, 0.11f, 0.12f, 1f)
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
}
