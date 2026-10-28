namespace Volt.Ui.Input;

/// <summary>
/// Owns the native pointer capture a viewport uses for free-look
/// navigation.
/// </summary>
/// <remarks>
/// <para>
/// The UI framework routes pointer events through its own capture
/// (<see cref="Volt.Ui.UiContext.CapturedWidget"/>), which is enough
/// to keep a drag pointed at the widget that began it. It is not
/// enough for looking around: a cursor that stops at the desktop edge
/// stops the camera with it. This interface is the seam where the
/// application supplies the platform's own relative-pointer mode, so
/// the widget can ask for it without Volt.Ui depending on SDL.
/// </para>
/// <para>
/// Implementations must be honest about the native state: a request
/// that the backend refused is not a capture, and
/// <see cref="IsCaptured"/> must report what the backend actually
/// did rather than what was asked for.
/// </para>
/// <para>
/// Motion is pushed once per frame by the host with
/// <see cref="AccumulateMotion"/> and pulled by the one viewport that
/// is navigating with <see cref="ConsumeMotion"/>. Consuming clears
/// the accumulated value, so a frame's movement can never be applied
/// twice.
/// </para>
/// </remarks>
public interface IUiPointerCapture
{
	/// <summary>
	/// Gets whether the backend has the pointer captured right now.
	/// </summary>
	bool IsCaptured { get; }

	/// <summary>
	/// Asks the backend to capture the pointer.
	/// </summary>
	/// <returns>
	/// True when capture was confirmed. False means navigation must
	/// fall back to ordinary pointer movement: the cursor is visible
	/// and the widget still works, it just cannot turn forever.
	/// </returns>
	bool TryCapture();

	/// <summary>
	/// Asks the backend to release the pointer.
	/// </summary>
	/// <returns>
	/// True when the pointer is confirmed to be free. False means the
	/// request failed and a later call must retry it, because the
	/// cursor must never stay hidden after navigation ends.
	/// </returns>
	bool TryRelease();

	/// <summary>
	/// Drops relative motion the backend already accumulated.
	/// </summary>
	/// <remarks>
	/// Called when capture starts and ends, so motion from before the
	/// transition cannot turn the view on the first frame.
	/// </remarks>
	void DiscardMotion();

	/// <summary>
	/// Adds one frame of native relative motion, in logical units.
	/// </summary>
	/// <remarks>
	/// The host calls this once per completed input frame, whether or
	/// not a viewport is navigating. An implementation that is not
	/// captured ignores the value; that is what keeps ordinary pointer
	/// movement from also steering a captured camera.
	/// </remarks>
	void AccumulateMotion(UiPoint motion);

	/// <summary>
	/// Takes the motion accumulated since the last call.
	/// </summary>
	/// <returns>
	/// The movement in logical units, which is what the UI already
	/// uses for pointer positions. The value is cleared, so it is
	/// applied exactly once.
	/// </returns>
	UiPoint ConsumeMotion();
}
