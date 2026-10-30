using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;

namespace Volt.Editor.Tests;

/// <summary>
/// Compares headless editor captures against committed baseline images.
/// </summary>
/// <remarks>
/// The baselines are ordinary PNG files in
/// <c>tests/Volt.Editor.Tests/Baselines</c>. A comparison allows a small
/// per-channel difference and a fraction of a percent of differing
/// pixels, so a new runtime or a tiny antialiasing change does not fail
/// the build, while a moved panel, a lost label, or a wrong colour does.
/// Set <c>VOLT_UPDATE_SNAPSHOTS=1</c> to rewrite a baseline after an
/// intentional interface change, then review the new image before
/// committing it.
/// </remarks>
public sealed class GoldenSnapshotTests : IDisposable
{
	private const int ChannelTolerance = 8;

	private const double PixelTolerance = 0.002;

	private const string UpdateVariable = "VOLT_UPDATE_SNAPSHOTS";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-golden-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public GoldenSnapshotTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Snapshot"), "Snapshot");
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
	public void DefaultWorkspace_MatchesItsBaseline()
	{
		Verify("editor-default.png", 1440, 900);
	}

	[Fact]
	public void StartScreen_MatchesItsBaseline()
	{
		// The start screen is the interface before any project exists, and
		// its heading and caption are the shapes that painted as solid
		// blocks when the shell kept a second font set, so it gets its own
		// baseline. The default folder is the literal the windowed editor
		// starts with, which keeps the capture independent of HOME.
		using EditorSession startSession = new();

		UiRasterImage actual = EditorSnapshot.Capture(
			startSession,
			Path.Combine(_root, "editor-start.png"),
			1440,
			900,
			defaultProjectFolder: "~/VoltGame");

		VerifyImage("editor-start.png", actual);
	}

	[Fact]
	public void AuthoringState_MatchesItsBaseline()
	{
		// The state the editor is actually worked in: a selected mesh, the
		// translate tool, and a populated inspector. The other baselines
		// capture the default workspace and a rotate/snap variant, neither
		// of which draws gizmo handles on a selection.
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_session.Select(meshId);
		_session.Tool = EditorTool.Translate;

		Verify("editor-authoring.png", 1440, 900);
	}

	[Fact]
	public void PlayModeEcsPanel_MatchesItsBaseline()
	{
		// The ECS panel reports the world Play Mode built, so this baseline
		// covers a running session: the cube selected, a remembered Position
		// query, and a taller bottom dock that leaves room for the session
		// summary, the selected runtime entity, the query rows, and the key
		// hints in one image.
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_session.Select(meshId);
		_session.StartPlay();

		DockLayout layout = DockLayout.CreateEditorDefault();
		layout.FindTabs("ecs")!.ActivePanelId = "ecs";

		DockSplitNode? bottom = FindVerticalSplit(layout.Root);

		if (bottom is not null)
		{
			bottom.Ratio = 0.2f;
		}

		UiRasterImage actual = EditorSnapshot.Capture(
			_session,
			Path.Combine(_root, "editor-ecs.png"),
			1440,
			900,
			layout: layout,
			ecsPanelState: new EcsInspectorPanelState
			{
				RequiredComponents =
					[ComponentRegistry.Get<PositionComponent>()]
			});

		VerifyImage("editor-ecs.png", actual);
	}

	[Fact]
	public void RotateWorldSnapState_MatchesItsBaseline()
	{
		_session.Tool = EditorTool.Rotate;
		_session.LocalTransform = false;
		_session.GridSnapping = true;

		Verify("editor-rotate-world-snap.png", 1280, 800);
	}

	private void Verify(string name, int width, int height)
	{
		UiRasterImage actual = EditorSnapshot.Capture(
			_session,
			Path.Combine(_root, name),
			width,
			height);

		VerifyImage(name, actual);
	}

	private static void VerifyImage(string name, UiRasterImage actual)
	{
		Assert.True(
			CountColors(actual) > 24,
			"The capture must contain real interface content.");

		string directory = BaselineDirectory();
		string baselinePath = Path.Combine(directory, name);

		if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
		{
			Directory.CreateDirectory(directory);
			actual.SavePng(baselinePath);
			return;
		}

		Assert.True(
			File.Exists(baselinePath),
			$"Missing baseline '{baselinePath}'. Run with " +
			$"{UpdateVariable}=1 to write it.");

		UiRasterImage expected = UiRasterImage.DecodePng(
			File.ReadAllBytes(baselinePath));

		Assert.Equal(expected.Width, actual.Width);
		Assert.Equal(expected.Height, actual.Height);

		int differing = 0;
		int worst = 0;

		for (int y = 0; y < actual.Height; y++)
		{
			for (int x = 0; x < actual.Width; x++)
			{
				UiColor right = actual.GetPixel(x, y);
				UiColor left = expected.GetPixel(x, y);

				int delta = Math.Max(
					Math.Abs(right.R - left.R),
					Math.Max(
						Math.Abs(right.G - left.G),
						Math.Max(
							Math.Abs(right.B - left.B),
							Math.Abs(right.A - left.A))));

				worst = Math.Max(worst, delta);

				if (delta > ChannelTolerance)
				{
					differing++;
				}
			}
		}

		if (differing == 0)
		{
			return;
		}

		string failureDirectory = Path.Combine(
			AppContext.BaseDirectory,
			"snapshot-failures");

		Directory.CreateDirectory(failureDirectory);

		string failurePath = Path.Combine(failureDirectory, name);
		actual.SavePng(failurePath);

		double ratio = differing / (double)(actual.Width * actual.Height);

		Assert.True(
			ratio <= PixelTolerance,
			$"{name}: {differing} of {actual.Width * actual.Height} " +
			$"pixels ({ratio:P2}) differ by more than {ChannelTolerance}; " +
			$"worst channel delta {worst}. The capture was written to " +
			$"{failurePath}.");
	}

	/// <summary>
	/// Finds the vertical split, which is the one between the scene tabs and
	/// the bottom tab group, so a baseline can give the bottom panels more
	/// room.
	/// </summary>
	private static DockSplitNode? FindVerticalSplit(DockNode? node)
	{
		if (node is not DockSplitNode split)
		{
			return null;
		}

		if (split.Orientation == UiOrientation.Vertical)
		{
			return split;
		}

		return FindVerticalSplit(split.First) ??
			FindVerticalSplit(split.Second);
	}

	private static int CountColors(UiRasterImage image)
	{
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

		return colors.Count;
	}

	/// <summary>
	/// Finds the baseline folder in the source tree, so a capture taken
	/// here and a baseline read by a test are the same file.
	/// </summary>
	private static string BaselineDirectory()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (Directory.Exists(
				Path.Combine(directory.FullName, "src", "Volt.Ui")))
			{
				return Path.Combine(
					directory.FullName,
					"tests",
					"Volt.Editor.Tests",
					"Baselines");
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			"Could not locate the repository root from the test output folder.");
	}
}
