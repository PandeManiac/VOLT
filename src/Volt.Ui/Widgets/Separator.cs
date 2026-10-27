using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A themed one-pixel divider.
/// </summary>
public sealed class Separator : Widget
{
	public Separator()
		: this(UiOrientation.Horizontal)
	{
	}

	public Separator(UiOrientation orientation)
	{
		Orientation = orientation;

		if (orientation == UiOrientation.Horizontal)
		{
			Height = UiLength.Points(1f);
		}

		else
		{
			Width = UiLength.Points(1f);
		}
	}

	public UiOrientation Orientation { get; }

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		drawList.AddRect(GlobalBounds, theme.Palette.Border);
	}
}
