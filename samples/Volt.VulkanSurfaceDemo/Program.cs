using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Windows;

namespace Volt.VulkanSurfaceDemo;

/// <summary>
/// Exercises Vulkan surface integration, swapchain creation,
/// image-view creation, command recording, frame synchronization,
/// fence-backed graphics submission, and native resource cleanup.
/// </summary>
internal static class Program
{
    private const string ValidationArgument = "--validation";
    private const string SwapchainExtension = "VK_KHR_swapchain";
    private const int FramesInFlight = 2;
    private const ulong SubmissionTimeoutNanoseconds = 5_000_000_000UL;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1
            || (args.Length == 1 && args[0] != ValidationArgument))
        {
            Console.Error.WriteLine(
                "Usage: Volt.VulkanSurfaceDemo [--validation]");

            return 2;
        }

        bool enableValidation = args.Length == 1;

        try
        {
            return RunDemo(enableValidation);
        }

        catch (Exception exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Vulkan surface demo failed:");
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static int RunDemo(bool enableValidation)
    {
        Console.WriteLine("VOLT Vulkan Surface Demo");
        Console.WriteLine("========================");
        Console.WriteLine();

        WindowDescription windowDescription = new(
            Title: "VOLT Vulkan Surface Demo",
            Size: new WindowSize(1280, 720),
            VulkanCompatible: true);

        using (Window window = new(windowDescription))
        {
            Console.WriteLine("SDL3 Vulkan window created.");
            Console.WriteLine($"Logical window size: {window.Size}");
            Console.WriteLine($"Framebuffer size: {window.PixelSize}");
            Console.WriteLine();

            string[] instanceExtensions =
                VulkanWindowSupport.GetRequiredInstanceExtensions(
                    window);

            Console.WriteLine("Required Vulkan instance extensions:");

            foreach (string extension in instanceExtensions)
            {
                Console.WriteLine($"  {extension}");
            }

            Console.WriteLine();

            VulkanInstanceDescription instanceDescription = new(
                ApplicationName: "VOLT Vulkan Surface Demo",
                EnableValidation: enableValidation);

            using (VulkanInstance instance = new(
                instanceDescription,
                instanceExtensions))
            {
                Console.WriteLine(
                    "Vulkan instance created successfully.");

                Console.WriteLine(
                    $"Validation requested: {enableValidation}");

                Console.WriteLine();

                using (VulkanSurface surface = new(instance, window))
                {
                    Console.WriteLine(
                        "Vulkan presentation surface created.");

                    Console.WriteLine(
                        $"Surface handle: 0x{surface.Handle.Handle:X}");

                    Console.WriteLine();

                    PhysicalDevice[] nativeDevices =
                        instance.EnumeratePhysicalDevices();

                    VulkanPhysicalDeviceInfo[] candidates =
                        new VulkanPhysicalDeviceInfo[nativeDevices.Length];

                    VulkanSwapchainSupport?[] swapchainSupport =
                        new VulkanSwapchainSupport?[nativeDevices.Length];

                    Console.WriteLine(
                        $"Physical devices: {nativeDevices.Length}");

                    Console.WriteLine();

                    for (int index = 0;
                         index < nativeDevices.Length;
                         index++)
                    {
                        VulkanPhysicalDeviceInfo information =
                            VulkanPhysicalDeviceInspector.Inspect(
                                instance,
                                nativeDevices[index]);

                        uint? presentationFamily =
                            VulkanPresentationSupport
                                .FindGraphicsPresentationQueueFamily(
                                    instance,
                                    nativeDevices[index],
                                    surface);

                        Console.WriteLine(
                            $"[{index}] {information.Name}");

                        Console.WriteLine(
                            $"    Type: {information.DeviceType}");

                        Console.WriteLine(
                            "    Graphics/presentation queue: " +
                            (presentationFamily?.ToString() ?? "None"));

                        if (presentationFamily.HasValue)
                        {
                            VulkanSwapchainSupport support =
                                VulkanSwapchainSupport.Query(
                                    instance,
                                    nativeDevices[index],
                                    surface);

                            swapchainSupport[index] = support;

                            Console.WriteLine(
                                "    Color swapchain supported: " +
                                support.CanCreateColorSwapchain);

                            Console.WriteLine(
                                $"    Surface formats: {support.Formats.Length}");

                            Console.WriteLine(
                                $"    Present modes: {support.PresentModes.Length}");
                        }

                        else
                        {
                            Console.WriteLine(
                                "    Color swapchain supported: False");
                        }

                        bool suitableForPresentation =
                            presentationFamily.HasValue &&
                            swapchainSupport[index]?
                                .CanCreateColorSwapchain == true;

                        candidates[index] = information with
                        {
                            GraphicsQueueFamilyIndex =
                                suitableForPresentation
                                    ? presentationFamily
                                    : null
                        };

                        Console.WriteLine();
                    }

                    int? selectedIndex =
                        VulkanPhysicalDeviceSelector.SelectIndex(
                            candidates,
                            requiredApiMajor:
                                instanceDescription.ApiMajor,
                            requiredApiMinor:
                                instanceDescription.ApiMinor);

                    if (selectedIndex is not int deviceIndex)
                    {
                        Console.Error.WriteLine(
                            "No suitable GPU supports graphics, " +
                            "presentation, and color swapchains.");

                        return 3;
                    }

                    VulkanPhysicalDeviceInfo selected =
                        candidates[deviceIndex];

                    uint queueFamily =
                        selected.GraphicsQueueFamilyIndex
                        ?? throw new InvalidOperationException(
                            "Selected GPU has no presentation queue.");

                    VulkanSwapchainSupport selectedSupport =
                        swapchainSupport[deviceIndex]
                        ?? throw new InvalidOperationException(
                            "Selected GPU has no swapchain support snapshot.");

                    Console.WriteLine("Selected GPU");
                    Console.WriteLine("------------");

                    Console.WriteLine(selected.Name);

                    Console.WriteLine(
                        $"Graphics/presentation queue family: {queueFamily}");

                    Console.WriteLine();

                    PrintSwapchainSupport(selectedSupport);

                    Console.WriteLine();

                    WindowSize framebufferSize = window.PixelSize;

                    if (!framebufferSize.IsPositive)
                    {
                        throw new InvalidOperationException(
                            "The framebuffer has zero dimensions. " +
                            "Cannot create a swapchain while minimized.");
                    }

                    PrintSelectedConfiguration(
                        selectedSupport,
                        framebufferSize);

                    Console.WriteLine();

                    using (VulkanLogicalDevice logicalDevice = new(
                        instance,
                        nativeDevices[deviceIndex],
                        queueFamily,
                        [SwapchainExtension]))
                    {
                        Console.WriteLine(
                            "Vulkan logical device created successfully.");

                        Console.WriteLine(
                            "VK_KHR_swapchain enabled.");

                        Console.WriteLine(
                            $"Graphics queue handle: " +
                            $"0x{logicalDevice.GraphicsQueue.Handle:X}");

                        Console.WriteLine();

                        using (VulkanSwapchain swapchain = new(
                            instance,
                            nativeDevices[deviceIndex],
                            logicalDevice,
                            surface,
                            checked((uint)framebufferSize.Width),
                            checked((uint)framebufferSize.Height)))
                        {
                            PrintSwapchain(swapchain);

                            Console.WriteLine();

                            using (VulkanSwapchainImageViews imageViews = new(
                                instance,
                                logicalDevice,
                                swapchain))
                            {
                                PrintImageViews(swapchain, imageViews);

                                Console.WriteLine();

                                using (VulkanCommandPool commandPool = new(
                                    instance,
                                    logicalDevice))
                                {
                                    Console.WriteLine(
                                        "Vulkan command pool created successfully.");

                                    Console.WriteLine(
                                        $"Command pool handle: " +
                                        $"0x{commandPool.Handle.Handle:X}");

                                    Console.WriteLine(
                                        $"Queue family: {commandPool.QueueFamilyIndex}");

                                    Console.WriteLine(
                                        $"Creation flags: {commandPool.Flags}");

                                    Console.WriteLine();

                                    int commandBufferCount =
                                        checked((int)swapchain.ImageCount);

                                    using (VulkanCommandBuffers commandBuffers = new(
                                        instance,
                                        logicalDevice,
                                        commandPool,
                                        commandBufferCount))
                                    {
                                        PrintCommandBuffers(
                                            commandPool,
                                            commandBuffers,
                                            commandBufferCount);

                                        Console.WriteLine();

                                        RecordCommandBuffers(
                                            instance,
                                            commandBuffers);

                                        Console.WriteLine();

                                        using (VulkanFrameSynchronization synchronization = new(
                                            instance,
                                            logicalDevice,
                                            FramesInFlight,
                                            checked((int)swapchain.ImageCount)))
                                        {
                                            PrintFrameSynchronization(
                                                instance,
                                                logicalDevice,
                                                synchronization,
                                                FramesInFlight,
                                                checked((int)swapchain.ImageCount));

                                            Console.WriteLine();

                                            SubmitRecordedCommandBuffer(
                                                instance,
                                                logicalDevice,
                                                commandBuffers,
                                                synchronization);
                                        }

                                        Console.WriteLine();

                                        Console.WriteLine(
                                            "Vulkan frame synchronization resources " +
                                            "destroyed successfully.");
                                    }

                                    Console.WriteLine();

                                    Console.WriteLine(
                                        "Vulkan command buffers freed successfully.");
                                }

                                Console.WriteLine();

                                Console.WriteLine(
                                    "Vulkan command pool destroyed successfully.");
                            }

                            Console.WriteLine();

                            Console.WriteLine(
                                "Vulkan swapchain image views " +
                                "destroyed successfully.");
                        }

                        Console.WriteLine();

                        Console.WriteLine(
                            "Vulkan swapchain destroyed successfully.");
                    }

                    Console.WriteLine(
                        "Vulkan logical device destroyed successfully.");
                }

                Console.WriteLine(
                    "Vulkan presentation surface destroyed successfully.");
            }

            Console.WriteLine(
                "Vulkan instance destroyed successfully.");
        }

        Console.WriteLine("SDL3 window destroyed successfully.");
        Console.WriteLine();

        Console.WriteLine(
            "Vulkan swapchain, command recording, and GPU submission: PASSED");

        return 0;
    }

    /// <summary>
    /// Submits one previously recorded primary command buffer
    /// without semaphore dependencies and verifies fence completion.
    /// </summary>
    private static void SubmitRecordedCommandBuffer(
        VulkanInstance instance,
        VulkanLogicalDevice logicalDevice,
        VulkanCommandBuffers commandBuffers,
        VulkanFrameSynchronization synchronization)
    {
        if (commandBuffers.Count == 0
            || synchronization.FramesInFlight == 0)
        {
            throw new InvalidOperationException(
                "GPU submission requires a command buffer and a frame fence.");
        }

        CommandBuffer buffer = commandBuffers.GetBuffer(0);
        Fence fence = synchronization.GetInFlightFence(0);

        if (buffer.Handle == 0 || fence.Handle == 0)
        {
            throw new InvalidOperationException(
                "GPU submission requires nonzero command buffer and fence handles.");
        }

        Console.WriteLine("Vulkan graphics queue submission");
        Console.WriteLine("--------------------------------");

        Console.WriteLine(
            $"Command buffer: 0x{buffer.Handle:X}");

        Console.WriteLine(
            $"Completion fence: 0x{fence.Handle:X}");

        // The first fence starts signaled because no previous
        // frame has been submitted for this frame slot.
        VulkanQueueSubmission.WaitForFence(
            instance,
            logicalDevice,
            fence,
            SubmissionTimeoutNanoseconds);

        Console.WriteLine("Initial fence wait succeeded.");

        // Fence reset occurs only once a command buffer is ready
        // for immediate submission. It must not stay unsignaled
        // across a skipped frame in the future frame loop.
        VulkanQueueSubmission.ResetFence(
            instance,
            logicalDevice,
            fence);

        Result resetStatus = instance.Api.GetFenceStatus(
            logicalDevice.Handle,
            fence);

        if (resetStatus != Result.NotReady)
        {
            throw new InvalidOperationException(
                "The reset submission fence should be unsignaled, " +
                $"but vkGetFenceStatus returned {resetStatus}.");
        }

        Console.WriteLine("Completion fence reset successfully.");

        try
        {
            // This is an empty, executable command buffer.
            // No image is acquired and no semaphore is used.
            VulkanQueueSubmission.Submit(
                instance,
                logicalDevice,
                buffer,
                fence);

            Console.WriteLine("vkQueueSubmit accepted the command buffer.");

            VulkanQueueSubmission.WaitForFence(
                instance,
                logicalDevice,
                fence,
                SubmissionTimeoutNanoseconds);

            Result completedStatus = instance.Api.GetFenceStatus(
                logicalDevice.Handle,
                fence);

            if (completedStatus != Result.Success)
            {
                throw new InvalidOperationException(
                    "The submission fence should be signaled, " +
                    $"but vkGetFenceStatus returned {completedStatus}.");
            }

            Console.WriteLine("GPU submission completed; fence is signaled.");
        }

        finally
        {
            // If the finite fence wait fails, do not free a
            // possibly pending command buffer or fence. Synchronize
            // the device before the enclosing using scopes unwind.
            logicalDevice.WaitIdle();
        }

        Console.WriteLine("Vulkan device idle wait succeeded.");
    }

    private static void PrintFrameSynchronization(
        VulkanInstance instance,
        VulkanLogicalDevice logicalDevice,
        VulkanFrameSynchronization synchronization,
        int expectedFrameCount,
        int expectedImageCount)
    {
        Console.WriteLine("Vulkan frame synchronization");
        Console.WriteLine("----------------------------");

        if (synchronization.FramesInFlight != expectedFrameCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedFrameCount} frames in flight, " +
                $"but got {synchronization.FramesInFlight}.");
        }

        if (synchronization.SwapchainImageCount != expectedImageCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedImageCount} swapchain images, " +
                $"but got {synchronization.SwapchainImageCount}.");
        }

        Console.WriteLine(
            $"Frames in flight: {synchronization.FramesInFlight}");

        Console.WriteLine(
            $"Swapchain image count: {synchronization.SwapchainImageCount}");

        Console.WriteLine();
        Console.WriteLine("Per-frame image-available semaphores and fences:");

        HashSet<ulong> semaphoreHandles = [];
        HashSet<ulong> fenceHandles = [];

        for (int index = 0; index < synchronization.FramesInFlight; index++)
        {
            Silk.NET.Vulkan.Semaphore imageAvailable =
                synchronization.GetImageAvailableSemaphore(index);

            Fence inFlight =
                synchronization.GetInFlightFence(index);

            if (imageAvailable.Handle == 0
                || !semaphoreHandles.Add(imageAvailable.Handle))
            {
                throw new InvalidOperationException(
                    $"Frame {index} has a null or duplicate " +
                    "image-available semaphore handle.");
            }

            if (inFlight.Handle == 0
                || !fenceHandles.Add(inFlight.Handle))
            {
                throw new InvalidOperationException(
                    $"Frame {index} has a null or duplicate fence handle.");
            }

            Result status = instance.Api.GetFenceStatus(
                logicalDevice.Handle,
                inFlight);

            if (status != Result.Success)
            {
                throw new InvalidOperationException(
                    $"Frame {index} fence should be initially signaled, " +
                    $"but vkGetFenceStatus returned {status}.");
            }

            Console.WriteLine(
                $"  Frame [{index}] Image available: 0x{imageAvailable.Handle:X}");

            Console.WriteLine(
                $"            In-flight fence: 0x{inFlight.Handle:X} (Signaled)");
        }

        Console.WriteLine();
        Console.WriteLine("Per-swapchain-image render-finished semaphores:");

        for (int index = 0; index < synchronization.SwapchainImageCount; index++)
        {
            Silk.NET.Vulkan.Semaphore renderFinished =
                synchronization.GetRenderFinishedSemaphore(index);

            if (renderFinished.Handle == 0
                || !semaphoreHandles.Add(renderFinished.Handle))
            {
                throw new InvalidOperationException(
                    $"Swapchain image {index} has a null or duplicate " +
                    "render-finished semaphore handle.");
            }

            Console.WriteLine(
                $"  Image [{index}] Render finished: 0x{renderFinished.Handle:X}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "All Vulkan frame synchronization handles verified successfully.");

        Console.WriteLine(
            "All frame fences are initially signaled.");
    }

    private static void PrintSwapchain(
        VulkanSwapchain swapchain)
    {
        Console.WriteLine("Vulkan swapchain");
        Console.WriteLine("----------------");

        Console.WriteLine("Swapchain created successfully.");

        Console.WriteLine(
            $"Swapchain handle: 0x{swapchain.Handle.Handle:X}");

        Console.WriteLine(
            $"Image format: {swapchain.ImageFormat}");

        Console.WriteLine(
            $"Color space: {swapchain.ColorSpace}");

        Console.WriteLine(
            $"Present mode: {swapchain.PresentMode}");

        Console.WriteLine(
            $"Image extent: " +
            $"{swapchain.Extent.Width}x{swapchain.Extent.Height}");

        Console.WriteLine(
            $"Actual swapchain images: {swapchain.ImageCount}");

        if (swapchain.ImageCount == 0)
        {
            throw new InvalidOperationException(
                "The swapchain contains no images.");
        }

        Console.WriteLine();
        Console.WriteLine("Swapchain images:");

        for (int index = 0;
             index < swapchain.ImageCount;
             index++)
        {
            Image image = swapchain.GetImage(index);

            if (image.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Swapchain image {index} has a null handle.");
            }

            Console.WriteLine(
                $"  [{index}] 0x{image.Handle:X}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "Swapchain image enumeration succeeded.");
    }

    private static void PrintImageViews(
        VulkanSwapchain swapchain,
        VulkanSwapchainImageViews imageViews)
    {
        Console.WriteLine("Vulkan swapchain image views");
        Console.WriteLine("----------------------------");

        int expectedCount =
            checked((int)swapchain.ImageCount);

        if (imageViews.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} image views, " +
                $"but Vulkan created {imageViews.Count}.");
        }

        Console.WriteLine(
            $"Expected image views: {expectedCount}");

        Console.WriteLine(
            $"Created image views: {imageViews.Count}");

        Console.WriteLine();

        for (int index = 0; index < imageViews.Count; index++)
        {
            Image image = swapchain.GetImage(index);
            ImageView view = imageViews.GetView(index);

            if (image.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Swapchain image {index} has a null handle.");
            }

            if (view.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Swapchain image view {index} has a null handle.");
            }

            Console.WriteLine(
                $"  [{index}] Image: 0x{image.Handle:X}");

            Console.WriteLine(
                $"      View:  0x{view.Handle:X}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "All swapchain image views created successfully.");
    }

    private static void PrintCommandBuffers(
        VulkanCommandPool commandPool,
        VulkanCommandBuffers commandBuffers,
        int expectedCount)
    {
        Console.WriteLine("Vulkan command buffers");
        Console.WriteLine("----------------------");

        if (commandPool.Handle.Handle == 0)
        {
            throw new InvalidOperationException(
                "The command pool has a null native handle.");
        }

        if (commandBuffers.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} command buffers, " +
                $"but allocated {commandBuffers.Count}.");
        }

        if (commandBuffers.Level != CommandBufferLevel.Primary)
        {
            throw new InvalidOperationException(
                "Expected primary Vulkan command buffers.");
        }

        Console.WriteLine(
            $"Expected command buffers: {expectedCount}");

        Console.WriteLine(
            $"Allocated command buffers: {commandBuffers.Count}");

        Console.WriteLine(
            $"Command buffer level: {commandBuffers.Level}");

        Console.WriteLine();

        for (int index = 0; index < commandBuffers.Count; index++)
        {
            CommandBuffer buffer = commandBuffers.GetBuffer(index);

            if (buffer.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Command buffer {index} has a null native handle.");
            }

            Console.WriteLine(
                $"  [{index}] 0x{buffer.Handle:X}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "All primary command buffers allocated successfully.");
    }

    private static void RecordCommandBuffers(
        VulkanInstance instance,
        VulkanCommandBuffers commandBuffers)
    {
        Console.WriteLine("Vulkan command recording");
        Console.WriteLine("------------------------");

        for (int index = 0; index < commandBuffers.Count; index++)
        {
            CommandBuffer buffer = commandBuffers.GetBuffer(index);

            if (buffer.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Command buffer {index} has a null native handle.");
            }

            VulkanCommandRecording.Begin(instance, buffer);
            VulkanCommandRecording.End(instance, buffer);

            Console.WriteLine(
                $"  [{index}] 0x{buffer.Handle:X} recorded successfully.");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"All {commandBuffers.Count} primary command buffers " +
            "recorded successfully.");
    }

    private static void PrintSwapchainSupport(
        VulkanSwapchainSupport support)
    {
        SurfaceCapabilitiesKHR capabilities =
            support.Capabilities;

        Console.WriteLine("Vulkan swapchain capabilities");
        Console.WriteLine("-----------------------------");

        Console.WriteLine(
            $"Minimum image count: {capabilities.MinImageCount}");

        Console.WriteLine(
            "Maximum image count: " +
            (capabilities.MaxImageCount == 0
                ? "Unbounded"
                : capabilities.MaxImageCount.ToString()));

        if (capabilities.CurrentExtent.Width == uint.MaxValue)
        {
            Console.WriteLine(
                "Current surface extent: Variable");
        }

        else
        {
            Console.WriteLine(
                $"Current surface extent: " +
                $"{capabilities.CurrentExtent.Width}x" +
                $"{capabilities.CurrentExtent.Height}");
        }

        Console.WriteLine(
            $"Minimum image extent: " +
            $"{capabilities.MinImageExtent.Width}x" +
            $"{capabilities.MinImageExtent.Height}");

        Console.WriteLine(
            $"Maximum image extent: " +
            $"{capabilities.MaxImageExtent.Width}x" +
            $"{capabilities.MaxImageExtent.Height}");

        Console.WriteLine(
            $"Maximum image array layers: " +
            capabilities.MaxImageArrayLayers);

        Console.WriteLine(
            $"Supported image usage: " +
            capabilities.SupportedUsageFlags);

        Console.WriteLine(
            $"Current transform: " +
            capabilities.CurrentTransform);

        Console.WriteLine(
            $"Supported transforms: " +
            capabilities.SupportedTransforms);

        Console.WriteLine(
            $"Supported composite alpha: " +
            capabilities.SupportedCompositeAlpha);

        Console.WriteLine(
            $"Color attachment supported: " +
            support.SupportsColorAttachment);

        Console.WriteLine();
        Console.WriteLine("Available surface formats:");

        foreach (SurfaceFormatKHR format in support.Formats.Span)
        {
            Console.WriteLine(
                $"  {format.Format} / {format.ColorSpace}");
        }

        Console.WriteLine();
        Console.WriteLine("Available presentation modes:");

        foreach (PresentModeKHR mode in support.PresentModes.Span)
        {
            Console.WriteLine($"  {mode}");
        }
    }

    private static void PrintSelectedConfiguration(
        VulkanSwapchainSupport support,
        WindowSize framebufferSize)
    {
        if (!support.CanCreateColorSwapchain)
        {
            throw new NotSupportedException(
                "The selected surface does not support " +
                "VOLT's color swapchain requirements.");
        }

        SurfaceFormatKHR selectedFormat =
            VulkanSwapchainSelector.SelectSurfaceFormat(
                support.Formats.Span);

        PresentModeKHR selectedMode =
            VulkanSwapchainSelector.SelectPresentMode(
                support.PresentModes.Span);

        uint imageCount =
            VulkanSwapchainSelector.SelectImageCount(
                support.Capabilities);

        Extent2D extent =
            VulkanSwapchainSelector.SelectExtent(
                support.Capabilities,
                checked((uint)framebufferSize.Width),
                checked((uint)framebufferSize.Height));

        Console.WriteLine("Selected swapchain configuration");
        Console.WriteLine("--------------------------------");

        Console.WriteLine(
            $"Image format: {selectedFormat.Format}");

        Console.WriteLine(
            $"Color space: {selectedFormat.ColorSpace}");

        Console.WriteLine(
            $"Present mode: {selectedMode}");

        Console.WriteLine(
            $"Requested image count: {imageCount}");

        Console.WriteLine(
            $"Image extent: {extent.Width}x{extent.Height}");

        Console.WriteLine();
        Console.WriteLine(
            "Swapchain parameters selected successfully.");
    }
}
