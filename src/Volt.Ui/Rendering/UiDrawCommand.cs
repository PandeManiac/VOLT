using Volt.Ui.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// Identifies the geometry a draw command produces.
/// </summary>
public enum UiDrawCommandKind
{
	Rectangle = 0,
	RoundedRectangle = 1,
	Border = 2,
	Line = 3,
	Text = 4,
	Image = 5
}

/// <summary>
/// One retained draw operation in paint order.
/// </summary>
public readonly record struct UiDrawCommand(
	UiDrawCommandKind Kind,
	UiRect Rect,
	UiColor Color,
	UiRect Clip,
	float Radius,
	float Thickness,
	UiPoint Start,
	UiPoint End,
	UiTextureId Texture,
	UiRect Uv,
	UiTextLayoutResult? Text,
	UiPoint TextOrigin)
{
	public bool IsEmpty
	{
		get
		{
			return Kind switch
			{
				UiDrawCommandKind.Line => Thickness <= 0f,
				UiDrawCommandKind.Text => Text is null ||
					Text.Glyphs.Length == 0 ||
					Color.A == 0,
				UiDrawCommandKind.Image => Rect.IsEmpty || Color.A == 0,
				_ => Rect.IsEmpty || Color.A == 0
			};
		}
	}
}
