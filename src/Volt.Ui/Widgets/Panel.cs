using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A rectangular surface that fills the space it is given and hosts
/// children in an overlay arrangement.
/// </summary>
/// <remarks>
/// A panel is the basic structural container for editor panes,
/// toolbars, and dialogs. It clips descendants by default so content
/// cannot paint over neighboring panes. Set
/// <see cref="Widget.ClipToBounds"/> to false for a non-clipping
/// layout surface.
/// </remarks>
public class Panel : Widget
{
	public Panel()
	{
		ClipToBounds = true;
	}

	/// <summary>
	/// Gets or sets whether the themed panel background is painted.
	/// </summary>
	public bool DrawBackground { get; set; } = true;

	/// <summary>
	/// Gets or sets whether a themed one-pixel border is painted.
	/// </summary>
	public bool DrawBorder { get; set; }

	public UiColor? Background { get; set; }

	public UiColor? BorderColor { get; set; }

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		float width = float.IsFinite(contentAvailable.Width)
			? contentAvailable.Width
			: 0f;

		float height = float.IsFinite(contentAvailable.Height)
			? contentAvailable.Height
			: 0f;

		// A panel fills the space it is given, but it still has to
		// measure its descendants. Otherwise a nested container would
		// be arranged before its children ever computed a desired
		// size.
		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!child.IsVisible)
			{
				continue;
			}

			UiSize desired = MeasureChild(child, contentAvailable);

			if (float.IsInfinity(contentAvailable.Width))
			{
				width = MathF.Max(width, desired.Width);
			}

			if (float.IsInfinity(contentAvailable.Height))
			{
				height = MathF.Max(height, desired.Height);
			}
		}

		return new UiSize(width, height);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		if (DrawBackground)
		{
			UiColor background = Background ??
				theme.GetPanelStyle().Background;

			drawList.AddRect(GlobalBounds, background);
		}

		if (DrawBorder)
		{
			UiColor border = BorderColor ?? theme.Palette.Border;

			drawList.AddBorder(
				GlobalBounds,
				border,
				theme.Metrics.BorderWidth);
		}
	}
}
