using SDL3;
using Volt.Platform.Windows;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Samples the sample window's keyboard and mouse state and feeds it
/// to the camera controller.
/// </summary>
/// <remarks>
/// <para>
/// Input is read with SDL's state queries on the main thread, which
/// is the same thread that pumps the existing event queue. No global
/// hook is registered, no other window is touched, and no synthetic
/// events are generated.
/// </para>
/// <para>
/// Relative mouse mode is never enabled automatically. It is entered
/// only by a left click in the sample window while that window owns
/// both keyboard focus and the pointer. SDL reports a null mouse-focus
/// window when no window owns the pointer; such a click cannot be
/// attributed to this window, so it never starts capture. Mouse mode
/// is released by Escape, by losing keyboard focus, by another window
/// taking the pointer, or by disposal.
/// </para>
/// <para>
/// A release is only recorded once SDL confirms the native state, so a
/// failing disable keeps the camera frozen and is retried on the next
/// update and at shutdown instead of silently reporting success.
/// </para>
/// <para>
/// The controller is only updated while the window has focus, so
/// background windows cannot move the camera. Regaining focus starts
/// from the retained orientation and position; the camera does not
/// jump because mouse look resumes only after a fresh click.
/// </para>
/// </remarks>
internal sealed class CameraInput : IDisposable
{
	private readonly Window _window;
	private readonly CameraCaptureController _capture;

	private bool _releaseFailureReported;
	private bool _disposed;

	/// <summary>
	/// Gets whether relative mouse mode is believed to be enabled.
	/// </summary>
	public bool IsCaptured => _capture.IsCaptured;

	public CameraInput(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);
		_window = window;

		_capture = new CameraCaptureController(
			SetRelativeMouseMode,
			IsRelativeMouseModeEnabled,
			DiscardPendingMotion);
	}

	/// <summary>
	/// Samples input and integrates it into the camera.
	/// </summary>
	/// <param name="camera">The camera to update.</param>
	/// <param name="deltaSeconds">
	/// Elapsed time since the previous update, in seconds. The
	/// controller clamps and validates it.
	/// </param>
	public void Update(CameraController camera, float deltaSeconds)
	{
		ArgumentNullException.ThrowIfNull(camera);
		ObjectDisposedException.ThrowIf(_disposed, this);

		IntPtr handle = _window.NativeHandle;

		bool keyboardFocused =
			SDL.GetKeyboardFocus() == handle &&
			(SDL.GetWindowFlags(handle) & SDL.WindowFlags.InputFocus) != 0;

		ReadOnlySpan<bool> keys = SDL.GetKeyboardState(out _);

		CameraInputSample sample = new(
			KeyboardFocused: keyboardFocused,
			PointerFocus: ClassifyPointerFocus(
				SDL.GetMouseFocus(),
				handle),
			LeftButtonDown:
				(SDL.GetMouseState(out _, out _) &
					SDL.MouseButtonFlags.Left) != 0,
			EscapeDown: keyboardFocused &&
				IsDown(keys, SDL.Scancode.Escape));

		CameraCaptureTransition transition = _capture.Update(sample);

		ReportFailures(transition);

		if (_capture.IsLookActive)
		{
			SDL.GetRelativeMouseState(out float lookX, out float lookY);

			if (lookX != 0.0f || lookY != 0.0f)
			{
				camera.ApplyLook(lookX, lookY);
			}
		}

		camera.Move(
			keyboardFocused
				? SampleMovement(keys)
				: CameraMovementInput.None,
			deltaSeconds);
	}

	/// <summary>
	/// Releases relative mouse mode and restores the cursor.
	/// </summary>
	/// <remarks>
	/// Safe to call more than once. A release that SDL does not confirm
	/// is reported on stderr rather than silently treated as success.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		if (!_capture.Close())
		{
			Console.Error.WriteLine(
				"Relative mouse mode could not be disabled during " +
				"disposal; the cursor may remain captured: " +
				$"{SDL.GetError()}");
		}
	}

	private static CameraMovementInput SampleMovement(
		ReadOnlySpan<bool> keys)
	{
		return new CameraMovementInput(
			Forward: IsDown(keys, SDL.Scancode.W),
			Backward: IsDown(keys, SDL.Scancode.S),
			StrafeLeft: IsDown(keys, SDL.Scancode.A),
			StrafeRight: IsDown(keys, SDL.Scancode.D),
			Up: IsDown(keys, SDL.Scancode.Space),
			Down: IsDown(keys, SDL.Scancode.LCtrl),
			Sprint: IsDown(keys, SDL.Scancode.LShift));
	}

	private static bool IsDown(ReadOnlySpan<bool> keys, SDL.Scancode code)
	{
		int index = (int)code;

		return (uint)index < (uint)keys.Length && keys[index];
	}

	private static CameraPointerFocus ClassifyPointerFocus(
		IntPtr mouseFocus,
		IntPtr handle)
	{
		if (mouseFocus == IntPtr.Zero)
		{
			return CameraPointerFocus.None;
		}

		return mouseFocus == handle
			? CameraPointerFocus.OwnWindow
			: CameraPointerFocus.OtherWindow;
	}

	private bool SetRelativeMouseMode(bool enabled)
	{
		return SDL.SetWindowRelativeMouseMode(
			_window.NativeHandle,
			enabled);
	}

	private bool IsRelativeMouseModeEnabled()
	{
		return SDL.GetWindowRelativeMouseMode(_window.NativeHandle);
	}

	private static void DiscardPendingMotion()
	{
		SDL.GetRelativeMouseState(out _, out _);
	}

	private void ReportFailures(CameraCaptureTransition transition)
	{
		if (transition.CaptureFailed)
		{
			Console.Error.WriteLine(
				$"Relative mouse mode could not be enabled: " +
				$"{SDL.GetError()}");
		}

		if (!transition.ReleaseFailed)
		{
			_releaseFailureReported = false;
			return;
		}

		if (_releaseFailureReported)
		{
			return;
		}

		_releaseFailureReported = true;

		Console.Error.WriteLine(
			$"Relative mouse mode could not be disabled; retrying: " +
			$"{SDL.GetError()}");
	}
}
