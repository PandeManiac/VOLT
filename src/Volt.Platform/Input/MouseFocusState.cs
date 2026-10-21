namespace Volt.Platform.Input;

/// <summary>
/// Describes which window owns the mouse pointer for one input frame.
/// </summary>
/// <remarks>
/// <para>
/// The three states are deliberately distinct. A null pointer focus
/// means no operating-system window owns the pointer, for example
/// while it is outside every application window; a click that cannot
/// be attributed to a window this process owns is not the same as a
/// click on a different VOLT window.
/// </para>
/// <para>
/// This value is informational. The platform never captures, warps,
/// or re-centers the pointer based on it.
/// </para>
/// </remarks>
public enum MouseFocusState
{
	/// <summary>
	/// No native window owns the pointer, so the pointer is not over
	/// any window the process can attribute the input to.
	/// </summary>
	None = 0,

	/// <summary>
	/// The pointer is over the window that produced this snapshot.
	/// </summary>
	OwnWindow = 1,

	/// <summary>
	/// The pointer is over a different native window owned by this
	/// process. It can be another VOLT window or a native window that
	/// is not registered with VOLT; the input never leaks between
	/// them.
	/// </summary>
	OtherWindow = 2
}
