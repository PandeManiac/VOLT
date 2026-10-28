
using SDL3;
using Volt.Platform.Events;
using Volt.Platform.Input;
using Volt.Platform.Internal;

namespace Volt.Platform.Windows;

/// <summary>
/// Owns a native SDL application window.
/// </summary>
/// <remarks>
/// Create, access, pump events, and dispose windows on the main thread.
/// Calling PollEvents on any window dispatches SDL events to all VOLT windows.
/// The caller must dispose the window before SDL's video subsystem shuts down.
/// </remarks>
public sealed class Window : IDisposable
{
	private readonly Queue<WindowEvent> _events = [];
	private readonly WindowInput _input;
	private readonly uint _windowId;

	private IntPtr _handle;
	private bool _disposed;

	public bool ShouldClose { get; private set; }

	/// <summary>
	/// Gets the borrowed native SDL window handle.
	/// </summary>
	/// <remarks>
	/// This handle is owned by the Window instance and must not
	/// be destroyed by callers.
	///
	/// It becomes invalid when the window is disposed.
	/// Native operations must be performed on the main thread.
	/// </remarks>
	public IntPtr NativeHandle
	{
		get
		{
			ThrowIfDisposed();
			return _handle;
		}
	}

	/// <summary>
	/// Gets the client area dimensions in logical window coordinates.
	/// </summary>
	public WindowSize Size
	{
		get
		{
			ThrowIfDisposed();

			if (!SDL.GetWindowSize(
				_handle,
				out int width,
				out int height))
			{
				throw new InvalidOperationException(
					$"Unable to query window size: {SDL.GetError()}");
			}

			return new WindowSize(width, height);
		}
	}

	/// <summary>
	/// Gets the framebuffer dimensions in physical pixels.
	/// These can be zero while minimized and need not match Size.
	/// </summary>
	public WindowSize PixelSize
	{
		get
		{
			ThrowIfDisposed();

			if (!SDL.GetWindowSizeInPixels(
				_handle,
				out int width,
				out int height))
			{
				throw new InvalidOperationException(
					$"Unable to query window pixel size: {SDL.GetError()}");
			}

			return new WindowSize(width, height);
		}
	}

	/// <summary>
	/// Gets this window's keyboard and mouse input.
	/// </summary>
	/// <remarks>
	/// The input state has the same lifetime as the window and is
	/// updated by <see cref="PollEvents"/>. Polling any VOLT window
	/// starts a new input frame for every registered window and
	/// delivers each native event to the window it belongs to, so two
	/// windows never share keyboard or mouse state.
	/// </remarks>
	public WindowInput Input
	{
		get
		{
			ThrowIfDisposed();
			return _input;
		}
	}

	public Window(WindowDescription description)
	{
		description.Validate();
		SdlLifetime.Acquire();

		try
		{
			SDL.WindowFlags flags = 0;

			if (description.Resizable)
			{
				flags |= SDL.WindowFlags.Resizable;
			}

			if (description.HighPixelDensity)
			{
				flags |= SDL.WindowFlags.HighPixelDensity;
			}

			if (description.VulkanCompatible)
			{
				flags |= SDL.WindowFlags.Vulkan;
			}

			_handle = SDL.CreateWindow(
				description.Title,
				description.Size.Width,
				description.Size.Height,
				flags);

			if (_handle == IntPtr.Zero)
			{
				throw new InvalidOperationException(
					$"SDL could not create the window: {SDL.GetError()}");
			}

			_windowId = SDL.GetWindowID(_handle);

			if (_windowId == 0)
			{
				throw new InvalidOperationException(
					$"SDL could not identify the window: {SDL.GetError()}");
			}

			_input = new WindowInput(
				_handle,
				enabled => SDL.SetWindowRelativeMouseMode(_handle, enabled),
				() => SDL.GetWindowRelativeMouseMode(_handle),
				DiscardRelativeMotion);

			SdlLifetime.RegisterWindow(_windowId, this);
		}

		catch
		{
			if (_handle != IntPtr.Zero)
			{
				SDL.DestroyWindow(_handle);
				_handle = IntPtr.Zero;
			}

			SdlLifetime.Release();
			throw;
		}
	}

	/// <summary>
	/// Polls the global SDL event queue, delivering events to every VOLT window.
	/// </summary>
	/// <remarks>
	/// One call is one input frame: every registered window starts a
	/// new frame, each native input event is applied to the window it
	/// belongs to, and every window freezes a completed
	/// <see cref="InputSnapshot"/>. Call this once per application
	/// frame from the main thread and read <see cref="Input"/> after
	/// it returns.
	/// </remarks>
	public void PollEvents()
	{
		ThrowIfDisposed();
		SdlLifetime.PollEvents();
	}

	/// <summary>
	/// Retrieves the next queued event for this window.
	/// </summary>
	public bool TryDequeueEvent(out WindowEvent windowEvent)
	{
		ThrowIfDisposed();
		return _events.TryDequeue(out windowEvent);
	}

	/// <summary>
	/// Enables Unicode text input for this window.
	/// </summary>
	/// <remarks>
	/// While enabled, composed text is queued as
	/// <see cref="WindowEventType.TextInput"/> events. Composition support
	/// depends on the platform input method, so the exact text delivered
	/// for a key press must come from these events rather than from the
	/// physical key state.
	/// </remarks>
	public void StartTextInput()
	{
		ThrowIfDisposed();

		if (!SDL.StartTextInput(_handle))
		{
			throw new InvalidOperationException(
				$"SDL could not enable text input: {SDL.GetError()}");
		}
	}

	/// <summary>
	/// Disables Unicode text input for this window.
	/// </summary>
	public void StopTextInput()
	{
		ThrowIfDisposed();
		SDL.StopTextInput(_handle);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		// Unregister first so a poll can no longer deliver events
		// while the input state and the native window go away.
		SdlLifetime.UnregisterWindow(_windowId);
		_input.Dispose();
		SDL.DestroyWindow(_handle);

		_handle = IntPtr.Zero;
		_disposed = true;

		SdlLifetime.Release();
	}

	internal void ReceiveCloseRequest()
	{
		if (ShouldClose)
		{
			return;
		}

		ShouldClose = true;

		_events.Enqueue(
			new WindowEvent(WindowEventType.CloseRequested));
	}

	internal void BeginInputFrame()
	{
		_input.BeginFrame();
	}

	internal void CompleteInputFrame(
		IntPtr keyboardFocus,
		IntPtr mouseFocus)
	{
		// The window flag is checked in addition to the focused-window
		// handle so a stale focus pointer cannot report focus a window
		// no longer has.
		bool keyboardFocused =
			keyboardFocus == _handle &&
			(SDL.GetWindowFlags(_handle) & SDL.WindowFlags.InputFocus) != 0;

		_input.CompleteFrame(keyboardFocused, mouseFocus);
	}

	internal void ReceiveNativeEvent(SDL.Event nativeEvent)
	{
		_input.ReceiveEvent(nativeEvent);

		switch ((SDL.EventType)nativeEvent.Type)
		{
			case SDL.EventType.WindowCloseRequested:
				ReceiveCloseRequest();
				break;

			case SDL.EventType.WindowResized:
				_events.Enqueue(
					new WindowEvent(
						WindowEventType.Resized,
						new WindowSize(
							nativeEvent.Window.Data1,
							nativeEvent.Window.Data2)));
				break;

			case SDL.EventType.WindowPixelSizeChanged:
				_events.Enqueue(
					new WindowEvent(
						WindowEventType.PixelSizeChanged,
						new WindowSize(
							nativeEvent.Window.Data1,
							nativeEvent.Window.Data2)));
				break;

			case SDL.EventType.TextInput:
				string? text = System.Runtime.InteropServices
					.Marshal.PtrToStringUTF8(nativeEvent.Text.Text);

				if (!string.IsNullOrEmpty(text))
				{
					_events.Enqueue(
						new WindowEvent(
							WindowEventType.TextInput,
							Text: text));
				}

				break;
		}
	}

	private static void DiscardRelativeMotion()
	{
		SDL.GetRelativeMouseState(out _, out _);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
