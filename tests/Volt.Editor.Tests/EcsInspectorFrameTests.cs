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
/// Covers the frame ordering that headless captures depend on.
/// </summary>
/// <remarks>
/// The ECS panel rebuilds its rows from a live snapshot whenever the world
/// it shows moves on, and that refresh runs after the frame has laid its
/// children out. Such a frame reserves the rows' space without painting
/// them, so a capture keeps one further layout and draw. Dropping that
/// frame leaves the archetype rows blank in the image, which is what this
/// checks.
/// </remarks>
public sealed class EcsInspectorFrameTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-ecs-frame-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EcsInspectorFrameTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Frame Order");
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
	public void TheCapturedFrame_KeepsTheArchetypeRowsItRebuilt()
	{
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_session.Select(meshId);
		_session.StartPlay();

		DockLayout layout = DockLayout.CreateEditorDefault();
		layout.FindTabs("ecs")!.ActivePanelId = "ecs";

		if (FindVerticalSplit(layout.Root) is { } bottom)
		{
			bottom.Ratio = 0.2f;
		}

		UiRasterImage image = EditorSnapshot.Capture(
			_session,
			Path.Combine(_root, "frame.png"),
			1440,
			900,
			layout: layout,
			ecsPanelState: new EcsInspectorPanelState
			{
				RequiredComponents =
					[ComponentRegistry.Get<PositionComponent>()]
			});

		// The archetype heading and its rows sit at the bottom of the
		// panel, above the pinned pager. When a capture keeps a frame that
		// only reserved their space, this band is empty.
		int ink = Ink(image, top: 760, bottom: 850);

		Assert.True(
			ink > 1000,
			$"The captured frame must keep the archetype rows: only {ink} " +
			$"pixels differ from the panel background in that band.");
	}

	/// <summary>
	/// Finds the split between the scene tabs and the bottom tab group.
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

	/// <summary>
	/// Counts the pixels in a band that differ from the colour that fills
	/// most of it, which is the band's background.
	/// </summary>
	private static int Ink(UiRasterImage image, int top, int bottom)
	{
		Dictionary<UiColor, int> counts = [];

		for (int y = top; y < Math.Min(bottom, image.Height); y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				UiColor pixel = image.GetPixel(x, y);

				counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
			}
		}

		UiColor background = default;
		int most = -1;
		int ink = 0;

		foreach ((UiColor color, int count) in counts)
		{
			if (count > most)
			{
				most = count;
				background = color;
			}
		}

		foreach ((UiColor color, int count) in counts)
		{
			if (!color.Equals(background))
			{
				ink += count;
			}
		}

		return ink;
	}
}
