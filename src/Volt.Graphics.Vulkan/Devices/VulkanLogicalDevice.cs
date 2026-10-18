
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
/// If a transfer's completion cannot be established, resources
/// awaiting destruction are retained until the device is known
/// to be idle or lost.
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
    private readonly object _retirementGate = new();
    private readonly List<Action> _deferredReleases = [];

    private Device _handle;
    private Queue _graphicsQueue;

    private bool _transferUnresolved;
    private bool _deviceLost;
    private bool _disposed;

    /// <summary>
    /// Gets the graphics queue family used by this device.
    /// </summary>
    public uint GraphicsQueueFamilyIndex { get; }

    /// <summary>
    /// Indicates that a transfer may still be using GPU resources.
    /// </summary>
    public bool HasUnresolvedTransfer
    {
        get
        {
            lock (_retirementGate)
            {
                return _transferUnresolved;
            }
        }
    }

    /// <summary>
    /// Indicates that device loss has been observed.
    /// </summary>
    public bool IsLost
    {
        get
        {
            lock (_retirementGate)
            {
                return _deviceLost;
            }
        }
    }

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
    /// Successful completion releases any resources retained after
    /// an earlier unresolved transfer.
    ///
    /// A device-lost result also permits resource reclamation,
    /// but leaves the device permanently unavailable for new work.
    /// </remarks>
    public void WaitIdle()
    {
        ThrowIfDisposed();

        Result result = _api.DeviceWaitIdle(_handle);

        if (CanReclaimAfterWait(result))
        {
            ReleaseDeferredResources(result);
        }

        EnsureSuccess(result, "Wait for Vulkan device idle");
    }

    /// <summary>
    /// Destroys the device after safely releasing deferred resources.
    /// </summary>
    /// <remarks>
    /// If device-idle waiting returns neither success nor device
    /// loss, destruction is refused because pending work may
    /// still reference the retained native resources.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Result result = _api.DeviceWaitIdle(_handle);

        if (!CanReclaimAfterWait(result))
        {
            throw new InvalidOperationException(
                "Cannot destroy the Vulkan device because " +
                $"GPU completion is not established: {result}.");
        }

        ReleaseDeferredResources(result);

        _api.DestroyDevice(_handle, null);

        _handle = default;
        _graphicsQueue = default;
        _disposed = true;

        GC.KeepAlive(_instance);
    }

    /// <summary>
    /// Rejects transfers while completion is uncertain or the
    /// logical device has been lost.
    /// </summary>
    internal void EnsureTransferAvailable()
    {
        lock (_retirementGate)
        {
            ThrowIfDisposed();

            if (_deviceLost)
            {
                throw new InvalidOperationException(
                    "The Vulkan device has been lost and " +
                    "cannot accept new transfers.");
            }

            if (_transferUnresolved)
            {
                throw new InvalidOperationException(
                    "A previous Vulkan transfer has unresolved " +
                    "GPU execution. Wait for device idle or " +
                    "recreate the device before submitting more work.");
            }
        }
    }

    /// <summary>
    /// Marks pending transfer execution as unresolved.
    /// </summary>
    internal void MarkTransferUnresolved()
    {
        lock (_retirementGate)
        {
            ThrowIfDisposed();
            _transferUnresolved = true;
        }
    }

    /// <summary>
    /// Records device loss and prevents further transfer submissions.
    /// </summary>
    internal void MarkDeviceLost()
    {
        lock (_retirementGate)
        {
            ThrowIfDisposed();
            _deviceLost = true;
        }
    }

    /// <summary>
    /// Executes resource destruction immediately when safe,
    /// otherwise retains it until GPU completion is established.
    /// </summary>
    internal void ReleaseOrDefer(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);

        lock (_retirementGate)
        {
            ThrowIfDisposed();

            if (_transferUnresolved)
            {
                _deferredReleases.Add(release);
                return;
            }
        }

        release();
    }

    /// <summary>
    /// Whether a device-idle result permits destruction of
    /// resources potentially referenced by pending commands.
    /// </summary>
    internal static bool CanReclaimAfterWait(Result result)
    {
        return result is
            Result.Success or Result.ErrorDeviceLost;
    }

    private void ReleaseDeferredResources(Result result)
    {
        Action[] releases;

        lock (_retirementGate)
        {
            if (result == Result.ErrorDeviceLost)
            {
                _deviceLost = true;
            }

            _transferUnresolved = false;

            releases = _deferredReleases.ToArray();
            _deferredReleases.Clear();
        }

        foreach (Action release in releases)
        {
            release();
        }
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

    private static Result _CreateDevice(
        Vk api,
        PhysicalDevice physicalDevice,
        in DeviceCreateInfo createInfo,
        out Device device)
    {
        return api.CreateDevice(
            physicalDevice,
            in createInfo,
            null,
            out device);
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

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
