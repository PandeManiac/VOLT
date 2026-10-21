namespace Volt.VulkanMeshDemo;

/// <summary>
/// Identifies which window owns the mouse pointer for one sample.
/// </summary>
internal enum CameraPointerFocus
{
	/// <summary>
	/// No SDL window owns the pointer. SDL reports this as a null
	/// mouse-focus window, for example while the pointer is outside
	/// every application window.
	/// </summary>
	None,

	/// <summary>
	/// The sample window owns the pointer.
	/// </summary>
	OwnWindow,

	/// <summary>
	/// A different window owns the pointer.
	/// </summary>
	OtherWindow
}

/// <summary>
/// Describes the input state relevant to relative mouse capture.
/// </summary>
/// <remarks>
/// Sampling this struct is the SDL adapter's only job, which keeps the
/// capture decision itself free of platform calls and testable
/// without a desktop.
/// </remarks>
internal readonly record struct CameraInputSample(
	bool KeyboardFocused,
	CameraPointerFocus PointerFocus,
	bool LeftButtonDown,
	bool EscapeDown);

/// <summary>
/// Describes what one update did to relative mouse mode, and what
/// gameplay may do with the same sample.
/// </summary>
/// <remarks>
/// <para>
/// A failure means the native transition could not be confirmed. The
/// controller never reports a release that SDL did not confirm.
/// </para>
/// <para>
/// <see cref="CaptureStarted"/> is true only in the update that
/// enabled relative mouse mode. <see cref="FireRequested"/> is the
/// gameplay contract for a left-button press: it is set exactly once
/// per fresh press, only while capture was already confirmed at the
/// start of the update, and never in an update that captured,
/// released, or is still waiting to release. One owner for the button
/// edge is why the capture decision and the firing decision cannot
/// drift apart.
/// </para>
/// </remarks>
internal readonly record struct CameraCaptureTransition(
	bool CaptureFailed,
	bool ReleaseFailed,
	bool ReleasePending,
	bool CaptureStarted,
	bool FireRequested);

/// <summary>
/// Owns the relative mouse capture state machine for the sample.
/// </summary>
/// <remarks>
/// <para>
/// Capture requires an intentional left click on the sample window,
/// which must own both keyboard focus and the pointer. A different
/// window owning the pointer never starts capture, and neither does a
/// null mouse focus, because a click that SDL cannot attribute to this
/// window is not intentional input for this window.
/// </para>
/// <para>
/// Escape, keyboard focus loss, or another window taking the pointer
/// releases capture. Escape is level-triggered rather than an edge:
/// while the key is held, capture cannot be started and no shot is
/// accepted, so clicking again after Escape can neither recapture the
/// pointer nor fire. A confirmed release is not repeated, so holding
/// the key does not call the native disable path every frame. A
/// release is only recorded once the native state is confirmed, so a
/// failing <c>SDL_SetWindowRelativeMouseMode</c> call leaves the
/// controller in a pending state that keeps the view frozen and is
/// retried on the next update and at shutdown. Capture is never
/// started in an update that released the mouse or that is still
/// waiting to release it.
/// </para>
/// </remarks>
internal sealed class CameraCaptureController
{
	private readonly Func<bool, bool> _setRelativeMouseMode;
	private readonly Func<bool> _isRelativeMouseModeEnabled;
	private readonly Action _discardPendingMotion;

	private bool _captured;
	private bool _releasePending;
	private bool _closed;
	private bool _leftButtonWasDown;

	/// <param name="setRelativeMouseMode">
	/// Enables or disables relative mouse mode and returns whether SDL
	/// reported success.
	/// </param>
	/// <param name="isRelativeMouseModeEnabled">
	/// Queries the live native relative mouse mode state.
	/// </param>
	/// <param name="discardPendingMotion">
	/// Discards motion accumulated before capture.
	/// </param>
	public CameraCaptureController(
		Func<bool, bool> setRelativeMouseMode,
		Func<bool> isRelativeMouseModeEnabled,
		Action discardPendingMotion)
	{
		ArgumentNullException.ThrowIfNull(setRelativeMouseMode);
		ArgumentNullException.ThrowIfNull(isRelativeMouseModeEnabled);
		ArgumentNullException.ThrowIfNull(discardPendingMotion);

		_setRelativeMouseMode = setRelativeMouseMode;
		_isRelativeMouseModeEnabled = isRelativeMouseModeEnabled;
		_discardPendingMotion = discardPendingMotion;
	}

	/// <summary>
	/// Gets whether relative mouse mode is believed to be enabled.
	/// </summary>
	/// <remarks>
	/// This tracks the last confirmed native state, so it stays true
	/// while a release is pending rather than claiming a release that
	/// did not happen.
	/// </remarks>
	public bool IsCaptured => _captured;

	/// <summary>
	/// Gets whether a release has been requested but not confirmed.
	/// </summary>
	public bool IsReleasePending => _releasePending;

	/// <summary>
	/// Gets whether relative mouse motion may rotate the camera.
	/// </summary>
	/// <remarks>
	/// Motion is ignored while a release is pending, because the
	/// pointer may still be captured and would otherwise keep turning
	/// the camera after the user asked to stop.
	/// </remarks>
	public bool IsLookActive => _captured && !_releasePending;

	/// <summary>
	/// Applies one sampled input state.
	/// </summary>
	/// <remarks>
	/// The left-button edge is tracked from every sample, including
	/// samples that only retry a pending release, so a held button
	/// never turns into a new capture request. Escape and focus are
	/// evaluated at the level, so a key that stays down keeps blocking
	/// capture and fire instead of only acting on its first frame.
	/// </remarks>
	public CameraCaptureTransition Update(CameraInputSample sample)
	{
		if (_closed)
		{
			return default;
		}

		// The firing decision is made against the capture state at the
		// start of the update: the click that starts capture is consumed
		// by capture and must never also fire.
		bool wasCaptured = _captured;
		bool released = false;
		bool releaseFailed = false;

		if (_releasePending)
		{
			released = TryRelease();
			releaseFailed = !released;
		}

		bool releaseRequested =
			wasCaptured &&
			!_releasePending &&
			IsReleaseRequested(sample);

		if (releaseRequested)
		{
			released = TryRelease();
			releaseFailed = !released;
		}

		bool captureFailed = false;
		bool captureStarted = false;

		// Capture is blocked in any update that released the mouse, so
		// Escape or focus loss combined with a fresh click cannot
		// release and immediately recapture in the same frame.
		if (!wasCaptured &&
			!_releasePending &&
			!released &&
			IsCaptureRequested(sample))
		{
			captureStarted = TryCapture();
			captureFailed = !captureStarted;
		}

		// A shot is a fresh left press with capture already confirmed.
		// Escape, focus loss, another window taking the pointer, and a
		// pending failed release all suppress it, and the held state of
		// the button is tracked below so holding never repeats a shot.
		// Escape is checked at the level rather than as an edge, so a
		// click made while the key is still down stays inert.
		bool fireRequested =
			wasCaptured &&
			!_releasePending &&
			!released &&
			sample.KeyboardFocused &&
			sample.PointerFocus == CameraPointerFocus.OwnWindow &&
			!sample.EscapeDown &&
			sample.LeftButtonDown &&
			!_leftButtonWasDown;

		_leftButtonWasDown = sample.LeftButtonDown;

		return new CameraCaptureTransition(
			CaptureFailed: captureFailed,
			ReleaseFailed: releaseFailed,
			ReleasePending: _releasePending,
			CaptureStarted: captureStarted,
			FireRequested: fireRequested);
	}

	/// <summary>
	/// Releases relative mouse mode.
	/// </summary>
	/// <returns>
	/// True when the mouse is confirmed to be uncaptured, including the
	/// case where nothing was captured.
	/// </returns>
	/// <remarks>
	/// A failed call is reconciled with the live native state rather
	/// than assumed: the mode can be changed even when SDL reports
	/// failure. If the mode is still enabled, the release stays pending
	/// for a later retry.
	/// </remarks>
	public bool TryRelease()
	{
		if (!_captured && !_releasePending)
		{
			return true;
		}

		if (_setRelativeMouseMode(false))
		{
			_captured = false;
			_releasePending = false;
			return true;
		}

		bool stillEnabled = _isRelativeMouseModeEnabled();

		_captured = stillEnabled;
		_releasePending = stillEnabled;

		return !stillEnabled;
	}

	/// <summary>
	/// Releases relative mouse mode at shutdown.
	/// </summary>
	/// <returns>
	/// True when the mouse is confirmed to be uncaptured.
	/// </returns>
	/// <remarks>
	/// Calling this repeatedly is safe. A confirmed release stops all
	/// later platform calls, a failed one stays retryable, and
	/// <see cref="Update"/> becomes a no-op once shutdown has begun.
	/// </remarks>
	public bool Close()
	{
		_closed = true;
		return TryRelease();
	}

	/// <remarks>
	/// Escape is level-triggered while captured: the first held sample
	/// releases, and every later sample sees an already released
	/// controller, so the native disable path runs once per press
	/// instead of once per frame. A pending release keeps being retried
	/// by <see cref="Update"/> regardless of the key state.
	/// </remarks>
	private static bool IsReleaseRequested(CameraInputSample sample)
	{
		return !sample.KeyboardFocused ||
			sample.PointerFocus == CameraPointerFocus.OtherWindow ||
			sample.EscapeDown;
	}

	/// <remarks>
	/// A held Escape blocks capture at the level, so a click after
	/// releasing the pointer can neither recapture nor fire while the
	/// key remains down. Capture becomes available again once Escape is
	/// released and a fresh press is seen.
	/// </remarks>
	private bool IsCaptureRequested(CameraInputSample sample)
	{
		return sample.KeyboardFocused &&
			sample.PointerFocus == CameraPointerFocus.OwnWindow &&
			!sample.EscapeDown &&
			sample.LeftButtonDown &&
			!_leftButtonWasDown;
	}

	private bool TryCapture()
	{
		if (_setRelativeMouseMode(true))
		{
			_captured = true;
			_releasePending = false;

			// Discard the motion accumulated before capture so the
			// mode change cannot rotate the camera on its first frame.
			_discardPendingMotion();

			return true;
		}

		// A failed enable can still have changed the mode, so track
		// the live state; otherwise a later Escape could not release
		// a pointer that is in fact captured.
		_captured = _isRelativeMouseModeEnabled();
		_releasePending = false;

		if (_captured)
		{
			_discardPendingMotion();
		}

		return false;
	}
}
