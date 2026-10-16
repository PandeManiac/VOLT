
using SDL3;
using Volt.Platform.Events;
using Volt.Platform.Windows;

namespace Volt.WindowingDemo;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            WindowDescription description = new(
                "VOLT Windowing Demo",
                new WindowSize(1280, 720));

            using Window window = new(description);

            IntPtr renderer = SDL.CreateRenderer(
                window.NativeHandle,
                null);

            if (renderer == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"SDL renderer creation failed: {SDL.GetError()}");
            }

            try
            {
                Console.WriteLine(
                    $"Opened VOLT window: {window.Size}");

                Console.WriteLine(
                    $"Framebuffer pixel size: {window.PixelSize}");

                Console.WriteLine(
                    "Resize the window or close it to exit.");

                while (!window.ShouldClose)
                {
                    window.PollEvents();

                    while (window.TryDequeueEvent(
                        out WindowEvent windowEvent))
                    {
                        switch (windowEvent.Type)
                        {
                            case WindowEventType.Resized:
                                Console.WriteLine(
                                    $"Logical size: {windowEvent.Size}");
                                break;

                            case WindowEventType.PixelSizeChanged:
                                Console.WriteLine(
                                    $"Pixel size: {windowEvent.Size}");
                                break;

                            case WindowEventType.CloseRequested:
                                Console.WriteLine(
                                    "Window close requested.");
                                break;
                        }
                    }

                    if (window.ShouldClose)
                    {
                        break;
                    }

                    if (!SDL.SetRenderDrawColor(
                        renderer,
                        24,
                        32,
                        48,
                        255))
                    {
                        throw new InvalidOperationException(
                            $"Unable to set render color: {SDL.GetError()}");
                    }

                    if (!SDL.RenderClear(renderer))
                    {
                        throw new InvalidOperationException(
                            $"Unable to clear renderer: {SDL.GetError()}");
                    }

                    if (!SDL.RenderPresent(renderer))
                    {
                        throw new InvalidOperationException(
                            $"Unable to present frame: {SDL.GetError()}");
                    }

                    Thread.Sleep(16);
                }

                return 0;
            }

            finally
            {
                SDL.DestroyRenderer(renderer);
            }
        }

        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
