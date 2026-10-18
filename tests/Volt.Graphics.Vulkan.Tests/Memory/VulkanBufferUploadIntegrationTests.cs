
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

    [VulkanIntegrationFact]
    public void UploadAsync_RoundTripsAfterSourceModification()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[] source = new byte[257];

            for (int index = 0; index < source.Length; index++)
            {
                source[index] = unchecked(
                    (byte)(index * 37 + 11));
            }

            byte[] expected = source.ToArray();

            using VulkanBufferUploadTicket ticket =
                VulkanBufferTransfer.UploadDeviceLocalAsync<byte>(
                    instance,
                    device,
                    physicalDevice,
                    source,
                    UploadUsage);

            Array.Fill(source, (byte)0);
            bool completed = ticket.Wait(0);

            if (!completed)
            {
                Assert.True(ticket.Wait(ulong.MaxValue));
            }

            Assert.True(ticket.IsCompleted);

            using VulkanBuffer uploaded =
                ticket.TakeBuffer();

            Assert.Throws<ObjectDisposedException>(
                () => _ = ticket.IsCompleted);

            byte[] actual = Readback(
                instance,
                device,
                physicalDevice,
                uploaded);

            Assert.Equal(expected, actual);
        });
    }

    [VulkanIntegrationFact]
    public void UploadAsync_MultipleInFlightUploadsPreserveData()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            List<(byte[] Expected, VulkanBufferUploadTicket Ticket)>
                uploads = [];

            try
            {
                for (int uploadIndex = 0;
                     uploadIndex < 16;
                     uploadIndex++)
                {
                    int byteCount = uploadIndex * 7 + 1;
                    byte[] payload = new byte[byteCount];

                    for (int index = 0;
                         index < payload.Length;
                         index++)
                    {
                        payload[index] = unchecked(
                            (byte)(uploadIndex * 19 + index * 13));
                    }

                    VulkanBufferUploadTicket ticket =
                        VulkanBufferTransfer
                            .UploadDeviceLocalAsync<byte>(
                                instance,
                                device,
                                physicalDevice,
                                payload,
                                UploadUsage);

                    uploads.Add((
                        payload.ToArray(),
                        ticket));
                }

                foreach (var upload in uploads)
                {
                    Assert.True(
                        upload.Ticket.Wait(ulong.MaxValue));
                }

                foreach (var upload in uploads)
                {
                    using VulkanBuffer buffer =
                        upload.Ticket.TakeBuffer();

                    byte[] actual = Readback(
                        instance,
                        device,
                        physicalDevice,
                        buffer);

                    Assert.Equal(upload.Expected, actual);
                }
            }

            finally
            {
                foreach (var upload in uploads)
                {
                    upload.Ticket.Dispose();
                }
            }
        });
    }

    [VulkanIntegrationFact]
    public void UploadAsync_EarlyDisposalKeepsDeviceUsable()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[] firstPayload = [1, 2, 3, 4, 5];

            VulkanBufferUploadTicket first =
                VulkanBufferTransfer.UploadDeviceLocalAsync<byte>(
                    instance,
                    device,
                    physicalDevice,
                    firstPayload,
                    UploadUsage);

            try
            {
                Assert.Throws<InvalidOperationException>(
                    device.Dispose);
            }

            finally
            {
                first.Dispose();
            }

            Assert.Throws<ObjectDisposedException>(
                () => first.Wait(0));

            device.WaitIdle();

            byte[] secondPayload =
            [
                0x10, 0x20, 0x30, 0x40,
                0x50, 0x60, 0x70
            ];

            using VulkanBufferUploadTicket second =
                VulkanBufferTransfer.UploadDeviceLocalAsync<byte>(
                    instance,
                    device,
                    physicalDevice,
                    secondPayload,
                    UploadUsage);

            Assert.True(second.Wait(ulong.MaxValue));

            using VulkanBuffer uploaded =
                second.TakeBuffer();

            byte[] actual = Readback(
                instance,
                device,
                physicalDevice,
                uploaded);

            Assert.Equal(secondPayload, actual);
        });
    }

    [VulkanIntegrationFact]
    public void BatchAsync_RoundTripsOddSizedPayloads()
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

            byte[][] expected = payloads
                .Select(payload => payload.ToArray())
                .ToArray();

            VulkanBufferUploadBatch batch = new();

            foreach (byte[] payload in payloads)
            {
                batch.AddBytes(
                    payload,
                    UploadUsage);
            }

            // AddBytes owns a copy of the payload.
            // Modifying the original arrays must not change
            // the data eventually uploaded to the GPU.
            foreach (byte[] payload in payloads)
            {
                Array.Fill(payload, (byte)0);
            }

            using VulkanBufferUploadBatchTicket ticket =
                batch.SubmitAsync(
                    instance,
                    device,
                    physicalDevice);

            Assert.Equal(expected.Length, ticket.UploadCount);

            Assert.Throws<InvalidOperationException>(() =>
                batch.SubmitAsync(
                    instance,
                    device,
                    physicalDevice));

            Assert.Throws<InvalidOperationException>(() =>
                batch.AddBytes(
                    [1],
                    UploadUsage));

            if (!ticket.Wait(0))
            {
                Assert.True(ticket.Wait(ulong.MaxValue));
            }

            Assert.True(ticket.IsCompleted);

            VulkanBuffer[] uploaded = ticket.TakeBuffers();

            try
            {
                Assert.Equal(expected.Length, uploaded.Length);

                for (int index = 0; index < uploaded.Length; index++)
                {
                    byte[] actual = Readback(
                        instance,
                        device,
                        physicalDevice,
                        uploaded[index]);

                    Assert.Equal(expected[index], actual);
                }
            }

            finally
            {
                foreach (VulkanBuffer buffer in uploaded)
                {
                    buffer.Dispose();
                }
            }

            Assert.Throws<ObjectDisposedException>(
                () => _ = ticket.IsCompleted);
        });
    }

    [VulkanIntegrationFact]
    public void BatchAsync_MultipleBatchesRemainIndependent()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            List<(
                byte[][] Expected,
                VulkanBufferUploadBatchTicket Ticket)> uploads = [];

            try
            {
                // All eight batches are submitted before
                // waiting for any of them.
                for (int batchIndex = 0; batchIndex < 8; batchIndex++)
                {
                    byte[][] payloads = new byte[3][];

                    for (int index = 0; index < payloads.Length; index++)
                    {
                        int byteCount =
                            batchIndex * 11 + index * 7 + 1;

                        byte[] payload = new byte[byteCount];

                        for (int offset = 0;
                             offset < payload.Length;
                             offset++)
                        {
                            payload[offset] = unchecked(
                                (byte)(
                                    batchIndex * 29 +
                                    index * 17 +
                                    offset * 13));
                        }

                        payloads[index] = payload;
                    }

                    VulkanBufferUploadBatch batch = new();

                    foreach (byte[] payload in payloads)
                    {
                        batch.AddBytes(
                            payload,
                            UploadUsage);
                    }

                    VulkanBufferUploadBatchTicket ticket =
                        batch.SubmitAsync(
                            instance,
                            device,
                            physicalDevice);

                    uploads.Add((payloads, ticket));
                }

                foreach (var upload in uploads)
                {
                    Assert.True(
                        upload.Ticket.Wait(ulong.MaxValue));
                }

                foreach (var upload in uploads)
                {
                    VulkanBuffer[] buffers =
                        upload.Ticket.TakeBuffers();

                    try
                    {
                        Assert.Equal(
                            upload.Expected.Length,
                            buffers.Length);

                        for (int index = 0;
                             index < buffers.Length;
                             index++)
                        {
                            byte[] actual = Readback(
                                instance,
                                device,
                                physicalDevice,
                                buffers[index]);

                            Assert.Equal(
                                upload.Expected[index],
                                actual);
                        }
                    }

                    finally
                    {
                        foreach (VulkanBuffer buffer in buffers)
                        {
                            buffer.Dispose();
                        }
                    }
                }
            }

            finally
            {
                foreach (var upload in uploads)
                {
                    upload.Ticket.Dispose();
                }
            }
        });
    }

    [VulkanIntegrationFact]
    public void BatchAsync_EarlyDisposalKeepsDeviceUsable()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            VulkanBufferUploadBatch firstBatch = new();

            firstBatch.AddBytes(
                [1, 2, 3],
                UploadUsage);

            firstBatch.AddBytes(
                [4, 5, 6, 7, 8],
                UploadUsage);

            VulkanBufferUploadBatchTicket first =
                firstBatch.SubmitAsync(
                    instance,
                    device,
                    physicalDevice);

            try
            {
                Assert.Throws<InvalidOperationException>(
                    device.Dispose);
            }

            finally
            {
                first.Dispose();
            }

            Assert.Throws<ObjectDisposedException>(
                () => first.Wait(0));

            VulkanBufferUploadBatch secondBatch = new();

            byte[] expected =
            [
                0xA1, 0xB2, 0xC3, 0xD4, 0xE5
            ];

            secondBatch.AddBytes(
                expected,
                UploadUsage);

            using VulkanBufferUploadBatchTicket second =
                secondBatch.SubmitAsync(
                    instance,
                    device,
                    physicalDevice);

            Assert.True(second.Wait(ulong.MaxValue));
            VulkanBuffer[] uploaded = second.TakeBuffers();

            try
            {
                Assert.Single(uploaded);

                byte[] actual = Readback(
                    instance,
                    device,
                    physicalDevice,
                    uploaded[0]);

                Assert.Equal(expected, actual);
            }

            finally
            {
                foreach (VulkanBuffer buffer in uploaded)
                {
                    buffer.Dispose();
                }
            }

            device.WaitIdle();
        });
    }

    [VulkanIntegrationFact]
    public void BatchedUpload_ChunkBoundaryRoundTrips()
    {
        RunWithDevice((instance, device, physicalDevice) =>
        {
            byte[][] payloads =
            [
                new byte[65_535],
                [0xA1, 0xB2, 0xC3],
                new byte[65_537]
            ];

            for (int payloadIndex = 0;
                 payloadIndex < payloads.Length;
                 payloadIndex++)
            {
                byte[] payload = payloads[payloadIndex];

                for (int index = 0; index < payload.Length; index++)
                {
                    payload[index] = unchecked(
                        (byte)(payloadIndex * 23 + index * 17));
                }
            }

            for (int mode = 0; mode < 2; mode++)
            {
                VulkanBufferUploadBatch batch = new();

                foreach (byte[] payload in payloads)
                {
                    batch.AddBytes(payload, UploadUsage);
                }

                VulkanBuffer[] uploaded;

                if (mode == 0)
                {
                    uploaded = batch.Submit(
                        instance,
                        device,
                        physicalDevice);
                }

                else
                {
                    using VulkanBufferUploadBatchTicket ticket =
                        batch.SubmitAsync(
                            instance,
                            device,
                            physicalDevice);

                    Assert.True(ticket.Wait(ulong.MaxValue));

                    uploaded = ticket.TakeBuffers();
                }

                try
                {
                    Assert.Equal(payloads.Length, uploaded.Length);

                    for (int index = 0;
                         index < uploaded.Length;
                         index++)
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
            }
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
