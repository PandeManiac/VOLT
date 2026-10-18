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
    private const int MinimizedPollMilliseconds = 16;

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
            Resizable: true,
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

        using VulkanLogicalDevice device = new(
            instance,
            selectedPhysicalDevice,
            selectedQueueFamily,
            [SwapchainExtension]);

        using VulkanCommandPool commandPool = new(
            instance,
            device);

        // Frame-slot command buffers survive swapchain recreation.
        // Each new swapchain generation creates its own synchronization.
        using VulkanCommandBuffers commandBuffers = new(
            instance,
            device,
            commandPool,
            MaxFramesInFlight);

        string shaderDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Shaders");

        Console.WriteLine(
            "Rendering cube. Resize, minimize, or close the window.");

        RenderLoop(
            window,
            instance,
            selectedPhysicalDevice,
            device,
            surface,
            commandBuffers,
            Path.Combine(shaderDirectory, "cube.vert.spv"),
            Path.Combine(shaderDirectory, "cube.frag.spv"));

        Console.WriteLine("Vulkan cube demo exited successfully.");
    }

    private static void RenderLoop(
        Window window,
        VulkanInstance instance,
        PhysicalDevice physicalDevice,
        VulkanLogicalDevice device,
        VulkanSurface surface,
        VulkanCommandBuffers commandBuffers,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        CubeSwapchainResources? resources = null;

        Stopwatch clock = Stopwatch.StartNew();
        Stopwatch statisticsClock = Stopwatch.StartNew();

        WindowSize lastFramebufferSize = default;

        bool recreationRequested = true;

        int frameIndex = 0;
        int framesSinceReport = 0;
        long totalFrames = 0;

        try
        {
            while (!window.ShouldClose)
            {
                window.PollEvents();

                while (window.TryDequeueEvent(out WindowEvent windowEvent))
                {
                    if (windowEvent.Type is WindowEventType.PixelSizeChanged
                        or WindowEventType.Resized)
                    {
                        recreationRequested = true;
                    }
                }

                if (window.ShouldClose)
                {
                    break;
                }

                WindowSize framebufferSize = window.PixelSize;

                if (!framebufferSize.IsPositive)
                {
                    // Do not create a zero-extent swapchain or spin at 100% CPU.
                    recreationRequested = true;
                    Thread.Sleep(MinimizedPollMilliseconds);
                    continue;
                }

                if (resources is null ||
                    recreationRequested ||
                    framebufferSize != lastFramebufferSize)
                {
                    if (resources is not null)
                    {
                        // Rebuild only at a generation boundary, never per frame.
                        device.WaitIdle();
                        resources.Dispose();
                        resources = null;
                    }

                    resources = new CubeSwapchainResources(
                        instance,
                        physicalDevice,
                        device,
                        surface,
                        framebufferSize,
                        MaxFramesInFlight,
                        vertexShaderPath,
                        fragmentShaderPath);

                    lastFramebufferSize = framebufferSize;
                    recreationRequested = false;
                    frameIndex = 0;

                    Console.WriteLine(
                        $"Swapchain: {resources.Swapchain.Extent.Width}x" +
                        $"{resources.Swapchain.Extent.Height}, " +
                        $"images: {resources.Swapchain.ImageCount}, " +
                        $"depth: {resources.Renderer.DepthFormat}, " +
                        $"frames in flight: " +
                        $"{resources.Synchronization.FramesInFlight}");
                }

                VulkanSwapchain swapchain = resources.Swapchain;
                VulkanFrameSynchronization synchronization =
                    resources.Synchronization;

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
                        recreationRequested = true;
                    }

                    continue;
                }

                uint imageIndex = acquisition.ImageIndex
                    ?? throw new InvalidOperationException(
                        "Acquisition returned no image index.");

                int framebufferIndex = checked((int)imageIndex);

                resources.WaitForImage(
                    instance,
                    device,
                    framebufferIndex,
                    frameFence,
                    FenceTimeoutNanoseconds);

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

                resources.Renderer.Record(
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

                resources.MarkImageSubmitted(
                    framebufferIndex,
                    frameFence);

                // A suboptimal acquisition still owns an image. Submit
                // and present it before starting a new swapchain generation.
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
                    recreationRequested = true;
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
        finally
        {
            try
            {
                // Required before freeing command buffers and render targets.
                device.WaitIdle();
            }
            finally
            {
                resources?.Dispose();
            }
        }
    }
}
