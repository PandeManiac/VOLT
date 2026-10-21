using System.Numerics;

namespace Volt.Platform.Input;

/// <summary>
/// An immutable snapshot of one window's input state for one frame.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot is a value type with no managed references. Copying it
/// copies the whole state, so a snapshot captured earlier never
/// changes when a later frame is polled. Reading it performs no native
/// call, no boxing, and no heap allocation.
/// </para>
/// <para>
/// <see cref="KeyboardFocused"/> and <see cref="MouseFocus"/> describe
/// the window at the moment the frame completed. An unfocused window
/// reports no held keys, no held mouse buttons, and no press or
/// release edges: held state is cleared when focus is lost, and key or
/// button events that reach an unfocused window are discarded. This
/// means gameplay can trust an edge without checking focus first.
/// </para>
/// <para>
/// The snapshot deliberately does not remember which keys are held
/// across frames; it describes only the completed frame. Call
/// <c>Window.PollEvents</c> once per application frame and read the
/// snapshot afterwards.
/// </para>
/// </remarks>
public readonly struct InputSnapshot
{
	private readonly InputBitset _keysDown;
	private readonly InputBitset _keysPressed;
	private readonly InputBitset _keysReleased;
	private readonly byte _mouseButtonsDown;
	private readonly byte _mouseButtonsPressed;
	private readonly byte _mouseButtonsReleased;
	private readonly bool _keyboardFocused;
	private readonly bool _focusLostDuringFrame;
	private readonly MouseFocusState _mouseFocus;
	private readonly Vector2 _mousePosition;
	private readonly Vector2 _mouseMotion;
	private readonly Vector2 _relativeMotion;
	private readonly Vector2 _wheelDelta;

	internal InputSnapshot(
		in InputStateAccumulator accumulator,
		bool keyboardFocused,
		MouseFocusState mouseFocus,
		bool relativeMotionActive,
		bool focusLostDuringFrame)
	{
		_keysDown = accumulator.KeysDown;
		_keysPressed = accumulator.KeysPressed;
		_keysReleased = accumulator.KeysReleased;
		_mouseButtonsDown = accumulator.MouseButtonsDown;
		_mouseButtonsPressed = accumulator.MouseButtonsPressed;
		_mouseButtonsReleased = accumulator.MouseButtonsReleased;
		_keyboardFocused = keyboardFocused;
		_focusLostDuringFrame = focusLostDuringFrame;
		_mouseFocus = mouseFocus;
		_mousePosition = accumulator.MousePosition;
		_mouseMotion = accumulator.MouseMotion;
		_relativeMotion = relativeMotionActive
			? accumulator.MouseMotion
			: Vector2.Zero;
		_wheelDelta = accumulator.WheelDelta;
	}

	/// <summary>
	/// Gets an all-up snapshot with no focus, no motion, and no wheel.
	/// </summary>
	/// <remarks>
	/// This is the value of <c>Window.Input.Snapshot</c> before the
	/// first poll of a window.
	/// </remarks>
	public static InputSnapshot Empty => default;

	/// <summary>
	/// Gets whether the window had keyboard focus when the frame
	/// completed.
	/// </summary>
	public bool KeyboardFocused => _keyboardFocused;

	/// <summary>
	/// Gets whether the window lost keyboard focus during the frame,
	/// even if it had focus again when the frame completed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The frame describes an interval, and input from the part of it
	/// when the window was unfocused cannot be attributed to the
	/// application. Such a frame is therefore cleared: it reports no
	/// held keys or buttons, no edges, no motion, and no wheel, and
	/// this flag stays true. It is the conservative signal an
	/// application needs to drop any capture or camera state that a
	/// focus loss invalidated, including a loss that was repaired
	/// before the frame was read.
	/// </para>
	/// <para>
	/// The flag is independent of <see cref="KeyboardFocused"/>:
	/// that property reports the state the frame ended in, while this
	/// one reports that focus changed inside the frame.
	/// </para>
	/// </remarks>
	public bool FocusLostDuringFrame => _focusLostDuringFrame;

	/// <summary>
	/// Gets which window owned the mouse pointer when the frame
	/// completed.
	/// </summary>
	public MouseFocusState MouseFocus => _mouseFocus;

	/// <summary>
	/// Gets the pointer position from the most recent mouse-motion
	/// event, in logical window coordinates.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Logical coordinates are the window's client-area units, not
	/// framebuffer pixels: on a high-density display the framebuffer
	/// can be larger, so this value must not be compared with, or used
	/// as, a pixel coordinate.
	/// </para>
	/// <para>
	/// The value is zero until the first motion event arrives and then
	/// holds the last known position across frames without motion. It
	/// is never clamped to the window bounds and never wraps.
	/// </para>
	/// </remarks>
	public Vector2 MousePosition => _mousePosition;

	/// <summary>
	/// Gets the pointer movement accumulated during this frame, in
	/// logical window units.
	/// </summary>
	/// <remarks>
	/// Multiple native motion events in one frame are summed, and the
	/// value resets at the next poll. It is never clamped to the
	/// window bounds, so fast movement across the edge keeps its full
	/// magnitude. When the window is not in relative mouse mode this
	/// is ordinary pointer movement; use <see cref="RelativeMotion"/>
	/// for pointer-locked look control.
	/// </remarks>
	public Vector2 MouseMotion => _mouseMotion;

	/// <summary>
	/// Gets the pointer movement accumulated during this frame while
	/// relative mouse mode was active.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the value an FPS-style camera should consume. It is
	/// zero when relative mouse mode was not enabled at the end of
	/// the frame, so the frame in which capture starts reports no
	/// movement and the first captured frame cannot jump the view. It
	/// resets at the next poll and is never clamped or re-centered by
	/// the input subsystem.
	/// </para>
	/// <para>
	/// The value comes from the same accumulated motion events as
	/// <see cref="MouseMotion"/>; consume one or the other, never both,
	/// or movement is counted twice.
	/// </para>
	/// </remarks>
	public Vector2 RelativeMotion => _relativeMotion;

	/// <summary>
	/// Gets the mouse wheel movement accumulated during this frame.
	/// </summary>
	/// <remarks>
	/// X is horizontal and Y is vertical, in wheel notches with the
	/// conventional positive-up sign. Values from flipped devices are
	/// normalized by the native adapter, and the value resets at the
	/// next poll.
	/// </remarks>
	public Vector2 WheelDelta => _wheelDelta;

	/// <summary>
	/// Gets whether the key is held at the end of the frame.
	/// </summary>
	public bool IsKeyDown(KeyCode key)
	{
		return _keysDown.Contains((int)key);
	}

	/// <summary>
	/// Gets whether the key went from up to down during the frame.
	/// </summary>
	/// <remarks>
	/// A key pressed and released in the same frame reports both
	/// <see cref="WasKeyPressed"/> and <see cref="WasKeyReleased"/>;
	/// <see cref="IsKeyDown"/> reflects the last transition.
	/// </remarks>
	public bool WasKeyPressed(KeyCode key)
	{
		return _keysPressed.Contains((int)key);
	}

	/// <summary>
	/// Gets whether the key went from down to up during the frame.
	/// </summary>
	public bool WasKeyReleased(KeyCode key)
	{
		return _keysReleased.Contains((int)key);
	}

	/// <summary>
	/// Gets whether the mouse button is held at the end of the frame.
	/// </summary>
	public bool IsMouseButtonDown(MouseButton button)
	{
		return Contains(_mouseButtonsDown, button);
	}

	/// <summary>
	/// Gets whether the mouse button went from up to down during the
	/// frame.
	/// </summary>
	/// <remarks>
	/// A held button reports one press edge, not one per frame.
	/// </remarks>
	public bool WasMouseButtonPressed(MouseButton button)
	{
		return Contains(_mouseButtonsPressed, button);
	}

	/// <summary>
	/// Gets whether the mouse button went from down to up during the
	/// frame.
	/// </summary>
	public bool WasMouseButtonReleased(MouseButton button)
	{
		return Contains(_mouseButtonsReleased, button);
	}

	private static bool Contains(byte buttons, MouseButton button)
	{
		int index = (int)button;

		return (uint)index < InputStateAccumulator.MouseButtonCount &&
			(buttons & (1 << index)) != 0;
	}
}
