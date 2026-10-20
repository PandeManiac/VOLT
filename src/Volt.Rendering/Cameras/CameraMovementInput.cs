namespace Volt.Rendering.Cameras;

/// <summary>
/// Describes the movement axes requested for one camera update.
/// </summary>
/// <remarks>
/// <para>
/// Sampling input and applying it are deliberately separate: this
/// struct carries only the requested directions, so the movement
/// mathematics can be driven by a keyboard, a script, or a test
/// without any input API.
/// </para>
/// <para>
/// The type is a value type with no collection state, so producing
/// one per frame does not allocate.
/// </para>
/// </remarks>
public readonly record struct CameraMovementInput(
	bool Forward,
	bool Backward,
	bool StrafeLeft,
	bool StrafeRight,
	bool Up,
	bool Down,
	bool Sprint)
{
	/// <summary>
	/// Gets an input with no movement requested.
	/// </summary>
	public static CameraMovementInput None => default;

	/// <summary>
	/// Gets an input that moves forward only.
	/// </summary>
	public static CameraMovementInput ForwardOnly =>
		new(
			Forward: true,
			Backward: false,
			StrafeLeft: false,
			StrafeRight: false,
			Up: false,
			Down: false,
			Sprint: false);

	/// <summary>
	/// Gets whether any movement axis is requested.
	/// </summary>
	public bool HasMovement =>
		Forward ||
		Backward ||
		StrafeLeft ||
		StrafeRight ||
		Up ||
		Down;
}
