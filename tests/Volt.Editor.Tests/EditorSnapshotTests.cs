using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the headless editor snapshot: the whole shell must lay out,
/// paint, and encode without a window or a GPU.
/// </summary>
public sealed class EditorSnapshotTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-editor-snapshot-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EditorSnapshotTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Snapshot");
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
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void Snapshot_RendersTheWholeEditorIntoAnImage()
	{
		string path = Path.Combine(_root, "editor.png");

		UiRasterImage image = EditorSnapshot.Capture(
			_session,
			path,
			1440,
			900);

		Assert.Equal(1440, image.Width);
		Assert.Equal(900, image.Height);
		Assert.True(File.Exists(path));
		Assert.True(
			new FileInfo(path).Length > 20_000,
			"A real editor frame does not compress to almost nothing.");

		// Chrome, panels, icons, and text all contribute: a frame that
		// failed to lay out would be one flat colour.
		HashSet<uint> colors = [];

		for (int y = 0; y < image.Height; y += 12)
		{
			for (int x = 0; x < image.Width; x += 12)
			{
				UiColor pixel = image.GetPixel(x, y);

				colors.Add(
					((uint)pixel.R << 16) |
					((uint)pixel.G << 8) |
					pixel.B);
			}
		}

		Assert.True(
			colors.Count > 24,
			$"Only {colors.Count} distinct colours were rendered.");
	}

	[Fact]
	public void Snapshot_HonoursTheInterfaceScale()
	{
		UiRasterImage normal = EditorSnapshot.Capture(
			_session,
			Path.Combine(_root, "normal.png"),
			1024,
			720);

		UiRasterImage scaled = EditorSnapshot.Capture(
			_session,
			Path.Combine(_root, "scaled.png"),
			1024,
			720,
			1.5f);

		Assert.Equal(normal.Width, scaled.Width);
		Assert.Equal(normal.Height, scaled.Height);

		int differences = 0;

		for (int y = 0; y < normal.Height; y += 16)
		{
			for (int x = 0; x < normal.Width; x += 16)
			{
				if (normal.GetPixel(x, y) != scaled.GetPixel(x, y))
				{
					differences++;
				}
			}
		}

		Assert.True(
			differences > 100,
			"A larger interface scale must change what is painted.");
	}
}
