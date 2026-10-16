
namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Selects a suitable Vulkan physical device using deterministic
/// hardware capability preferences.
/// </summary>
/// <remarks>
/// Selection does not verify window-surface presentation support,
/// required device extensions, or advanced rendering features.
/// Those requirements must be incorporated before creating a
/// presentation-capable logical device.
/// </remarks>
public static class VulkanPhysicalDeviceSelector
{
    /// <summary>
    /// Selects the index of the preferred physical device.
    /// Returns null when no device satisfies the minimum requirements.
    /// </summary>
    /// <param name="devices">
    /// Physical-device snapshots in native enumeration order.
    /// </param>
    /// <param name="requiredApiMajor">
    /// Minimum required Vulkan API major version.
    /// </param>
    /// <param name="requiredApiMinor">
    /// Minimum required Vulkan API minor version.
    /// </param>
    public static int? SelectIndex(
        IReadOnlyList<VulkanPhysicalDeviceInfo> devices,
        uint requiredApiMajor = 1,
        uint requiredApiMinor = 2)
    {
        ArgumentNullException.ThrowIfNull(devices);

        ValidateRequiredVersion(
            requiredApiMajor,
            requiredApiMinor);

        int? selectedIndex = null;

        for (int index = 0; index < devices.Count; index++)
        {
            VulkanPhysicalDeviceInfo candidate = devices[index];

            candidate.Validate();

            if (!candidate.IsSuitableForGraphics)
            {
                continue;
            }

            if (!SupportsRequiredApiVersion(
                candidate,
                requiredApiMajor,
                requiredApiMinor))
            {
                continue;
            }

            if (!selectedIndex.HasValue)
            {
                selectedIndex = index;
                continue;
            }

            VulkanPhysicalDeviceInfo selected =
                devices[selectedIndex.Value];

            if (CompareCandidates(candidate, selected) > 0)
            {
                selectedIndex = index;
            }
        }

        return selectedIndex;
    }

    private static bool SupportsRequiredApiVersion(
        VulkanPhysicalDeviceInfo device,
        uint requiredMajor,
        uint requiredMinor)
    {
        if (device.ApiMajor != requiredMajor)
        {
            return device.ApiMajor > requiredMajor;
        }

        return device.ApiMinor >= requiredMinor;
    }

    /// <summary>
    /// Returns a positive value when the first device is preferred,
    /// a negative value when the second is preferred, or zero when
    /// both have identical selection priorities.
    /// </summary>
    private static int CompareCandidates(
        VulkanPhysicalDeviceInfo first,
        VulkanPhysicalDeviceInfo second)
    {
        int comparison = GetDeviceTypePriority(
            first.DeviceType).CompareTo(
            GetDeviceTypePriority(second.DeviceType));

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.ApiMajor.CompareTo(second.ApiMajor);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.ApiMinor.CompareTo(second.ApiMinor);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.SupportsWireframe.CompareTo(
            second.SupportsWireframe);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.SupportsTimestampQueries.CompareTo(
            second.SupportsTimestampQueries);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.MaxTexture2DDimension.CompareTo(
            second.MaxTexture2DDimension);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = first.MaxColorAttachments.CompareTo(
            second.MaxColorAttachments);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = second.VendorId.CompareTo(first.VendorId);

        if (comparison != 0)
        {
            return comparison;
        }

        comparison = second.DeviceId.CompareTo(first.DeviceId);

        if (comparison != 0)
        {
            return comparison;
        }

        return string.CompareOrdinal(
            second.Name,
            first.Name);
    }

    private static int GetDeviceTypePriority(
        Silk.NET.Vulkan.PhysicalDeviceType deviceType)
    {
        return deviceType switch
        {
            Silk.NET.Vulkan.PhysicalDeviceType.DiscreteGpu => 4,
            Silk.NET.Vulkan.PhysicalDeviceType.IntegratedGpu => 3,
            Silk.NET.Vulkan.PhysicalDeviceType.VirtualGpu => 2,
            Silk.NET.Vulkan.PhysicalDeviceType.Other => 1,
            Silk.NET.Vulkan.PhysicalDeviceType.Cpu => 0,
            _ => 0
        };
    }

    private static void ValidateRequiredVersion(
        uint major,
        uint minor)
    {
        if (major is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(major),
                major,
                "The Vulkan API major version must be between 1 and 127.");
        }

        if (minor > 1023)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minor),
                minor,
                "The Vulkan API minor version cannot exceed 1023.");
        }
    }
}
