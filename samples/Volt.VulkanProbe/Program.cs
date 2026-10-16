
using Silk.NET.Vulkan;
using Volt.Graphics.Devices;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.VulkanProbe;

/// <summary>
/// Exercises VOLT's Vulkan instance, physical-device discovery,
/// logical-device creation, and graphics queue initialization.
/// </summary>
internal static class Program
{
    private const string ValidationArgument = "--validation";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 1
            || (args.Length == 1 && args[0] != ValidationArgument))
        {
            Console.Error.WriteLine(
                "Usage: Volt.VulkanProbe [--validation]");

            return 2;
        }

        bool enableValidation = args.Length == 1;

        try
        {
            return RunProbe(enableValidation);
        }

        catch (Exception exception)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Vulkan probe failed:");
            Console.Error.WriteLine(exception);

            return 1;
        }
    }

    private static int RunProbe(bool enableValidation)
    {
        VulkanInstanceDescription description = new(
            ApplicationName: "VOLT Vulkan Probe",
            EnableValidation: enableValidation);

        Console.WriteLine("VOLT Vulkan Probe");
        Console.WriteLine("=================");
        Console.WriteLine();

        Console.WriteLine(
            $"Validation requested: {enableValidation}");

        Console.WriteLine(
            $"Requested Vulkan API: " +
            $"{description.ApiMajor}.{description.ApiMinor}");

        Console.WriteLine();

        bool selectedSuccessfully;

        using (VulkanInstance instance = new(description))
        {
            Console.WriteLine(
                "Vulkan instance created successfully.");

            Console.WriteLine(
                $"Vulkan loader API: " +
                FormatVersion(instance.LoaderApiVersion));

            Console.WriteLine();

            PhysicalDevice[] nativeDevices =
                instance.EnumeratePhysicalDevices();

            Console.WriteLine(
                $"Physical devices discovered: {nativeDevices.Length}");

            Console.WriteLine();

            VulkanPhysicalDeviceInfo[] devices =
                new VulkanPhysicalDeviceInfo[nativeDevices.Length];

            for (int index = 0; index < nativeDevices.Length; index++)
            {
                devices[index] =
                    VulkanPhysicalDeviceInspector.Inspect(
                        instance,
                        nativeDevices[index]);

                PrintDevice(index, devices[index]);
            }

            int? selectedIndex =
                VulkanPhysicalDeviceSelector.SelectIndex(
                    devices,
                    requiredApiMajor: description.ApiMajor,
                    requiredApiMinor: description.ApiMinor);

            selectedSuccessfully = selectedIndex.HasValue;

            if (selectedIndex is int deviceIndex)
            {
                VulkanPhysicalDeviceInfo selected =
                    devices[deviceIndex];

                PhysicalDevice selectedNativeDevice =
                    nativeDevices[deviceIndex];

                GraphicsDeviceCapabilities capabilities =
                    VulkanCapabilityMapper.Map(selected);

                Console.WriteLine("Selected physical device");
                Console.WriteLine("------------------------");

                Console.WriteLine($"Index: {deviceIndex}");
                Console.WriteLine($"Name: {selected.Name}");
                Console.WriteLine();

                PrintCapabilities(capabilities);

                Console.WriteLine();

                if (selected.GraphicsQueueFamilyIndex
                    is not uint graphicsQueueFamilyIndex)
                {
                    throw new InvalidOperationException(
                        "The selected physical device has no " +
                        "graphics queue family.");
                }

                using (VulkanLogicalDevice logicalDevice = new(
                    instance,
                    selectedNativeDevice,
                    graphicsQueueFamilyIndex))
                {
                    Console.WriteLine("Vulkan logical device");
                    Console.WriteLine("---------------------");

                    Console.WriteLine(
                        "Logical device created successfully.");

                    Console.WriteLine(
                        $"Device handle: " +
                        $"0x{logicalDevice.Handle.Handle:X}");

                    Console.WriteLine(
                        $"Graphics queue family: " +
                        $"{logicalDevice.GraphicsQueueFamilyIndex}");

                    Console.WriteLine("Graphics queue index: 0");

                    Queue graphicsQueue =
                        logicalDevice.GraphicsQueue;

                    if (graphicsQueue.Handle == 0)
                    {
                        throw new InvalidOperationException(
                            "The graphics queue handle is null.");
                    }

                    Console.WriteLine(
                        $"Graphics queue handle: " +
                        $"0x{graphicsQueue.Handle:X}");

                    Console.WriteLine(
                        "Graphics queue acquired successfully.");

                    Console.WriteLine();

                    Console.WriteLine(
                        "Waiting for Vulkan device idle...");

                    logicalDevice.WaitIdle();

                    Console.WriteLine(
                        "Vulkan device idle wait succeeded.");
                }

                Console.WriteLine();
                Console.WriteLine(
                    "Vulkan logical device destroyed successfully.");
            }

            else
            {
                Console.WriteLine(
                    "No physical device satisfies VOLT's " +
                    "initial graphics requirements.");
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "Vulkan instance destroyed successfully.");

        return selectedSuccessfully ? 0 : 3;
    }

    private static void PrintDevice(
        int index,
        VulkanPhysicalDeviceInfo device)
    {
        Console.WriteLine($"[{index}] {device.Name}");
        Console.WriteLine(new string('-', 48));

        Console.WriteLine($"Type: {device.DeviceType}");

        Console.WriteLine(
            $"Vendor ID: 0x{device.VendorId:X4}");

        Console.WriteLine(
            $"Device ID: 0x{device.DeviceId:X4}");

        Console.WriteLine(
            $"Vulkan API: " +
            $"{device.ApiMajor}.{device.ApiMinor}.{device.ApiPatch}");

        Console.WriteLine(
            $"Graphics queue family: " +
            $"{device.GraphicsQueueFamilyIndex?.ToString() ?? "None"}");

        Console.WriteLine(
            $"Max 2D texture dimension: " +
            $"{device.MaxTexture2DDimension}");

        Console.WriteLine(
            $"Max color attachments: " +
            $"{device.MaxColorAttachments}");

        Console.WriteLine(
            $"Wireframe support: {device.SupportsWireframe}");

        Console.WriteLine(
            $"Timestamp queries: {device.SupportsTimestampQueries}");

        Console.WriteLine(
            $"Suitable for graphics: {device.IsSuitableForGraphics}");

        Console.WriteLine();
    }

    private static void PrintCapabilities(
        GraphicsDeviceCapabilities capabilities)
    {
        capabilities.Validate();

        Console.WriteLine("VOLT graphics capabilities");
        Console.WriteLine("--------------------------");

        Console.WriteLine(
            $"Backend: {capabilities.Backend}");

        Console.WriteLine(
            $"Adapter: {capabilities.AdapterName}");

        Console.WriteLine(
            $"Max texture dimension: " +
            $"{capabilities.MaxTexture2DDimension}");

        Console.WriteLine(
            $"Max color attachments: " +
            $"{capabilities.MaxColorAttachments}");

        Console.WriteLine(
            $"Wireframe: {capabilities.SupportsWireframe}");

        Console.WriteLine(
            $"Timestamp queries: " +
            $"{capabilities.SupportsTimestampQueries}");
    }

    private static string FormatVersion(uint version)
    {
        uint major = (version >> 22) & 0x7Fu;
        uint minor = (version >> 12) & 0x3FFu;
        uint patch = version & 0xFFFu;

        return $"{major}.{minor}.{patch}";
    }
}
