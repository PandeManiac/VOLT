using System.Numerics;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Styling;

/// <summary>
/// Covers the design-system pieces the editor chrome is built from:
/// interface scaling, the vector icon vocabulary, the tile grid, the
/// empty state, and the axis-tinted vector editor.
/// </summary>
public sealed class DesignSystemTests
{
	[Fact]
	public void UiScale_ScalesMetricsAndRasterizesLargerText()
	{
		UiTheme baseline = UiTheme.CreateDark();
		UiTheme scaled = baseline.WithUiScale(1.5f);

		// Control sizes and spacing scale with the interface.
		Assert.Equal(
			baseline.Metrics.RowHeight * 1.5f,
			scaled.Metrics.RowHeight,
			3);

		Assert.Equal(
			baseline.Metrics.IconSize * 1.5f,
			scaled.Metrics.IconSize,
			3);

		// The logical size stays the same and the rasterization scale
		// grows, which is what keeps enlarged text sharp.
		Assert.Equal(baseline.Fonts.Body.Size, scaled.Fonts.Body.Size, 3);
		Assert.Equal(
			baseline.Fonts.Body.DpiScale * 1.5f,
			scaled.Fonts.Body.DpiScale,
			3);

		// Logical metrics stay scale-invariant; the rasterized pixel
		// size is what grows, which is why enlarged text stays sharp
		// rather than being magnified from a small atlas.
		Assert.True(
			scaled.Fonts.Body.Size * scaled.Fonts.Body.DpiScale >
				baseline.Fonts.Body.Size * baseline.Fonts.Body.DpiScale,
			"Scaled text must rasterize at more physical pixels.");

		Assert.Equal(
			baseline.Fonts.Body.MeasureLine("VOLT Editor"),
			scaled.Fonts.Body.MeasureLine("VOLT Editor"),
			3);

		// Every role exists at the new scale, including the new ones.
		Assert.Equal(
			baseline.Fonts.Enumerate().Count(),
			scaled.Fonts.Enumerate().Count());

		Assert.Contains(scaled.Fonts.Title, scaled.Fonts.Enumerate());
		Assert.Contains(scaled.Fonts.PanelHeader, scaled.Fonts.Enumerate());
	}

	[Fact]
	public void IconVocabulary_PaintsInsideItsBounds()
	{
		// Every icon in the vocabulary is drawn from geometry into the
		// draw list. A shape that escapes its own box would bleed into
		// neighbouring rows, so containment is asserted per command.
		int painted = 0;

		foreach (UiIcon icon in Enum.GetValues<UiIcon>())
		{
			if (icon == UiIcon.None)
			{
				continue;
			}

			UiDrawList drawList = new();
			drawList.Begin(new UiSize(64f, 64f));

			UiRect bounds = new(8f, 8f, 32f, 32f);

			UiIconPainter.Draw(
				drawList,
				icon,
				bounds,
				UiColor.White,
				1.5f);

			Assert.True(
				drawList.Count > 0,
				$"Icon '{icon}' painted nothing.");

			for (int index = 0; index < drawList.Count; index++)
			{
				UiDrawCommand command = drawList.GetCommand(index);

				if (command.Kind == UiDrawCommandKind.Line)
				{
					continue;
				}

				Assert.True(
					command.Rect.X >= bounds.X - 0.5f &&
					command.Rect.Right <= bounds.Right + 0.5f &&
					command.Rect.Y >= bounds.Y - 0.5f &&
					command.Rect.Bottom <= bounds.Bottom + 0.5f,
					$"Icon '{icon}' painted " +
					$"({command.Rect.X:0.##},{command.Rect.Y:0.##} " +
					$"{command.Rect.Width:0.##}x{command.Rect.Height:0.##}) " +
					$"outside {bounds.X:0.##},{bounds.Y:0.##} " +
					$"{bounds.Width:0.##}x{bounds.Height:0.##}.");
			}

			painted++;
		}

		Assert.True(painted > 40, $"Expected a full vocabulary, got {painted}.");
	}

	[Fact]
	public void TileWrapPanel_WrapsTilesIntoRowsInsideItself()
	{
		using UiTestHost host = new(320f, 600f);

		TileWrapPanel panel = new();

		for (int index = 0; index < 7; index++)
		{
			panel.Add(new IconTileView(UiIcon.Cube, $"Tile {index}"));
		}

		host.Add(panel);
		host.Layout();

		UiTheme theme = host.Context.Theme;
		float tile = theme.Metrics.TileWidth;
		float spacing = theme.Metrics.TightSpacing;
		int expectedColumns = Math.Max(
			1,
			(int)((320f + spacing) / (tile + spacing)));

		Assert.Equal(expectedColumns, panel.Columns);
		Assert.Equal(3, panel.Columns);

		UiRect bounds = panel.GlobalVisualBounds;
		int row = -1;

		foreach (Widget child in panel.Children)
		{
			UiRect childBounds = child.GlobalVisualBounds;

			Assert.True(
				childBounds.X >= bounds.X - 0.5f &&
				childBounds.Right <= bounds.Right + 0.5f,
				"Tiles must wrap inside the panel width.");

			Assert.True(
				childBounds.Bottom <= bounds.Bottom + 0.5f,
				"Tiles must fit inside the measured panel height.");

			row = Math.Max(row, (int)childBounds.Y);
		}

		// Three rows of three, three, and one.
		Assert.Equal(3, panel.Columns);
		Assert.True(
			panel.DesiredSize.Height > theme.Metrics.TileHeight * 2f,
			"Seven tiles must occupy more than two rows.");
	}

	[Fact]
	public void EmptyStateView_PaintsItsTextAndIgnoresInput()
	{
		using UiTestHost host = new(400f, 300f);

		EmptyStateView view = new(
			UiIcon.Inspector,
			"Nothing selected",
			"Select an object to edit its components.");

		host.Add(view);
		host.Layout();

		Assert.False(view.IsHitTestVisible);

		UiDrawList drawList = host.Context.Draw();
		int text = 0;

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind == UiDrawCommandKind.Text &&
				!command.Rect.Intersect(view.GlobalVisualBounds).IsEmpty)
			{
				text++;
			}
		}

		Assert.True(text >= 2, "The title and description must both paint.");

		Assert.Null(
			host.Context.FindWidgetAt(view.GlobalVisualBounds.Center));
	}

	[Fact]
	public void Vector3Field_LabelsEachAxisAndKeepsThemAligned()
	{
		using UiTestHost host = new(320f, 120f);

		Vector3Field field = new()
		{
			Height = UiLength.Points(26f)
		};

		host.Add(field);
		field.SetValue(new Vector3(1f, 2f, 3f), notify: false);
		host.Layout();

		List<Widget> descendants = [];
		Collect(field, descendants);

		Label[] labels = descendants.OfType<Label>().ToArray();
		NumericField[] fields = descendants.OfType<NumericField>().ToArray();

		Assert.Equal(3, fields.Length);
		Assert.Contains(labels, label => label.Text == "X");
		Assert.Contains(labels, label => label.Text == "Y");
		Assert.Contains(labels, label => label.Text == "Z");

		Assert.Equal(1f, fields[0].Value);
		Assert.Equal(2f, fields[1].Value);
		Assert.Equal(3f, fields[2].Value);

		// Axis colors are distinct so a row is readable at a glance.
		Assert.NotEqual(UiAxisColors.X, UiAxisColors.Y);
		Assert.NotEqual(UiAxisColors.Y, UiAxisColors.Z);
		Assert.NotEqual(UiAxisColors.X, UiAxisColors.Z);
	}

	private static void Collect(Widget widget, List<Widget> destination)
	{
		foreach (Widget child in widget.Children)
		{
			destination.Add(child);
			Collect(child, destination);
		}
	}
}
