namespace Volt.Ui.Input;

/// <summary>
/// A cursor shape requested by the hovered widget.
/// </summary>
/// <remarks>
/// The platform adapter may map these to native cursors. Volt.Ui
/// never calls into a windowing library itself.
/// </remarks>
public enum UiCursor
{
	Arrow = 0,
	IBeam = 1,
	Hand = 2,
	ResizeHorizontal = 3,
	ResizeVertical = 4,
	ResizeAll = 5,
	Crosshair = 6,
	NotAllowed = 7
}
