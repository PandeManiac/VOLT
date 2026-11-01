using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Volt.Editor.Core.Projects;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what the Inspector reports for a selected material: its colour
/// as authored, whether its image decodes, and how large it is.
/// </summary>
[Collection(GameplayProjectCollection.Name)]
public sealed class MaterialPreviewTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-material-preview-{Guid.NewGuid():N}");

	public MaterialPreviewTests()
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
	public void ATexturedMaterialDescribesItsImage()
	{
		EditorProject project = CreateProject();

		WritePng(project, "Assets/Textures/wood.png", width: 2, height: 2);
		Write(
			project,
			"Assets/Materials/Wood.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/wood.png" }
			""");

		project.AssetCatalog.Refresh();

		MaterialPreview? preview = MaterialPreview.Describe(
			project,
			"Assets/Materials/Wood.material");

		Assert.NotNull(preview);
		Assert.Equal("Wood", preview!.Name);
		Assert.Equal("#FFFFFF", preview.AlbedoHex);
		Assert.Contains("wood.png", preview.TextureLine);
		Assert.Contains("2 × 2", preview.TextureLine);
	}

	[Fact]
	public void AFlatMaterialSaysItHasNoTexture()
	{
		EditorProject project = CreateProject();

		Write(
			project,
			"Assets/Materials/Red.material",
			"""{ "albedo": "#FF8000" }""");

		project.AssetCatalog.Refresh();

		MaterialPreview preview = Assert.IsType<MaterialPreview>(
			MaterialPreview.Describe(
				project,
				"Assets/Materials/Red.material"));

		Assert.Equal("#FF8000", preview.AlbedoHex);
		Assert.Contains("flat colour", preview.TextureLine);
	}

	[Fact]
	public void AMissingImageIsReportedInThePreview()
	{
		EditorProject project = CreateProject();

		Write(
			project,
			"Assets/Materials/Broken.material",
			"""
			{ "albedo": "#FFFFFF", "texture": "Assets/Textures/gone.png" }
			""");

		project.AssetCatalog.Refresh();

		MaterialPreview preview = Assert.IsType<MaterialPreview>(
			MaterialPreview.Describe(
				project,
				"Assets/Materials/Broken.material"));

		Assert.StartsWith("Missing:", preview.TextureLine);
	}

	[Fact]
	public void AnUnreadableMaterialStillDescribesItself()
	{
		EditorProject project = CreateProject();

		Write(
			project,
			"Assets/Materials/Bad.material",
			"{ albedo: }");

		project.AssetCatalog.Refresh();

		MaterialPreview preview = Assert.IsType<MaterialPreview>(
			MaterialPreview.Describe(
				project,
				"Assets/Materials/Bad.material"));

		Assert.Contains("Could not be read", preview.TextureLine);
	}

	[Fact]
	public void SomethingThatIsNotAMaterialHasNoPreview()
	{
		EditorProject project = CreateProject();

		Assert.Null(MaterialPreview.Describe(project, null));
		Assert.Null(MaterialPreview.Describe(project, "Assets/Missing.material"));
		Assert.Null(MaterialPreview.Describe(null, "Assets/Materials/Any.material"));
	}

	[Fact]
	public void TheSessionAnnouncesAssetSelectionChanges()
	{
		using EditorSession session = new();
		int changes = 0;

		session.AssetSelectionChanged += _ => changes++;

		session.SelectAsset("Assets/Materials/Wood.material");

		Assert.Equal("Assets/Materials/Wood.material", session.SelectedAssetId);
		Assert.Equal(1, changes);

		// Selecting the same asset again is not a change.
		session.SelectAsset("Assets/Materials/Wood.material");
		Assert.Equal(1, changes);

		session.SelectAsset(null);

		Assert.Null(session.SelectedAssetId);
		Assert.Equal(2, changes);
	}

	private EditorProject CreateProject()
	{
		using EditorSession session = new();

		Assert.True(session.NewProject(
			Path.Combine(_root, "Game"),
			"Game"));

		return session.Project!;
	}

	private static void WritePng(
		EditorProject project,
		string relativePath,
		int width,
		int height)
	{
		using MemoryStream output = new();
		output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(0, 4),
			(uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(4, 4),
			(uint)height);
		header[8] = 8;
		header[9] = 0;

		Chunk(output, "IHDR", header);

		using MemoryStream raw = new();

		for (int row = 0; row < height; row++)
		{
			raw.WriteByte(0);
			raw.Write(new byte[width]);
		}

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
}
