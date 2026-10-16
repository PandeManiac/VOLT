
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
    internal static void PollEvents()
    {
        lock (s_gate)
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

                if (s_windows.TryGetValue(
                    nativeEvent.Window.WindowID,
                    out Window? windowForEvent))
                {
                    windowForEvent.ReceiveNativeEvent(nativeEvent);
                }
            }
        }
    }
}
