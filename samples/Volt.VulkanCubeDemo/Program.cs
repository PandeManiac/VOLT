using System.Diagnostics;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Events;
using Volt.Platform.Windows;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Demonstrates continuously rendering a rotating, depth-tested
/// cube using VOLT's Vulkan and platform infrastructure.
/// </summary>
internal static class Program
{
    private const string ValidationArgument = "--validation";
    private const string SwapchainExtension = "VK_KHR_swapchain";

    private const ulong FenceTimeoutNanoseconds = 5_000_000_000UL;
    private const ulong AcquireTimeoutNanoseconds = 100_000_000UL;

    private const int MaxFramesInFlight = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1
            || (args.Length == 1 && args[0] != ValidationArgument))
        {
            Console.Error.WriteLine(
                "Usage: Volt.VulkanCubeDemo [--validation]");

            return 2;
        }

        try
        {
            Run(args.Length == 1);
            return 0;
        }

        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run(bool validation)
    {
        WindowDescription description = new(
            Title: "VOLT - Rotating Vulkan Cube",
            Size: new WindowSize(1280, 720),
            Resizable: false,
            VulkanCompatible: true);

        using Window window = new(description);

        string[] extensions =
            VulkanWindowSupport.GetRequiredInstanceExtensions(
                window);

        VulkanInstanceDescription instanceDescription = new(
            ApplicationName: "VOLT Vulkan Cube",
            EnableValidation: validation);

        using VulkanInstance instance = new(
            instanceDescription,
            extensions);

        using VulkanSurface surface = new(instance, window);

        PhysicalDevice[] physicalDevices =
            instance.EnumeratePhysicalDevices();

        VulkanPhysicalDeviceInfo[] candidates =
            new VulkanPhysicalDeviceInfo[physicalDevices.Length];

        for (int index = 0; index < physicalDevices.Length; index++)
        {
            PhysicalDevice physicalDevice = physicalDevices[index];

            VulkanPhysicalDeviceInfo information =
                VulkanPhysicalDeviceInspector.Inspect(
                    instance,
                    physicalDevice);

            uint? queueFamily =
                VulkanPresentationSupport
                    .FindGraphicsPresentationQueueFamily(
                        instance,
                        physicalDevice,
                        surface);

            bool presentationSupported = false;

            if (queueFamily.HasValue)
            {
                VulkanSwapchainSupport support =
                    VulkanSwapchainSupport.Query(
                        instance,
                        physicalDevice,
                        surface);

                presentationSupported = support.CanCreateColorSwapchain;
            }

            candidates[index] = information with
            {
                GraphicsQueueFamilyIndex =
                    presentationSupported ? queueFamily : null
            };
        }

        int selectedIndex =
            VulkanPhysicalDeviceSelector.SelectIndex(
                candidates,
                requiredApiMajor: instanceDescription.ApiMajor,
                requiredApiMinor: instanceDescription.ApiMinor)
            ?? throw new NotSupportedException(
                "No graphics and presentation capable GPU was found.");

        PhysicalDevice selectedPhysicalDevice =
            physicalDevices[selectedIndex];

        uint selectedQueueFamily =
            candidates[selectedIndex].GraphicsQueueFamilyIndex
            ?? throw new InvalidOperationException(
                "The selected GPU has no presentation queue.");

        Console.WriteLine("VOLT Rotating Vulkan Cube");
        Console.WriteLine("========================");
        Console.WriteLine($"GPU: {candidates[selectedIndex].Name}");
        Console.WriteLine($"Validation: {validation}");

        WindowSize framebufferSize = window.PixelSize;

        if (!framebufferSize.IsPositive)
        {
            throw new InvalidOperationException(
                "The window framebuffer has zero dimensions.");
        }

        using VulkanLogicalDevice device = new(
            instance,
            selectedPhysicalDevice,
            selectedQueueFamily,
            [SwapchainExtension]);

        using VulkanSwapchain swapchain = new(
            instance,
            selectedPhysicalDevice,
            device,
            surface,
            checked((uint)framebufferSize.Width),
            checked((uint)framebufferSize.Height));

        using VulkanSwapchainImageViews imageViews = new(
            instance,
            device,
            swapchain);

        using VulkanCommandPool commandPool = new(
            instance,
            device);

        int framesInFlight = Math.Min(
            MaxFramesInFlight,
            checked((int)swapchain.ImageCount));

        using VulkanCommandBuffers commandBuffers = new(
            instance,
            device,
            commandPool,
            framesInFlight);

        using VulkanFrameSynchronization synchronization = new(
            instance,
            device,
            framesInFlight,
            checked((int)swapchain.ImageCount));

        string shaderDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Shaders");

        using CubeRenderer renderer = new(
            instance,
            device,
            selectedPhysicalDevice,
            swapchain,
            imageViews,
            Path.Combine(shaderDirectory, "cube.vert.spv"),
            Path.Combine(shaderDirectory, "cube.frag.spv"));

        Console.WriteLine($"Swapchain: {swapchain.Extent.Width}x{swapchain.Extent.Height}");
        Console.WriteLine($"Swapchain images: {swapchain.ImageCount}");
        Console.WriteLine($"Depth format: {renderer.DepthFormat}");
        Console.WriteLine($"Frames in flight: {framesInFlight}");
        Console.WriteLine("Rendering cube. Close the window to exit.");

        try
        {
            RenderLoop(
                window,
                instance,
                device,
                swapchain,
                commandBuffers,
                synchronization,
                renderer);
        }

        finally
        {
            device.WaitIdle();
        }

        Console.WriteLine("Vulkan cube demo exited successfully.");
    }

    private static void RenderLoop(
        Window window,
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanSwapchain swapchain,
        VulkanCommandBuffers commandBuffers,
        VulkanFrameSynchronization synchronization,
        CubeRenderer renderer)
    {
        Fence[] imageInFlightFences =
            new Fence[checked((int)swapchain.ImageCount)];

        Stopwatch clock = Stopwatch.StartNew();
        Stopwatch statisticsClock = Stopwatch.StartNew();

        int frameIndex = 0;
        int framesSinceReport = 0;
        long totalFrames = 0;

        while (!window.ShouldClose)
        {
            window.PollEvents();

            bool sizeChanged = false;

            while (window.TryDequeueEvent(out WindowEvent windowEvent))
            {
                if (windowEvent.Type is WindowEventType.PixelSizeChanged
                    or WindowEventType.Resized)
                {
                    WindowSize pixelSize = window.PixelSize;

                    sizeChanged =
                        !pixelSize.IsPositive ||
                        (uint)pixelSize.Width != swapchain.Extent.Width ||
                        (uint)pixelSize.Height != swapchain.Extent.Height;
                }
            }

            if (window.ShouldClose)
            {
                break;
            }

            if (sizeChanged)
            {
                Console.WriteLine(
                    "Framebuffer size changed. Exiting because " +
                    "swapchain recreation is not implemented yet.");

                break;
            }

            Fence frameFence =
                synchronization.GetInFlightFence(frameIndex);

            VulkanQueueSubmission.WaitForFence(
                instance,
                device,
                frameFence,
                FenceTimeoutNanoseconds);

            Silk.NET.Vulkan.Semaphore imageAvailable =
                synchronization.GetImageAvailableSemaphore(
                    frameIndex);

            VulkanImageAcquisitionResult acquisition =
                VulkanSwapchainAcquisition.Acquire(
                    instance,
                    device,
                    swapchain,
                    imageAvailable,
                    AcquireTimeoutNanoseconds);

            if (!acquisition.HasImage)
            {
                if (acquisition.RequiresRecreation)
                {
                    Console.WriteLine(
                        "Swapchain is out of date. Exiting.");

                    break;
                }

                continue;
            }

            uint imageIndex = acquisition.ImageIndex
                ?? throw new InvalidOperationException(
                    "Acquisition returned no image index.");

            int framebufferIndex = checked((int)imageIndex);

            Fence imageFence =
                imageInFlightFences[framebufferIndex];

            if (imageFence.Handle != 0 &&
                imageFence.Handle != frameFence.Handle)
            {
                VulkanQueueSubmission.WaitForFence(
                    instance,
                    device,
                    imageFence,
                    FenceTimeoutNanoseconds);
            }

            CommandBuffer commandBuffer =
                commandBuffers.GetBuffer(frameIndex);

            Result resetResult = instance.Api.ResetCommandBuffer(
                commandBuffer,
                CommandBufferResetFlags.None);

            if (resetResult != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Command buffer reset failed: {resetResult}.");
            }

            VulkanCommandRecording.Begin(
                instance,
                commandBuffer);

            renderer.Record(
                commandBuffer,
                framebufferIndex,
                (float)clock.Elapsed.TotalSeconds * 0.85f);

            VulkanCommandRecording.End(
                instance,
                commandBuffer);

            Silk.NET.Vulkan.Semaphore renderFinished =
                synchronization.GetRenderFinishedSemaphore(
                    framebufferIndex);

            VulkanQueueSubmission.ResetFence(
                instance,
                device,
                frameFence);

            VulkanQueueSubmission.Submit(
                instance,
                device,
                commandBuffer,
                imageAvailable,
                PipelineStageFlags.AllCommandsBit,
                renderFinished,
                frameFence);

            imageInFlightFences[framebufferIndex] =
                frameFence;

            VulkanSwapchainPresentationResult presentation =
                VulkanSwapchainPresentation.Present(
                    instance,
                    device,
                    swapchain,
                    imageIndex,
                    renderFinished);

            if (!presentation.WasAccepted ||
                presentation.RequiresRecreation ||
                acquisition.RequiresRecreation)
            {
                Console.WriteLine(
                    "Presentation requires swapchain recreation. " +
                    "Exiting the demo.");

                break;
            }

            frameIndex =
                (frameIndex + 1) % synchronization.FramesInFlight;

            totalFrames++;
            framesSinceReport++;

            if (statisticsClock.Elapsed.TotalSeconds >= 1.0)
            {
                double fps =
                    framesSinceReport /
                    statisticsClock.Elapsed.TotalSeconds;

                Console.WriteLine(
                    $"Frames: {totalFrames:N0}, FPS: {fps:F1}");

                framesSinceReport = 0;
                statisticsClock.Restart();
            }
        }
    }
}
