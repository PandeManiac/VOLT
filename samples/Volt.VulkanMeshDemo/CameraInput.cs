using System.Numerics;
using Volt.Platform.Input;
using Volt.Platform.Windows;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Describes the gameplay-relevant events of one sampled input frame.
/// </summary>
/// <remarks>
/// <para>
/// The camera itself has already been updated by the time this value
/// is returned. The snapshot carries only what gameplay needs: whether
/// this window owns the input, the one-shot button edges, and the
/// movement that was requested.
/// </para>
/// <para>
/// Both edges come from the engine's input snapshot. The platform
/// clears held state and suppresses edges when the window is
/// unfocused, so holding a key or button in another application and
/// then focusing this one cannot fabricate a press.
/// </para>
/// <para>
/// The type is a value type with no managed references, so returning
/// it per frame allocates nothing.
/// </para>
/// </remarks>
internal readonly record struct CameraInputSnapshot(
	bool KeyboardFocused,
	bool FireRequested,
	bool RestartRequested,
	CameraMovementInput Movement);

/// <summary>
/// Maps the engine's per-window input snapshot onto the sample's
/// camera and target-range commands.
/// </summary>
/// <remarks>
/// <para>
/// The platform samples the keyboard, mouse, and focus state; this
/// adapter only translates it into <see cref="CameraMovementInput"/>
/// and the one-shot edges gameplay consumes. It contains no SDL call,
/// no state query, and no edge tracking of its own.
/// </para>
/// <para>
/// Relative mouse mode is never enabled automatically. It is entered
/// only by a left click in the sample window while that window owns
/// both keyboard focus and the pointer. SDL reports a null
/// mouse-focus window when no window owns the pointer; such a click
/// cannot be attributed to this window, so it never starts capture.
/// Mouse mode is released by Escape, by losing keyboard focus, by
/// another window taking the pointer, or by disposal. A focus loss
/// releases even when focus returns before the frame is read.
/// </para>
/// <para>
/// <see cref="CameraCaptureController"/> remains the single owner of
/// capture policy and of the left-button edge decision: the platform
/// supplies its press edge, and the controller decides whether that
/// press captures or fires. Gameplay never reads a button edge
/// directly, so the capture click cannot also fire. Passing the edge
/// rather than the held state is also what preserves a short click
/// that begins and ends between two polls.
/// </para>
/// <para>
/// A frame in which the window lost keyboard focus is cleared by the
/// platform and additionally releases capture through
/// <see cref="InputSnapshot.FocusLostDuringFrame"/>, so a focus loss
/// that was repaired before the frame was read cannot leave the
/// pointer captured or rotate the camera.
/// </para>
/// <para>
/// A release is only recorded once the native state is confirmed, so a
/// failing disable keeps the camera frozen and is retried on the next
/// update and at shutdown instead of silently reporting success.
/// </para>
/// <para>
/// Camera look consumes <see cref="InputSnapshot.RelativeMotion"/>,
/// which is nonzero only while relative mouse mode was enabled at the
/// end of the frame. The frame that starts capture therefore reports
/// no relative motion, so the first captured frame cannot jump the
/// view. The camera is only moved while the window has keyboard
/// focus, and regaining focus starts from the retained pose because
/// mouse look resumes only after a fresh click.
/// </para>
/// </remarks>
internal sealed class CameraInput : IDisposable
{
	private readonly WindowInput _input;
	private readonly CameraCaptureController _capture;

	private bool _releaseFailureReported;
	private bool _disposed;

	/// <summary>
	/// Gets whether relative mouse mode is believed to be enabled.
	/// </summary>
	public bool IsCaptured => _capture.IsCaptured;

	public CameraInput(Window window)
		: this(window.Input)
	{
	}

	/// <summary>
	/// Creates an adapter over an existing engine input.
	/// </summary>
	/// <remarks>
	/// The window owns the input, so this adapter never disposes it.
	/// The overload exists so the mapping and capture policy can be
	/// exercised with a synthetic input that needs no native window.
	/// </remarks>
	internal CameraInput(WindowInput input)
	{
		ArgumentNullException.ThrowIfNull(input);

		_input = input;

		_capture = new CameraCaptureController(
			input.TrySetRelativeMouseMode,
			() => input.IsRelativeMouseModeEnabled,
			input.DiscardRelativeMotion);
	}

	/// <summary>
	/// Reads the completed input frame, integrates it into the camera,
	/// and reports the frame's gameplay edges.
	/// </summary>
	/// <param name="camera">The camera to update.</param>
	/// <param name="deltaSeconds">
	/// Elapsed time since the previous update, in seconds. The
	/// controller clamps and validates it.
	/// </param>
	/// <returns>
	/// The frame's focus state, one-shot fire and restart edges, and
	/// the movement that was applied.
	/// </returns>
	/// <remarks>
	/// Read once per polled frame: a later poll starts a new frame and
	/// replaces the snapshot.
	/// </remarks>
	public CameraInputSnapshot Update(
		CameraController camera,
		float deltaSeconds)
	{
		ArgumentNullException.ThrowIfNull(camera);
		ObjectDisposedException.ThrowIf(_disposed, this);

		InputSnapshot snapshot = _input.Snapshot;

		CameraInputSample sample = new(
			KeyboardFocused: snapshot.KeyboardFocused,
			PointerFocus: ToPointerFocus(snapshot.MouseFocus),
			LeftButtonPressed:
				snapshot.WasMouseButtonPressed(MouseButton.Left),
			EscapeDown: snapshot.IsKeyDown(KeyCode.Escape),
			FocusLostDuringFrame: snapshot.FocusLostDuringFrame);

		CameraCaptureTransition transition = _capture.Update(sample);

		ReportFailures(transition);

		if (_capture.IsLookActive)
		{
			Vector2 look = snapshot.RelativeMotion;

			if (look.X != 0.0f || look.Y != 0.0f)
			{
				camera.ApplyLook(look.X, look.Y);
			}
		}

		CameraMovementInput movement = snapshot.KeyboardFocused
			? SampleMovement(snapshot)
			: CameraMovementInput.None;

		camera.Move(movement, deltaSeconds);

		return new CameraInputSnapshot(
			KeyboardFocused: snapshot.KeyboardFocused,
			FireRequested: transition.FireRequested,
			RestartRequested: snapshot.WasKeyPressed(KeyCode.R),
			Movement: movement);
	}

	/// <summary>
	/// Releases relative mouse mode and restores the cursor.
	/// </summary>
	/// <remarks>
	/// Safe to call more than once. A release that the windowing
	/// backend does not confirm is reported on stderr rather than
	/// silently treated as success. The engine input belongs to the
	/// window and is disposed with it.
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
				"disposal; the cursor may remain captured.");
		}
	}

	private static CameraMovementInput SampleMovement(
		in InputSnapshot snapshot)
	{
		return new CameraMovementInput(
			Forward: snapshot.IsKeyDown(KeyCode.W),
			Backward: snapshot.IsKeyDown(KeyCode.S),
			StrafeLeft: snapshot.IsKeyDown(KeyCode.A),
			StrafeRight: snapshot.IsKeyDown(KeyCode.D),
			Up: snapshot.IsKeyDown(KeyCode.Space),
			Down: snapshot.IsKeyDown(KeyCode.LeftControl),
			Sprint: snapshot.IsKeyDown(KeyCode.LeftShift));
	}

	private static CameraPointerFocus ToPointerFocus(
		MouseFocusState focus)
	{
		return focus switch
		{
			MouseFocusState.OwnWindow => CameraPointerFocus.OwnWindow,
			MouseFocusState.OtherWindow => CameraPointerFocus.OtherWindow,
			_ => CameraPointerFocus.None
		};
	}

	private void ReportFailures(CameraCaptureTransition transition)
	{
		if (transition.CaptureFailed)
		{
			Console.Error.WriteLine(
				"Relative mouse mode could not be enabled.");
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
			"Relative mouse mode could not be disabled; retrying.");
	}
}
