
using SDL3;
using Volt.Platform.Windows;

namespace Volt.Platform.Internal;

/// <summary>
/// Coordinates SDL video initialization and the global SDL event queue.
/// All native operations must run on the application's main thread.
/// </summary>
internal static class SdlLifetime
{
	private static readonly Lock s_gate = new();
	private static readonly Dictionary<uint, Window> s_windows = [];

	private static int s_videoUsers;

	internal static void Acquire()
	{
		lock (s_gate)
		{
			if (s_videoUsers == 0 && !SDL.Init(SDL.InitFlags.Video))
			{
				throw new InvalidOperationException(
					$"SDL video initialization failed: {SDL.GetError()}");
			}

			s_videoUsers = checked(s_videoUsers + 1);
		}
	}

	internal static void Release()
	{
		lock (s_gate)
		{
			if (s_videoUsers <= 0)
			{
				throw new InvalidOperationException(
					"SDL video was not acquired.");
			}

			s_videoUsers--;

			if (s_videoUsers == 0)
			{
				SDL.QuitSubSystem(SDL.InitFlags.Video);
			}
		}
	}

	internal static void RegisterWindow(uint windowId, Window window)
	{
		lock (s_gate)
		{
			s_windows.Add(windowId, window);
		}
	}

	internal static void UnregisterWindow(uint windowId)
	{
		lock (s_gate)
		{
			s_windows.Remove(windowId);
		}
	}

	/// <summary>
	/// Pumps SDL's process-wide event queue once and routes events by window ID.
	/// Polling any VOLT window updates all VOLT windows.
	/// </summary>
	/// <remarks>
	/// One call is one input frame: every registered window starts a
	/// new frame, the native events are dispatched, and every window
	/// freezes its completed snapshot. Events that carry no window ID
	/// are never matched against a window, and input is only applied
	/// to the window the native event names.
	/// </remarks>
	internal static void PollEvents()
	{
		lock (s_gate)
		{
			foreach (Window window in s_windows.Values)
			{
				window.BeginInputFrame();
			}

			try
			{
				while (SDL.PollEvent(out SDL.Event nativeEvent))
				{
					if (nativeEvent.Type == (uint)SDL.EventType.Quit)
					{
						foreach (Window window in s_windows.Values)
						{
							window.ReceiveCloseRequest();
						}

						continue;
					}

					if (!IsWindowScopedEvent((SDL.EventType)nativeEvent.Type))
					{
						continue;
					}

					if (s_windows.TryGetValue(
						nativeEvent.Window.WindowID,
						out Window? windowForEvent))
					{
						windowForEvent.ReceiveNativeEvent(nativeEvent);
					}
				}
			}

			finally
			{
				// Focus is queried once, after dispatch, so it reflects
				// the state the frame ended in rather than the state at
				// an arbitrary event boundary.
				IntPtr keyboardFocus = SDL.GetKeyboardFocus();
				IntPtr mouseFocus = SDL.GetMouseFocus();

				foreach (Window window in s_windows.Values)
				{
					window.CompleteInputFrame(keyboardFocus, mouseFocus);
				}
			}
		}
	}

	/// <summary>
	/// Gets whether an event type names a window and can be routed by
	/// its window ID.
	/// </summary>
	/// <remarks>
	/// Only window, keyboard, and mouse events carry a meaningful
	/// window ID. Other members of SDL's tagged event union are
	/// dropped instead of being matched against the window table,
	/// which keeps an unrelated event's payload from being mistaken
	/// for a window ID.
	/// </remarks>
	private static bool IsWindowScopedEvent(SDL.EventType type)
	{
		return (uint)type is
				(>= (uint)SDL.EventType.WindowFirst and
					<= (uint)SDL.EventType.WindowLast) or
				(uint)SDL.EventType.KeyDown or
				(uint)SDL.EventType.KeyUp or
				(uint)SDL.EventType.MouseMotion or
				(uint)SDL.EventType.MouseButtonDown or
				(uint)SDL.EventType.MouseButtonUp or
				(uint)SDL.EventType.MouseWheel;
	}
}
