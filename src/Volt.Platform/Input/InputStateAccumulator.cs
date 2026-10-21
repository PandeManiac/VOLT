using System.Numerics;

namespace Volt.Platform.Input;

/// <summary>
/// Accumulates one frame of semantic input transitions.
/// </summary>
/// <remarks>
/// <para>
/// The accumulator is the testable core of the input subsystem: it
/// knows nothing about SDL, native events, or windows, and every
/// mutation is a value-type operation on fixed-size state. The native
/// adapter translates events into these calls, and the window freezes
/// the result into an <see cref="InputSnapshot"/> when the frame ends.
/// </para>
/// <para>
/// Press and release sets describe transitions within the current
/// frame. The held set describes the state at the end of the frame,
/// so a key that went down and up in one frame keeps both edges while
/// reporting that it is no longer down.
/// </para>
/// <para>
/// All state lives in this struct: there are no arrays, dictionaries,
/// delegates, or managed references, so accumulating a frame does not
/// allocate.
/// </para>
/// </remarks>
internal struct InputStateAccumulator
{
	/// <summary>
	/// Number of supported mouse buttons.
	/// </summary>
	internal const int MouseButtonCount = 5;

	/// <summary>
	/// Gets the keys held at the end of the frame.
	/// </summary>
	internal InputBitset KeysDown { get; private set; }

	/// <summary>
	/// Gets the keys that went down during the frame.
	/// </summary>
	internal InputBitset KeysPressed { get; private set; }

	/// <summary>
	/// Gets the keys that went up during the frame.
	/// </summary>
	internal InputBitset KeysReleased { get; private set; }

	/// <summary>
	/// Gets the mouse buttons held at the end of the frame.
	/// </summary>
	internal byte MouseButtonsDown { get; private set; }

	/// <summary>
	/// Gets the mouse buttons that went down during the frame.
	/// </summary>
	internal byte MouseButtonsPressed { get; private set; }

	/// <summary>
	/// Gets the mouse buttons that went up during the frame.
	/// </summary>
	internal byte MouseButtonsReleased { get; private set; }

	/// <summary>
	/// Gets the last known pointer position in logical coordinates.
	/// </summary>
	internal Vector2 MousePosition { get; private set; }

	/// <summary>
	/// Gets the pointer movement accumulated during the frame.
	/// </summary>
	internal Vector2 MouseMotion { get; private set; }

	/// <summary>
	/// Gets the wheel movement accumulated during the frame.
	/// </summary>
	internal Vector2 WheelDelta { get; private set; }

	/// <summary>
	/// Gets whether the window lost keyboard focus during the frame.
	/// </summary>
	/// <remarks>
	/// The flag describes the whole frame, not its final state, so a
	/// window that lost and regained focus inside one poll is still
	/// reported as having been unfocused. A completed frame with this
	/// flag set carries no held input, no edges, and no motion.
	/// </remarks>
	internal bool FocusLostDuringFrame { get; private set; }

	/// <summary>
	/// Starts a new frame: clears the one-shot edges and the
	/// accumulated motion while keeping held state and position.
	/// </summary>
	internal void BeginFrame()
	{
		FocusLostDuringFrame = false;
		KeysPressed = InputBitset.Empty;
		KeysReleased = InputBitset.Empty;
		MouseButtonsPressed = 0;
		MouseButtonsReleased = 0;
		MouseMotion = Vector2.Zero;
		WheelDelta = Vector2.Zero;
	}

	/// <summary>
	/// Applies one key transition.
	/// </summary>
	/// <param name="key">The key that changed.</param>
	/// <param name="down">Whether the key is now down.</param>
	/// <param name="repeat">
	/// Whether the native event is an auto-repeat. Repeats are ignored
	/// entirely: they neither hold a key down that was cleared nor
	/// create a second press edge.
	/// </param>
	internal void ApplyKey(KeyCode key, bool down, bool repeat)
	{
		if (repeat)
		{
			return;
		}

		int index = (int)key;

		if ((uint)index >= InputBitset.Capacity || key == KeyCode.Unknown)
		{
			return;
		}

		bool wasDown = KeysDown.Contains(index);

		if (down)
		{
			if (wasDown)
			{
				return;
			}

			KeysDown = KeysDown.With(index);
			KeysPressed = KeysPressed.With(index);
			return;
		}

		if (!wasDown)
		{
			return;
		}

		KeysDown = KeysDown.Without(index);
		KeysReleased = KeysReleased.With(index);
	}

	/// <summary>
	/// Applies one mouse-button transition.
	/// </summary>
	internal void ApplyMouseButton(MouseButton button, bool down)
	{
		int index = (int)button;

		if ((uint)index >= MouseButtonCount)
		{
			return;
		}

		byte mask = (byte)(1 << index);
		bool wasDown = (MouseButtonsDown & mask) != 0;

		if (down)
		{
			if (wasDown)
			{
				return;
			}

			MouseButtonsDown |= mask;
			MouseButtonsPressed |= mask;
			return;
		}

		if (!wasDown)
		{
			return;
		}

		MouseButtonsDown &= (byte)~mask;
		MouseButtonsReleased |= mask;
	}

	/// <summary>
	/// Records a native motion event: the absolute position replaces
	/// the last known position, and the relative delta accumulates.
	/// </summary>
	internal void ApplyMouseMotion(Vector2 position, Vector2 delta)
	{
		MousePosition = position;
		MouseMotion += delta;
	}

	/// <summary>
	/// Accumulates one wheel event.
	/// </summary>
	internal void ApplyMouseWheel(Vector2 delta)
	{
		WheelDelta += delta;
	}

	/// <summary>
	/// Records that the window lost keyboard focus during this frame.
	/// </summary>
	/// <remarks>
	/// Everything accumulated so far is dropped, and the flag makes the
	/// frame unsafe even if focus is regained before it ends. That is
	/// what keeps a press delivered during the unfocused interval from
	/// becoming a gameplay edge once the final focus query reports the
	/// window as focused again.
	/// </remarks>
	internal void ApplyFocusLost()
	{
		FocusLostDuringFrame = true;
		DiscardFrameInput();
	}

	/// <summary>
	/// Drops everything the frame accumulated: held keys and buttons,
	/// both edge sets, motion, and the wheel.
	/// </summary>
	/// <remarks>
	/// Dropping transitions silently is what keeps a focus change from
	/// fabricating a gameplay press or release. The last known pointer
	/// position survives because it is a position rather than a
	/// transition.
	/// </remarks>
	internal void DiscardFrameInput()
	{
		KeysDown = InputBitset.Empty;
		KeysPressed = InputBitset.Empty;
		KeysReleased = InputBitset.Empty;
		MouseButtonsDown = 0;
		MouseButtonsPressed = 0;
		MouseButtonsReleased = 0;
		MouseMotion = Vector2.Zero;
		WheelDelta = Vector2.Zero;
	}

	/// <summary>
	/// Freezes the accumulated state into an immutable snapshot.
	/// </summary>
	internal readonly InputSnapshot ToSnapshot(
		bool keyboardFocused,
		MouseFocusState mouseFocus,
		bool relativeMotionActive,
		bool focusLostDuringFrame)
	{
		return new InputSnapshot(
			in this,
			keyboardFocused,
			mouseFocus,
			relativeMotionActive,
			FocusLostDuringFrame);
	}
}
