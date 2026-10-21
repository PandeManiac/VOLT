using SDL3;
using Volt.Platform.Internal;

namespace Volt.Platform.Input;

/// <summary>
/// Owns one window's input state and exposes its completed frames.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="WindowInput"/> is created by its <c>Window</c> and
/// lives exactly as long as that window. It is not a global: two
/// windows have independent held state, edges, motion, and focus, and
/// a key or click delivered for one window never appears in the
/// other.
/// </para>
/// <para>
/// Polling is driven by <c>Window.PollEvents</c>. One poll starts a
/// new frame for every registered window, dispatches the native
/// events routed to each window, and then freezes a snapshot for
/// each. Read <see cref="Snapshot"/> after polling; calling
/// <c>PollEvents</c> again starts another frame, so an edge is
/// reported to exactly one completed frame.
/// </para>
/// <para>
/// The type also owns the native relative-mouse-mode operations.
/// It changes the mode only when asked and never captures the pointer
/// on its own: choosing when capture is appropriate is application
/// policy, not platform policy.
/// </para>
/// <para>
/// Every member must be used on the thread that created the window,
/// which is the application's main thread.
/// </para>
/// </remarks>
public sealed class WindowInput : IDisposable
{
	private readonly IntPtr _handle;
	private readonly Func<bool, bool> _setRelativeMouseMode;
	private readonly Func<bool> _isRelativeMouseModeEnabled;
	private readonly Action _discardRelativeMotion;

	private InputStateAccumulator _accumulator;
	private InputSnapshot _snapshot;
	private bool _disposed;

	internal WindowInput(
		IntPtr handle,
		Func<bool, bool> setRelativeMouseMode,
		Func<bool> isRelativeMouseModeEnabled,
		Action discardRelativeMotion)
	{
		ArgumentNullException.ThrowIfNull(setRelativeMouseMode);
		ArgumentNullException.ThrowIfNull(isRelativeMouseModeEnabled);
		ArgumentNullException.ThrowIfNull(discardRelativeMotion);

		_handle = handle;
		_setRelativeMouseMode = setRelativeMouseMode;
		_isRelativeMouseModeEnabled = isRelativeMouseModeEnabled;
		_discardRelativeMotion = discardRelativeMotion;
	}

	/// <summary>
	/// Gets the most recently completed frame.
	/// </summary>
	/// <remarks>
	/// Before the first poll this is <see cref="InputSnapshot.Empty"/>:
	/// no focus, no keys down, no edges, and no motion. A snapshot
	/// captured by the caller is a value copy and never changes when a
	/// later frame completes.
	/// </remarks>
	public InputSnapshot Snapshot
	{
		get
		{
			ThrowIfDisposed();
			return _snapshot;
		}
	}

	/// <summary>
	/// Gets the live native relative mouse mode state.
	/// </summary>
	/// <remarks>
	/// This queries the windowing backend instead of trusting the last
	/// request, because a native mode change can fail or succeed
	/// without matching its return value. The application's capture
	/// policy decides what to do with it.
	/// </remarks>
	public bool IsRelativeMouseModeEnabled
	{
		get
		{
			ThrowIfDisposed();
			return _isRelativeMouseModeEnabled();
		}
	}

	/// <summary>
	/// Requests the native relative mouse mode state.
	/// </summary>
	/// <param name="enabled">True to capture the pointer, false to
	/// release it.</param>
	/// <returns>
	/// True when the backend reported success. A caller that tracks
	/// capture must reconcile a failure with
	/// <see cref="IsRelativeMouseModeEnabled"/> instead of assuming the
	/// mode did not change.
	/// </returns>
	public bool TrySetRelativeMouseMode(bool enabled)
	{
		ThrowIfDisposed();
		return _setRelativeMouseMode(enabled);
	}

	/// <summary>
	/// Discards native relative motion that has not been consumed yet.
	/// </summary>
	/// <remarks>
	/// Call this when relative mouse mode starts or stops, so motion
	/// accumulated before the transition cannot turn the view later.
	/// It does not touch this frame's snapshot; the next completed
	/// frame starts from the next native event.
	/// </remarks>
	public void DiscardRelativeMotion()
	{
		ThrowIfDisposed();
		_discardRelativeMotion();
	}

	/// <summary>
	/// Stops accepting input.
	/// </summary>
	/// <remarks>
	/// Disposal does not release relative mouse mode: capture policy
	/// belongs to the application, which is expected to release the
	/// pointer before its window is destroyed. After disposal every
	/// public member except <see cref="Dispose"/> throws
	/// <see cref="ObjectDisposedException"/>, and the snapshot is
	/// empty.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_snapshot = InputSnapshot.Empty;
	}

	/// <summary>
	/// Starts a new input frame.
	/// </summary>
	/// <remarks>
	/// Called by the shared event pump before native events are
	/// dispatched. Held state and the last known pointer position
	/// survive; edges and accumulated motion are cleared.
	/// </remarks>
	internal void BeginFrame()
	{
		if (_disposed)
		{
			return;
		}

		_accumulator.BeginFrame();
	}

	/// <summary>
	/// Applies one native event routed to this window.
	/// </summary>
	/// <remarks>
	/// The adapter ignores event types that do not describe keyboard,
	/// mouse, or focus input. Routing by native window ID already
	/// happened in the shared pump, so this window only ever sees its
	/// own events.
	/// </remarks>
	internal void ReceiveEvent(in SDL.Event nativeEvent)
	{
		if (_disposed)
		{
			return;
		}

		SdlInputTranslator.Translate(in nativeEvent, ref _accumulator);
	}

	/// <summary>
	/// Freezes the accumulated frame.
	/// </summary>
	/// <param name="keyboardFocused">
	/// Whether the native keyboard focus belongs to this window.
	/// </param>
	/// <param name="mouseFocus">
	/// The native window that owns the pointer, or a null handle when
	/// no native window does.
	/// </param>
	/// <remarks>
	/// The focus values are queried once per pump after all events
	/// were dispatched, so they describe the state the frame ended in.
	/// A frame that lost keyboard focus, even transiently, is treated
	/// as unfocused as a whole: it reports no held input, no edges,
	/// and no motion. That keeps a focus change from fabricating a
	/// gameplay press and keeps input the window could not attribute
	/// while unfocused from reaching the application.
	/// </remarks>
	internal void CompleteFrame(bool keyboardFocused, IntPtr mouseFocus)
	{
		if (_disposed)
		{
			return;
		}

		bool focusLostDuringFrame = _accumulator.FocusLostDuringFrame;

		if (!keyboardFocused || focusLostDuringFrame)
		{
			_accumulator.DiscardFrameInput();
		}

		MouseFocusState mouseFocusState = mouseFocus == IntPtr.Zero
			? MouseFocusState.None
			: mouseFocus == _handle
				? MouseFocusState.OwnWindow
				: MouseFocusState.OtherWindow;

		_snapshot = _accumulator.ToSnapshot(
			keyboardFocused,
			mouseFocusState,
			_isRelativeMouseModeEnabled(),
			focusLostDuringFrame);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
