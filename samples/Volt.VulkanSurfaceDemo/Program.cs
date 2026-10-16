
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Platform.Windows;

namespace Volt.VulkanSurfaceDemo;

/// <summary>
/// Verifies SDL3 and Vulkan presentation-surface integration.
/// </summary>
internal static class Program
{
    private const string ValidationArgument = "--validation";
    private const string SwapchainExtension = "VK_KHR_swapchain";

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

            Console.WriteLine(
                $"Logical window size: {window.Size}");

            Console.WriteLine(
                $"Framebuffer size: {window.PixelSize}");

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

                    uint?[] presentationFamilies =
                        new uint?[nativeDevices.Length];

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

                        presentationFamilies[index] =
                            presentationFamily;

                        candidates[index] = information with
                        {
                            GraphicsQueueFamilyIndex =
                                presentationFamily
                        };

                        Console.WriteLine(
                            $"[{index}] {information.Name}");

                        Console.WriteLine(
                            $"    Type: {information.DeviceType}");

                        Console.WriteLine(
                            "    Graphics/presentation queue: " +
                            (presentationFamily?.ToString() ?? "None"));

                        Console.WriteLine(
                            "    Suitable for surface: " +
                            candidates[index].IsSuitableForGraphics);

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
                            "No suitable GPU can present to this surface.");

                        return 3;
                    }

                    VulkanPhysicalDeviceInfo selected =
                        candidates[deviceIndex];

                    uint queueFamily =
                        presentationFamilies[deviceIndex]
                        ?? throw new InvalidOperationException(
                            "Selected GPU has no presentation queue.");

                    Console.WriteLine("Selected GPU");
                    Console.WriteLine("------------");

                    Console.WriteLine(selected.Name);

                    Console.WriteLine(
                        $"Graphics/presentation queue family: {queueFamily}");

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

                        logicalDevice.WaitIdle();

                        Console.WriteLine(
                            "Device idle wait succeeded.");
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
        Console.WriteLine("Vulkan surface integration: PASSED");

        return 0;
    }
}
