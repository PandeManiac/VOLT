using Volt.Platform.Input;
using Volt.Platform.Windows;
using Volt.Ui;
using Volt.Ui.Input;

namespace Volt.Editor.Input;

/// <summary>
/// Supplies the editor's viewports with the window's native relative
/// pointer mode.
/// </summary>
/// <remarks>
/// <para>
/// Capture is a state machine rather than a boolean. A native request
/// can fail, and the mode can be changed even when the backend reports
/// failure, so every transition is reconciled with the live state
/// instead of trusted. A failed release stays pending and is retried
/// by the next call, which is what guarantees the cursor cannot be
/// left hidden after navigation ends.
/// </para>
/// <para>
/// Every platform call is injected, so the whole state machine can be
/// tested without a window, SDL, or a desktop.
/// </para>
/// </remarks>
internal sealed class WindowPointerCapture : IUiPointerCapture, IDisposable
{
	private readonly Func<bool, bool> _setRelativeMouseMode;
	private readonly Func<bool> _isRelativeMouseModeEnabled;
	private readonly Action _discardPendingMotion;

	private UiPoint _motion;
	private bool _captured;
	private bool _releasePending;
	private bool _disposed;

	/// <summary>
	/// Initializes a capture source.
	/// </summary>
	/// <param name="setRelativeMouseMode">
	/// Enables or disables native relative mouse mode and reports
	/// whether the backend accepted the request.
	/// </param>
	/// <param name="isRelativeMouseModeEnabled">
	/// Queries the live native relative mouse mode state.
	/// </param>
	/// <param name="discardPendingMotion">
	/// Drains native relative motion that has not been consumed.
	/// </param>
	public WindowPointerCapture(
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
	/// Creates the capture source of one window's input.
	/// </summary>
	/// <param name="window">The window to capture the pointer for.</param>
	public static WindowPointerCapture Attach(Window window)
	{
		ArgumentNullException.ThrowIfNull(window);

		WindowInput input = window.Input;

		return new WindowPointerCapture(
			input.TrySetRelativeMouseMode,
			() => input.IsRelativeMouseModeEnabled,
			input.DiscardRelativeMotion);
	}

	/// <inheritdoc />
	public bool IsCaptured => _captured;

	/// <summary>
	/// Gets whether a release was requested but not confirmed.
	/// </summary>
	/// <remarks>
	/// A pending release keeps the view frozen: the pointer may still
	/// be captured, so any motion it reports must not turn the camera
	/// after the developer asked to stop.
	/// </remarks>
	public bool IsReleasePending => _releasePending;

	/// <inheritdoc />
	public bool TryCapture()
	{
		if (_disposed)
		{
			return false;
		}

		if (_captured && !_releasePending)
		{
			return true;
		}

		if (_releasePending && !TryRelease())
		{
			return false;
		}

		if (_setRelativeMouseMode(true))
		{
			_captured = true;
			_releasePending = false;
			_motion = UiPoint.Zero;
			_discardPendingMotion();

			return true;
		}

		// A failed enable can still have changed the mode, so the live
		// state decides what to report.
		_captured = _isRelativeMouseModeEnabled();
		_releasePending = false;
		_motion = UiPoint.Zero;

		if (_captured)
		{
			_discardPendingMotion();
		}

		return false;
	}

	/// <inheritdoc />
	public bool TryRelease()
	{
		if (_disposed)
		{
			return true;
		}

		_motion = UiPoint.Zero;

		if (!_captured && !_releasePending)
		{
			return true;
		}

		if (_setRelativeMouseMode(false))
		{
			_captured = false;
			_releasePending = false;
			_discardPendingMotion();

			return true;
		}

		bool stillEnabled = _isRelativeMouseModeEnabled();

		_captured = stillEnabled;
		_releasePending = stillEnabled;

		if (stillEnabled)
		{
			return false;
		}

		_discardPendingMotion();

		return true;
	}

	/// <inheritdoc />
	public void DiscardMotion()
	{
		if (_disposed)
		{
			return;
		}

		_motion = UiPoint.Zero;
		_discardPendingMotion();
	}

	/// <inheritdoc />
	public void AccumulateMotion(UiPoint motion)
	{
		if (_disposed || !_captured || _releasePending)
		{
			return;
		}

		if (!float.IsFinite(motion.X) || !float.IsFinite(motion.Y))
		{
			return;
		}

		_motion = new UiPoint(_motion.X + motion.X, _motion.Y + motion.Y);
	}

	/// <inheritdoc />
	public UiPoint ConsumeMotion()
	{
		if (_disposed || !_captured || _releasePending)
		{
			return UiPoint.Zero;
		}

		UiPoint motion = _motion;

		_motion = UiPoint.Zero;

		return motion;
	}

	/// <summary>
	/// Releases the pointer and stops accepting motion.
	/// </summary>
	/// <remarks>
	/// Shutdown calls this even when navigation already ended, so a
	/// failed earlier release still gets its retry before the window
	/// goes away.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		TryRelease();
		_disposed = true;
	}
}
