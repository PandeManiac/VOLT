
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Owns a Vulkan logical device and its first graphics queue.
/// </summary>
/// <remarks>
/// The parent VulkanInstance must remain alive until this device
/// has been disposed.
///
/// The graphics queue is owned by Vulkan and must not be destroyed
/// separately.
///
/// This implementation creates one graphics queue and enables no
/// optional physical-device features.
///
/// If transfer completion cannot be established, native resources
/// are retained until the device is known to be idle or lost.
///
/// Callers must externally synchronize queue operations and
/// device destruction. This class does not provide general
/// recovery from Vulkan device loss.
/// </remarks>
public sealed unsafe class VulkanLogicalDevice : IDisposable
{
    private const int MaxEnumerationAttempts = 4;

    private readonly Vk _api;
    private readonly VulkanInstance _instance;
    private readonly PhysicalDevice _physicalDevice;
    private readonly MemoryPropertyFlags[] _memoryTypeFlags;
    private readonly VulkanDeferredResourceReleases _retirement = new();
    private readonly object _ticketGate = new();

    private Device _handle;
    private Queue _graphicsQueue;
    private int _activeTransferTickets;
    private bool _disposed;

    /// <summary>
    /// Gets the graphics queue family used by this device.
    /// </summary>
    public uint GraphicsQueueFamilyIndex { get; }

    /// <summary>
    /// Indicates that a transfer may still be using GPU resources.
    /// </summary>
    public bool HasUnresolvedTransfer =>
        _retirement.HasUnresolvedTransfer;

    /// <summary>
    /// Indicates that device loss has been observed.
    /// </summary>
    public bool IsLost => _retirement.IsLost;

    /// <summary>
    /// Gets the borrowed native Vulkan device handle.
    /// </summary>
    public Device Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    /// <summary>
    /// Gets the borrowed graphics queue at index zero.
    /// </summary>
    public Queue GraphicsQueue
    {
        get
        {
            ThrowIfDisposed();
            return _graphicsQueue;
        }
    }

    /// <summary>
    /// Creates a Vulkan logical device with one graphics queue.
    /// </summary>
    public VulkanLogicalDevice(
        VulkanInstance instance,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamilyIndex,
        IReadOnlyList<string>? requiredExtensions = null)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (physicalDevice.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan physical-device handle is required.",
                nameof(physicalDevice));
        }

        string[] extensions = requiredExtensions?.ToArray() ?? [];
        ValidateExtensionNames(extensions);

        _api = instance.Api;
        _instance = instance;

        VerifyPhysicalDevice(instance, physicalDevice);

        VerifyGraphicsQueueFamily(
            _api,
            physicalDevice,
            graphicsQueueFamilyIndex);

        VerifyDeviceExtensions(
            _api,
            physicalDevice,
            extensions);

        PhysicalDeviceMemoryProperties memoryProperties =
            _api.GetPhysicalDeviceMemoryProperties(
                physicalDevice);

        int memoryTypeCount =
            checked((int)memoryProperties.MemoryTypeCount);

        MemoryPropertyFlags[] memoryTypeFlags =
            new MemoryPropertyFlags[memoryTypeCount];

        for (int index = 0; index < memoryTypeCount; index++)
        {
            memoryTypeFlags[index] =
                memoryProperties.MemoryTypes[index].PropertyFlags;
        }

        _physicalDevice = physicalDevice;
        _memoryTypeFlags = memoryTypeFlags;

        Device createdDevice = CreateNativeDevice(
            _api,
            physicalDevice,
            graphicsQueueFamilyIndex,
            extensions);

        try
        {
            _api.GetDeviceQueue(
                createdDevice,
                graphicsQueueFamilyIndex,
                0,
                out Queue graphicsQueue);

            if (graphicsQueue.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null graphics queue.");
            }

            _handle = createdDevice;
            _graphicsQueue = graphicsQueue;

            GraphicsQueueFamilyIndex = graphicsQueueFamilyIndex;
        }

        catch
        {
            _api.DestroyDevice(createdDevice, null);
            throw;
        }
    }

    /// <summary>
    /// Waits until all submitted work on this device has completed.
    /// </summary>
    /// <remarks>
    /// A safe idle result also attempts any pending deferred
    /// resource releases.
    ///
    /// Device loss permits lifetime cleanup, but the lost
    /// device cannot accept new transfers afterward.
    /// </remarks>
    public void WaitIdle()
    {
        ThrowIfDisposed();

        Result result = _api.DeviceWaitIdle(_handle);

        if (CanReclaimAfterWait(result))
        {
            _retirement.Reclaim(result);
        }

        EnsureSuccess(result, "Wait for Vulkan device idle");
    }

    /// <summary>
    /// Destroys the logical device after GPU completion
    /// and child-resource cleanup.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_ticketGate)
        {
            if (_activeTransferTickets != 0)
            {
                throw new InvalidOperationException(
                    "Cannot destroy the Vulkan device while " +
                    "asynchronous transfer tickets are active. " +
                    "Dispose all tickets first.");
            }
        }

        Result result = _api.DeviceWaitIdle(_handle);

        if (!CanReclaimAfterWait(result))
        {
            throw new InvalidOperationException(
                "Cannot destroy the Vulkan device because " +
                $"GPU completion is not established: {result}.");
        }

        _retirement.Reclaim(result);

        if (_retirement.PendingReleaseCount != 0)
        {
            throw new InvalidOperationException(
                "Cannot destroy the Vulkan device while " +
                "deferred resource releases remain pending.");
        }

        _api.DestroyDevice(_handle, null);

        _handle = default;
        _graphicsQueue = default;
        _disposed = true;

        GC.KeepAlive(_instance);
    }

    /// <summary>
    /// Gets cached memory-type properties for this logical
    /// device's physical device.
    /// </summary>
    /// <remarks>
    /// Memory-type properties are cached during construction.
    /// The returned span does not allocate and must not be
    /// retained beyond the logical device's lifetime.
    ///
    /// Resource-specific memory compatibility masks must still
    /// be applied when selecting an allocation memory type.
    /// </remarks>
    internal ReadOnlySpan<MemoryPropertyFlags> GetMemoryTypeFlags(
        PhysicalDevice physicalDevice)
    {
        ThrowIfDisposed();

        if (physicalDevice.Handle != _physicalDevice.Handle)
        {
            throw new ArgumentException(
                "The physical device does not belong to " +
                "this logical device.",
                nameof(physicalDevice));
        }

        return _memoryTypeFlags;
    }

    /// <summary>
    /// Prevents new transfers while execution or cleanup
    /// on this device is unresolved.
    /// </summary>
    internal void EnsureTransferAvailable()
    {
        ThrowIfDisposed();
        _retirement.EnsureTransferAvailable();
    }

    /// <summary>
    /// Marks transfer execution as unresolved.
    /// </summary>
    internal void MarkTransferUnresolved()
    {
        ThrowIfDisposed();
        _retirement.MarkTransferUnresolved();
    }

    /// <summary>
    /// Registers a transfer ticket that owns live Vulkan
    /// command and synchronization resources.
    /// </summary>
    internal void RegisterPendingTransferTicket()
    {
        lock (_ticketGate)
        {
            ThrowIfDisposed();
            _retirement.EnsureTransferAvailable();

            _activeTransferTickets = checked(
                _activeTransferTickets + 1);
        }
    }

    /// <summary>
    /// Unregisters a transfer ticket after its native
    /// resources have been released or safely deferred.
    /// </summary>
    internal void UnregisterPendingTransferTicket()
    {
        lock (_ticketGate)
        {
            if (_activeTransferTickets <= 0)
            {
                throw new InvalidOperationException(
                    "No pending Vulkan transfer ticket " +
                    "is registered with this device.");
            }

            _activeTransferTickets--;
        }
    }

    /// <summary>
    /// Marks the logical device as permanently lost.
    /// </summary>
    internal void MarkDeviceLost()
    {
        ThrowIfDisposed();
        _retirement.MarkDeviceLost();
    }

    /// <summary>
    /// Executes or retains native resource cleanup.
    /// </summary>
    internal void ReleaseOrDefer(Action release)
    {
        ThrowIfDisposed();
        _retirement.ReleaseOrDefer(release);
    }

    /// <summary>
    /// Determines whether a wait result permits resource cleanup.
    /// </summary>
    internal static bool CanReclaimAfterWait(Result result)
    {
        return VulkanDeferredResourceReleases
            .CanReclaimAfterWait(result);
    }

    private static void VerifyPhysicalDevice(
        VulkanInstance instance,
        PhysicalDevice physicalDevice)
    {
        PhysicalDevice[] available =
            instance.EnumeratePhysicalDevices();

        foreach (PhysicalDevice candidate in available)
        {
            if (candidate.Handle == physicalDevice.Handle)
            {
                return;
            }
        }

        throw new ArgumentException(
            "The physical device does not belong to the supplied " +
            "Vulkan instance.",
            nameof(physicalDevice));
    }

    private static void VerifyGraphicsQueueFamily(
        Vk api,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamilyIndex)
    {
        uint count = 0;

        api.GetPhysicalDeviceQueueFamilyProperties(
            physicalDevice,
            ref count,
            null);

        if (graphicsQueueFamilyIndex >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(graphicsQueueFamilyIndex),
                graphicsQueueFamilyIndex,
                "The graphics queue family index does not exist.");
        }

        QueueFamilyProperties[] families =
            new QueueFamilyProperties[checked((int)count)];

        fixed (QueueFamilyProperties* pointer = families)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(
                physicalDevice,
                ref count,
                pointer);
        }

        if (graphicsQueueFamilyIndex >= count)
        {
            throw new InvalidOperationException(
                "The Vulkan queue family list changed during inspection.");
        }

        QueueFamilyProperties family =
            families[checked((int)graphicsQueueFamilyIndex)];

        if (family.QueueCount == 0 ||
            (family.QueueFlags & QueueFlags.GraphicsBit) == 0)
        {
            throw new ArgumentException(
                "The selected queue family does not expose " +
                "an available graphics queue.",
                nameof(graphicsQueueFamilyIndex));
        }
    }

    private static void VerifyDeviceExtensions(
        Vk api,
        PhysicalDevice physicalDevice,
        string[] requiredExtensions)
    {
        if (requiredExtensions.Length == 0)
        {
            return;
        }

        HashSet<string> available = new(StringComparer.Ordinal);
        bool completed = false;

        for (int attempt = 0;
             attempt < MaxEnumerationAttempts;
             attempt++)
        {
            uint count = 0;

            EnsureSuccess(
                api.EnumerateDeviceExtensionProperties(
                    physicalDevice,
                    (byte*)null,
                    ref count,
                    null),
                "Count Vulkan device extensions");

            if (count == 0)
            {
                completed = true;
                break;
            }

            ExtensionProperties[] properties =
                new ExtensionProperties[checked((int)count)];

            fixed (ExtensionProperties* pointer = properties)
            {
                Result result =
                    api.EnumerateDeviceExtensionProperties(
                        physicalDevice,
                        (byte*)null,
                        ref count,
                        pointer);

                if (result == Result.Incomplete)
                {
                    continue;
                }

                EnsureSuccess(
                    result,
                    "Enumerate Vulkan device extensions");

                for (int index = 0; index < count; index++)
                {
                    string? name = Marshal.PtrToStringUTF8(
                        (nint)pointer[index].ExtensionName);

                    if (name is not null)
                    {
                        available.Add(name);
                    }
                }
            }

            completed = true;
            break;
        }

        if (!completed)
        {
            throw new InvalidOperationException(
                "The Vulkan device-extension list kept changing.");
        }

        foreach (string extension in requiredExtensions)
        {
            if (!available.Contains(extension))
            {
                throw new NotSupportedException(
                    $"Required Vulkan device extension " +
                    $"'{extension}' is unavailable.");
            }
        }
    }

    private static Device CreateNativeDevice(
        Vk api,
        PhysicalDevice physicalDevice,
        uint graphicsQueueFamilyIndex,
        string[] extensions)
    {
        List<nint> allocations = [];

        try
        {
            nint[] extensionPointers = new nint[extensions.Length];

            for (int index = 0; index < extensions.Length; index++)
            {
                nint pointer = Marshal.StringToCoTaskMemUTF8(
                    extensions[index]);

                allocations.Add(pointer);
                extensionPointers[index] = pointer;
            }

            float priority = 1.0f;

            DeviceQueueCreateInfo queueCreateInfo = new()
            {
                SType = StructureType.DeviceQueueCreateInfo,
                QueueFamilyIndex = graphicsQueueFamilyIndex,
                QueueCount = 1,
                PQueuePriorities = &priority
            };

            fixed (nint* extensionNames = extensionPointers)
            {
                DeviceCreateInfo createInfo = new()
                {
                    SType = StructureType.DeviceCreateInfo,
                    QueueCreateInfoCount = 1,
                    PQueueCreateInfos = &queueCreateInfo,
                    EnabledExtensionCount = (uint)extensions.Length,
                    PpEnabledExtensionNames =
                        extensions.Length > 0
                            ? (byte**)extensionNames
                            : null,
                    PEnabledFeatures = null
                };

                Result result = api.CreateDevice(
                    physicalDevice,
                    in createInfo,
                    null,
                    out Device device);

                EnsureSuccess(result, "Create Vulkan logical device");

                if (device.Handle == 0)
                {
                    throw new InvalidOperationException(
                        "Vulkan reported successful device creation " +
                        "but returned a null device handle.");
                }

                return device;
            }
        }

        finally
        {
            foreach (nint allocation in allocations)
            {
                Marshal.FreeCoTaskMem(allocation);
            }
        }
    }

    internal static void ValidateExtensionNames(
        string[] extensions)
    {
        HashSet<string> unique = new(StringComparer.Ordinal);

        foreach (string extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                throw new ArgumentException(
                    "Vulkan device extension names cannot be empty.",
                    nameof(extensions));
            }

            if (extension.Contains('\0'))
            {
                throw new ArgumentException(
                    "Vulkan device extension names cannot contain " +
                    "null characters.",
                    nameof(extensions));
            }

            if (!unique.Add(extension))
            {
                throw new ArgumentException(
                    $"Duplicate Vulkan device extension: '{extension}'.",
                    nameof(extensions));
            }
        }
    }

    private static void EnsureSuccess(
        Result result,
        string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result}.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
