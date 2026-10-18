
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;

namespace Volt.Graphics.Vulkan.Tests.Memory;

public sealed class VulkanBufferUploadIntegrationTests
{
    private const BufferUsageFlags UploadUsage =
        BufferUsageFlags.TransferSrcBit |
        BufferUsageFlags.StorageBufferBit;

    [VulkanIntegrationFact]
    public void UploadDeviceLocal_RoundTripsBytes()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[] expected = new byte[257];

            for (int index = 0; index < expected.Length; index++)
            {
                expected[index] = unchecked(
                    (byte)(index * 37 + 11));
            }

            using VulkanBuffer uploaded =
                VulkanBufferTransfer.UploadDeviceLocal<byte>(
                    instance,
                    device,
                    physicalDevice,
                    expected,
                    UploadUsage);

            byte[] actual = Readback(
                instance,
                device,
                physicalDevice,
                uploaded);

            Assert.Equal(expected, actual);
        });
    }

    [VulkanIntegrationFact]
    public void BatchedUpload_RoundTripsOddSizedPayloads()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[][] payloads =
            [
                [0x5A],
                [0x10, 0x20, 0x30],
                [0x01, 0x02, 0x03, 0x04, 0x05],
                [0xFF, 0x00, 0x7F, 0x80, 0x11, 0x22, 0x33]
            ];

            VulkanBufferUploadBatch batch = new();

            foreach (byte[] payload in payloads)
            {
                batch.AddBytes(payload, UploadUsage);
            }

            VulkanBuffer[] uploaded = batch.Submit(
                instance,
                device,
                physicalDevice);

            try
            {
                Assert.Equal(payloads.Length, uploaded.Length);

                for (int index = 0; index < payloads.Length; index++)
                {
                    byte[] actual = Readback(
                        instance,
                        device,
                        physicalDevice,
                        uploaded[index]);

                    Assert.Equal(payloads[index], actual);
                }
            }

            finally
            {
                foreach (VulkanBuffer buffer in uploaded)
                {
                    buffer.Dispose();
                }
            }
        });
    }

    [VulkanIntegrationFact]
    public void StagingCache_ReusesAndGrowsAcrossBatches()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            using VulkanStagingBufferCache cache = new(
                instance,
                device,
                physicalDevice);

            byte[] firstPayload = [0xA1, 0xB2, 0xC3];

            VulkanBufferUploadBatch firstBatch = new();

            firstBatch.AddBytes(
                firstPayload,
                UploadUsage);

            using (VulkanBuffer first = firstBatch.Submit(
                instance,
                device,
                physicalDevice,
                cache)[0])
            {
                Assert.Equal(
                    firstPayload,
                    Readback(
                        instance,
                        device,
                        physicalDevice,
                        first));
            }

            ulong initialCapacity = cache.Capacity;

            Assert.True(initialCapacity >= 3);

            byte[] secondPayload = [0x7E];

            VulkanBufferUploadBatch secondBatch = new();

            secondBatch.AddBytes(
                secondPayload,
                UploadUsage);

            using (VulkanBuffer second = secondBatch.Submit(
                instance,
                device,
                physicalDevice,
                cache)[0])
            {
                Assert.Equal(
                    secondPayload,
                    Readback(
                        instance,
                        device,
                        physicalDevice,
                        second));
            }

            Assert.Equal(initialCapacity, cache.Capacity);

            byte[] thirdPayload = new byte[257];

            for (int index = 0; index < thirdPayload.Length; index++)
            {
                thirdPayload[index] = unchecked(
                    (byte)(index * 13 + 9));
            }

            VulkanBufferUploadBatch thirdBatch = new();

            thirdBatch.AddBytes(
                thirdPayload,
                UploadUsage);

            using (VulkanBuffer third = thirdBatch.Submit(
                instance,
                device,
                physicalDevice,
                cache)[0])
            {
                Assert.Equal(
                    thirdPayload,
                    Readback(
                        instance,
                        device,
                        physicalDevice,
                        third));
            }

            Assert.True(cache.Capacity > initialCapacity);
            Assert.True(cache.Capacity >= (ulong)thirdPayload.Length);
        });
    }

    [VulkanIntegrationFact]
    public void Copy_SupportsByteGranularOffsets()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            using VulkanBuffer source = new(
                instance,
                device,
                physicalDevice,
                9,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit);

            using VulkanBuffer destination = new(
                instance,
                device,
                physicalDevice,
                11,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit);

            source.Write(
                new byte[]
                {
                    1, 2, 3, 4, 5, 6, 7, 8, 9
                });

            byte[] expected =
            [
                0xEE, 0xEE, 0xEE, 0xEE, 0xEE,
                3, 4, 5,
                0xEE, 0xEE, 0xEE
            ];

            destination.Write(
                new byte[]
                {
                    0xEE, 0xEE, 0xEE, 0xEE, 0xEE,
                    0xEE, 0xEE, 0xEE, 0xEE, 0xEE,
                    0xEE
                });

            VulkanBufferTransfer.Copy(
                instance,
                device,
                source,
                destination,
                byteCount: 3,
                sourceOffset: 2,
                destinationOffset: 5);

            byte[] actual = new byte[11];

            destination.Read(actual);

            Assert.Equal(expected, actual);
        });
    }

    private static byte[] Readback(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        VulkanBuffer source)
    {
        using VulkanBuffer readback = new(
            instance,
            device,
            physicalDevice,
            source.Size,
            BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.HostVisibleBit,
            MemoryPropertyFlags.HostCachedBit |
                MemoryPropertyFlags.HostCoherentBit);

        VulkanBufferTransfer.Copy(
            instance,
            device,
            source,
            readback,
            source.Size);

        byte[] actual = new byte[checked((int)source.Size)];

        readback.Read(actual);

        return actual;
    }

    private static void RunWithDevice(
        Action<VulkanInstance, VulkanLogicalDevice, PhysicalDevice> test)
    {
        bool enableValidation =
            Environment.GetEnvironmentVariable(
                "VOLT_VULKAN_VALIDATION") == "1";

        VulkanInstanceDescription description = new(
            ApplicationName: "VOLT Vulkan Buffer Integration Tests",
            EnableValidation: enableValidation);

        using VulkanInstance instance = new(description);

        PhysicalDevice[] nativeDevices =
            instance.EnumeratePhysicalDevices();

        VulkanPhysicalDeviceInfo[] devices =
            new VulkanPhysicalDeviceInfo[nativeDevices.Length];

        for (int index = 0; index < nativeDevices.Length; index++)
        {
            devices[index] =
                VulkanPhysicalDeviceInspector.Inspect(
                    instance,
                    nativeDevices[index]);
        }

        int? selectedIndex =
            VulkanPhysicalDeviceSelector.SelectIndex(
                devices,
                requiredApiMajor: description.ApiMajor,
                requiredApiMinor: description.ApiMinor);

        if (selectedIndex is not int indexOfSelected)
        {
            throw new InvalidOperationException(
                "No compatible Vulkan graphics device was found.");
        }

        VulkanPhysicalDeviceInfo selected =
            devices[indexOfSelected];

        uint queueFamilyIndex =
            selected.GraphicsQueueFamilyIndex ??
            throw new InvalidOperationException(
                "The selected device has no graphics queue.");

        PhysicalDevice physicalDevice =
            nativeDevices[indexOfSelected];

        using VulkanLogicalDevice device = new(
            instance,
            physicalDevice,
            queueFamilyIndex);

        test(
            instance,
            device,
            physicalDevice);
    }

    [VulkanIntegrationFact]
    public void CopyAsync_RoundTripsByteGranularData()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[] sourceData =
            [
                10, 20, 30, 40, 50, 60, 70, 80, 90
            ];

            using VulkanBuffer source = new(
                instance,
                device,
                physicalDevice,
                (ulong)sourceData.Length,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit);

            using VulkanBuffer destination = new(
                instance,
                device,
                physicalDevice,
                11,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit);

            source.Write(sourceData);

            destination.Write(
                new byte[]
                {
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
                });

            using (VulkanBufferCopyTicket ticket =
                VulkanBufferTransfer.CopyAsync(
                    instance,
                    device,
                    source,
                    destination,
                    byteCount: 5,
                    sourceOffset: 1,
                    destinationOffset: 3))
            {
                // A zero-timeout wait may succeed immediately,
                // depending on GPU scheduling.
                bool completed = ticket.Wait(0);

                if (!completed)
                {
                    Assert.True(ticket.Wait(ulong.MaxValue));
                }

                Assert.True(ticket.IsCompleted);
                Assert.True(ticket.Wait(0));
            }

            byte[] actual = new byte[11];

            destination.Read(actual);

            Assert.Equal(
                new byte[]
                {
                    0, 0, 0, 20, 30, 40, 50, 60, 0, 0, 0
                },
                actual);
        });
    }

    [VulkanIntegrationFact]
    public void CopyAsync_TicketPreventsPrematureDeviceDestruction()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            using VulkanBuffer source = new(
                instance,
                device,
                physicalDevice,
                4,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit);

            using VulkanBuffer destination = new(
                instance,
                device,
                physicalDevice,
                4,
                BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit);

            source.Write(
                new byte[] { 1, 2, 3, 4 });

            VulkanBufferCopyTicket ticket =
                VulkanBufferTransfer.CopyAsync(
                    instance,
                    device,
                    source,
                    destination,
                    4);

            try
            {
                Assert.Throws<InvalidOperationException>(
                    device.Dispose);

                Assert.True(ticket.Wait(ulong.MaxValue));
            }

            finally
            {
                ticket.Dispose();
            }

            byte[] actual = new byte[4];

            destination.Read(actual);

            Assert.Equal(
                new byte[] { 1, 2, 3, 4 },
                actual);

            device.WaitIdle();
        });
    }

}

/// <summary>
/// Enables native Vulkan integration tests only when explicitly
/// requested through the environment.
/// </summary>
internal sealed class VulkanIntegrationFactAttribute : FactAttribute
{
    public VulkanIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(
            "VOLT_VULKAN_INTEGRATION") != "1")
        {
            Skip =
                "Set VOLT_VULKAN_INTEGRATION=1 to run native " +
                "Vulkan integration tests.";
        }
    }
}
