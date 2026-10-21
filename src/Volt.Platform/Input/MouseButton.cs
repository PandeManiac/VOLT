namespace Volt.Platform.Input;

/// <summary>
/// Identifies a mouse button independently of the windowing backend.
/// </summary>
/// <remarks>
/// <para>
/// The values are dense and start at zero, so a frame snapshot can
/// pack all five buttons into one bit field. A cast outside the
/// supported range is inert: it is never reported as down, pressed,
/// or released.
/// </para>
/// <para>
/// Mouse buttons never change relative mouse mode on their own. The
/// application decides when the pointer should be captured; the input
/// snapshot only reports the transitions.
/// </para>
/// </remarks>
public enum MouseButton
{
	Left = 0,
	Right = 1,
	Middle = 2,

	/// <summary>The first extra (side) button.</summary>
	X1 = 3,

	/// <summary>The second extra (side) button.</summary>
	X2 = 4
}
