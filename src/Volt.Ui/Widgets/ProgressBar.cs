using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A determinate progress bar.
/// </summary>
public class ProgressBar : Widget
{
	private float _value;

	public ProgressBar()
	{
		MinHeight = 8f;
	}

	/// <summary>
	/// Gets or sets progress in the range zero to one.
	/// </summary>
	public float Value
	{
		get => _value;
		set
		{
			float next = Math.Clamp(value, 0f, 1f);

			if (_value == next)
			{
				return;
			}

			_value = next;
			InvalidateVisual();
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: 120f,
			8f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect content = GlobalContentBounds;

		if (content.IsEmpty)
		{
			return;
		}

		float height = MathF.Min(content.Height, 10f);
		float y = content.Y + ((content.Height - height) * 0.5f);

		UiRect track = new(content.X, y, content.Width, height);

		drawList.AddRoundedRect(
			track,
			height * 0.5f,
			theme.Palette.WindowBackground);

		float fillWidth = track.Width * _value;

		if (fillWidth > 0f)
		{
			drawList.AddRoundedRect(
				new UiRect(
					track.X,
					track.Y,
					fillWidth,
					track.Height),
				height * 0.5f,
				theme.Palette.Accent);
		}
	}
}
