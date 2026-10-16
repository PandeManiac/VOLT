
using SDL3;
using Volt.Platform.Events;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        SdlLifetime.UnregisterWindow(_windowId);
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

    internal void ReceiveNativeEvent(SDL.Event nativeEvent)
    {
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
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
