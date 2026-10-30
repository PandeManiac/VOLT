using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the frame ordering that a headless capture depends on.
/// </summary>
/// <remarks>
/// The panel rebuilds its rows from a live snapshot during the update
/// phase, which runs after the frame has measured its children. A frame
/// drawn straight after such a rebuild therefore reserves the rows'
/// space without painting them; one further layout and draw paints them.
/// <see cref="Volt.Editor.EditorSnapshot"/> relies on that extra frame, so
/// this pins the behaviour it compensates for.
/// </remarks>
public sealed class EcsInspectorFrameTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-ecs-frame-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EcsInspectorFrameTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Frame Order");
	}

	public void Dispose()
	{
		_context.Dispose();
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
	public void RowsRebuiltDuringUpdate_PaintOnALaterLayoutAndDraw()
	{
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);

		UiSize size = new(1140f, 660f);

		// The shell measures and arranges in BeginFrame, then updates.
		_context.BeginFrame(0.0, size, UiPoint.Zero);
		panel.Tick(0.3, 1f);

		int archetypesTop = ArchetypesTop(panel);

		Assert.True(
			archetypesTop > 0f,
			"The archetype section must be laid out for the rows to have a place.");

		int unmeasured = InkInArchetypeRows(Render(_context.Draw()), archetypesTop);

		// The next frame lays the rebuilt rows out before it draws, which
		// is the frame a capture keeps.
		_context.BeginFrame(0.0, size, UiPoint.Zero);

		int painted = InkInArchetypeRows(Render(_context.Draw()), archetypesTop);

		Assert.True(
			painted > unmeasured,
			$"The rows must paint once a frame measures them: " +
			$"{unmeasured} pixels before, {painted} after.");

		Assert.True(
			painted > 400,
			$"The archetype rows must leave real ink on the frame, " +
			$"but only {painted} pixels differ from the background.");
	}

	/// <summary>
	/// Counts the pixels below the archetype heading that differ from the
	/// most common colour in that band, which is the panel background.
	/// </summary>
	private static int InkInArchetypeRows(UiRasterImage image, int top)
	{
		Dictionary<UiColor, int> counts = [];

		for (int y = top; y < image.Height; y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				UiColor pixel = image.GetPixel(x, y);

				counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
			}
		}

		int ink = 0;

		foreach ((UiColor color, int count) in counts)
		{
			if (!color.Equals(Background(counts)))
			{
				ink += count;
			}
		}

		return ink;
	}

	private static UiColor Background(Dictionary<UiColor, int> counts)
	{
		UiColor background = default;
		int most = -1;

		foreach ((UiColor color, int count) in counts)
		{
			if (count > most)
			{
				most = count;
				background = color;
			}
		}

		return background;
	}

	private static int ArchetypesTop(Widget root)
	{
		if (root is Label { Text: "Archetypes" } heading)
		{
			UiRect bounds = heading.GlobalVisualBounds;

			return (int)MathF.Round(bounds.Y + bounds.Height);
		}

		foreach (Widget child in root.Children)
		{
			int found = ArchetypesTop(child);

			if (found > 0f)
			{
				return found;
			}
		}

		return 0;
	}

	private static UiRasterImage Render(UiDrawList frame)
	{
		return UiRasterizer.Render(
			frame,
			1f,
			UiTheme.CreateDark().Palette.WindowBackground,
			new Dictionary<UiTextureId, UiRasterImage>());
	}
}
