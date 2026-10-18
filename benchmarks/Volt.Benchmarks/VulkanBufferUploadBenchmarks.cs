
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Benchmarks;

/// <summary>
/// Defines representative Vulkan buffer-upload workloads.
/// </summary>
public enum VulkanUploadWorkload
{
    Tiny_1000x64,
    Small_256x4KiB,
    Medium_32x64KiB,
    Large_8x1MiB
}

/// <summary>
/// Compares synchronous and asynchronous Vulkan buffer uploads.
/// </summary>
/// <remarks>
/// All benchmarks include upload preparation, GPU completion,
/// and destination resource disposal.
///
/// Vulkan instance and device creation happen during GlobalSetup
/// and are excluded from the measured operation.
///
/// These are end-to-end upload benchmarks rather than isolated
/// GPU memory-bandwidth measurements.
///
/// Native Vulkan benchmarks must be explicitly enabled through
/// VOLT_VULKAN_BENCHMARKS=1.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(VulkanUploadBenchmarkConfig))]
public class VulkanBufferUploadBenchmarks
{
    private const BufferUsageFlags DestinationUsage =
        BufferUsageFlags.StorageBufferBit;

    private VulkanInstance? _instance;
    private VulkanLogicalDevice? _device;
    private VulkanStagingBufferCache? _stagingCache;

    private PhysicalDevice _physicalDevice;
    private byte[][] _payloads = [];
    private ulong _totalBytes;

    /// <summary>
    /// Gets or sets the workload selected by BenchmarkDotNet.
    /// </summary>
    [Params(
        VulkanUploadWorkload.Tiny_1000x64,
        VulkanUploadWorkload.Small_256x4KiB,
        VulkanUploadWorkload.Medium_32x64KiB,
        VulkanUploadWorkload.Large_8x1MiB)]
    public VulkanUploadWorkload Workload { get; set; }

    /// <summary>
    /// Creates the Vulkan device and deterministic input data.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        if (Environment.GetEnvironmentVariable(
            "VOLT_VULKAN_BENCHMARKS") != "1")
        {
            throw new InvalidOperationException(
                "Set VOLT_VULKAN_BENCHMARKS=1 to run " +
                "native Vulkan upload benchmarks.");
        }

        (int bufferCount, int bufferBytes) =
            GetWorkloadConfiguration(Workload);

        _payloads = new byte[bufferCount][];

        for (int index = 0; index < bufferCount; index++)
        {
            byte[] payload = new byte[bufferBytes];

            for (int offset = 0; offset < payload.Length; offset++)
            {
                payload[offset] = unchecked(
                    (byte)(index * 37 + offset * 13));
            }

            _payloads[index] = payload;
        }

        _totalBytes = checked(
            (ulong)bufferCount * (ulong)bufferBytes);

        bool enableValidation =
            Environment.GetEnvironmentVariable(
                "VOLT_VULKAN_VALIDATION") == "1";

        VulkanInstanceDescription description = new(
            ApplicationName: "VOLT Vulkan Upload Benchmarks",
            EnableValidation: enableValidation);

        try
        {
            _instance = new VulkanInstance(description);

            PhysicalDevice[] nativeDevices =
                _instance.EnumeratePhysicalDevices();

            VulkanPhysicalDeviceInfo[] devices =
                new VulkanPhysicalDeviceInfo[nativeDevices.Length];

            for (int index = 0; index < nativeDevices.Length; index++)
            {
                devices[index] =
                    VulkanPhysicalDeviceInspector.Inspect(
                        _instance,
                        nativeDevices[index]);
            }

            int? selectedIndex =
                VulkanPhysicalDeviceSelector.SelectIndex(
                    devices,
                    requiredApiMajor: description.ApiMajor,
                    requiredApiMinor: description.ApiMinor);

            if (selectedIndex is not int selected)
            {
                throw new InvalidOperationException(
                    "No compatible Vulkan graphics device " +
                    "was found for benchmarking.");
            }

            VulkanPhysicalDeviceInfo selectedDevice =
                devices[selected];

            uint queueFamilyIndex =
                selectedDevice.GraphicsQueueFamilyIndex ??
                throw new InvalidOperationException(
                    "The selected device has no graphics queue.");

            _physicalDevice = nativeDevices[selected];

            _device = new VulkanLogicalDevice(
                _instance,
                _physicalDevice,
                queueFamilyIndex);

            _stagingCache = new VulkanStagingBufferCache(
                _instance,
                _device,
                _physicalDevice);
        }

        catch
        {
            Cleanup();
            throw;
        }
    }

    /// <summary>
    /// Releases the staging cache and native Vulkan objects.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _stagingCache?.Dispose();
        _stagingCache = null;

        _device?.Dispose();
        _device = null;

        _instance?.Dispose();
        _instance = null;

        _physicalDevice = default;
        _payloads = [];
        _totalBytes = 0;
    }

    /// <summary>
    /// Submits a separate asynchronous upload for every buffer.
    /// </summary>
    /// <remarks>
    /// All uploads are submitted before waiting for completion.
    /// This includes one staging allocation, destination allocation,
    /// command buffer, and fence per individual upload.
    /// </remarks>
    [Benchmark(Baseline = true)]
    public ulong IndividualAsync()
    {
        VulkanInstance instance = _instance!;
        VulkanLogicalDevice device = _device!;

        VulkanBufferUploadTicket?[] tickets =
            new VulkanBufferUploadTicket?[_payloads.Length];

        try
        {
            for (int index = 0; index < _payloads.Length; index++)
            {
                tickets[index] =
                    VulkanBufferTransfer.UploadDeviceLocalAsync<byte>(
                        instance,
                        device,
                        _physicalDevice,
                        _payloads[index],
                        DestinationUsage);
            }

            foreach (VulkanBufferUploadTicket? ticket in tickets)
            {
                if (!ticket!.Wait(ulong.MaxValue))
                {
                    throw new InvalidOperationException(
                        "An unlimited upload wait timed out.");
                }
            }

            foreach (VulkanBufferUploadTicket? ticket in tickets)
            {
                using VulkanBuffer destination =
                    ticket!.TakeBuffer();
            }
        }

        finally
        {
            foreach (VulkanBufferUploadTicket? ticket in tickets)
            {
                ticket?.Dispose();
            }
        }

        return _totalBytes;
    }

    /// <summary>
    /// Submits all uploads through one asynchronous batch.
    /// </summary>
    [Benchmark]
    public ulong BatchedAsync()
    {
        VulkanBufferUploadBatch batch = CreateBatch();

        using VulkanBufferUploadBatchTicket ticket =
            batch.SubmitAsync(
                _instance!,
                _device!,
                _physicalDevice);

        if (!ticket.Wait(ulong.MaxValue))
        {
            throw new InvalidOperationException(
                "An unlimited batched upload wait timed out.");
        }

        VulkanBuffer[] destinations = ticket.TakeBuffers();
        DisposeDestinations(destinations);
        return _totalBytes;
    }

    /// <summary>
    /// Submits a synchronous batch with temporary staging storage.
    /// </summary>
    [Benchmark]
    public ulong BatchedSynchronous()
    {
        VulkanBufferUploadBatch batch = CreateBatch();

        VulkanBuffer[] destinations = batch.Submit(
            _instance!,
            _device!,
            _physicalDevice);

        DisposeDestinations(destinations);

        return _totalBytes;
    }

    /// <summary>
    /// Submits a synchronous batch using a retained staging buffer.
    /// </summary>
    [Benchmark]
    public ulong BatchedSynchronousCached()
    {
        VulkanBufferUploadBatch batch = CreateBatch();

        VulkanBuffer[] destinations = batch.Submit(
            _instance!,
            _device!,
            _physicalDevice,
            _stagingCache!);

        DisposeDestinations(destinations);

        return _totalBytes;
    }

    /// <summary>
    /// Measures creation and destruction of all device-local
    /// destination buffers without staging or GPU submission.
    /// </summary>
    /// <remarks>
    /// All buffers remain allocated until the final buffer has
    /// been created, matching the peak destination allocation
    /// count of a completed upload batch.
    /// </remarks>
    [Benchmark]
    public ulong DestinationAllocationsOnly()
    {
        VulkanInstance instance = _instance!;
        VulkanLogicalDevice device = _device!;

        VulkanBuffer?[] destinations =
            new VulkanBuffer?[_payloads.Length];

        try
        {
            for (int index = 0; index < _payloads.Length; index++)
            {
                destinations[index] = new VulkanBuffer(
                    instance,
                    device,
                    _physicalDevice,
                    (ulong)_payloads[index].Length,
                    DestinationUsage |
                        BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);
            }
        }

        finally
        {
            foreach (VulkanBuffer? destination in destinations)
            {
                destination?.Dispose();
            }
        }

        return _totalBytes;
    }

    /// <summary>
    /// Measures creating and destroying one host-visible
    /// Vulkan staging buffer.
    /// </summary>
    /// <remarks>
    /// Does not copy payload data, record commands, or submit
    /// GPU work.
    /// </remarks>
    [Benchmark]
    public ulong StagingAllocationOnly()
    {
        using VulkanBuffer staging = new(
            _instance!,
            _device!,
            _physicalDevice,
            _totalBytes,
            BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCoherentBit);

        return staging.Size;
    }

    /// <summary>
    /// Measures managed upload-batch construction without
    /// Vulkan allocation or GPU submission.
    /// </summary>
    [Benchmark]
    public ulong ManagedBatchPreparationOnly()
    {
        VulkanBufferUploadBatch batch = CreateBatch();
        ulong byteCount = batch.StagingByteCount;
        GC.KeepAlive(batch);
        return byteCount;
    }

    /// <summary>
    /// Measures shared device-memory allocation and independent
    /// destination-buffer creation and destruction.
    /// </summary>
    [Benchmark]
    public ulong SharedDestinationAllocationsOnly()
    {
        VulkanBufferAllocationRequest[] requests =
            new VulkanBufferAllocationRequest[_payloads.Length];

        for (int index = 0; index < requests.Length; index++)
        {
            requests[index] = new VulkanBufferAllocationRequest(
                (ulong)_payloads[index].Length,
                DestinationUsage |
                    BufferUsageFlags.TransferDstBit);
        }

        VulkanBuffer[] destinations =
            VulkanBatchDestinationAllocator.Create(
                _instance!,
                _device!,
                _physicalDevice,
                requests);

        try
        {
            return _totalBytes;
        }

        finally
        {
            foreach (VulkanBuffer destination in destinations)
            {
                destination.Dispose();
            }
        }
    }

    private VulkanBufferUploadBatch CreateBatch()
    {
        VulkanBufferUploadBatch batch = new();

        foreach (byte[] payload in _payloads)
        {
            batch.AddBytes(
                payload,
                DestinationUsage);
        }

        return batch;
    }

    private static void DisposeDestinations(
        VulkanBuffer[] destinations)
    {
        foreach (VulkanBuffer destination in destinations)
        {
            destination.Dispose();
        }
    }

    private static (int BufferCount, int BufferBytes)
        GetWorkloadConfiguration(VulkanUploadWorkload workload)
    {
        return workload switch
        {
            VulkanUploadWorkload.Tiny_1000x64 =>
                (1_000, 64),

            VulkanUploadWorkload.Small_256x4KiB =>
                (256, 4 * 1024),

            VulkanUploadWorkload.Medium_32x64KiB =>
                (32, 64 * 1024),

            VulkanUploadWorkload.Large_8x1MiB =>
                (8, 1024 * 1024),

            _ => throw new ArgumentOutOfRangeException(
                nameof(workload))
        };
    }
}

/// <summary>
/// Runs Vulkan upload benchmarks in the current process so
/// unrelated benchmark projects do not affect project discovery.
/// </summary>
public sealed class VulkanUploadBenchmarkConfig : ManualConfig
{
    public VulkanUploadBenchmarkConfig()
    {
        AddJob(
            Job.Default
                .WithToolchain(InProcessEmitToolchain.Instance)
                .WithStrategy(RunStrategy.Monitoring)
                .WithLaunchCount(1)
                .WithWarmupCount(2)
                .WithIterationCount(10)
                .WithInvocationCount(1)
                .WithUnrollFactor(1)
                .WithId("VulkanInProcess"));
    }
}
