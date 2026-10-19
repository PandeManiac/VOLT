using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Graphics.Vulkan.Tests.Memory;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Volt.Graphics.Vulkan.Tests.Rendering;

public sealed class VulkanMeshBuffersTests
{
	[Fact]
	public void CreateMetadata_NonIndexedMeshHasNoIndexBuffer()
	{
		VulkanMeshBuffers.VulkanMeshMetadata metadata =
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 24,
				vertexStride: 32,
				indexCount: 0,
				VkIndexType.Uint16);

		Assert.Equal(24u, metadata.VertexCount);
		Assert.Equal(32u, metadata.VertexStride);
		Assert.Equal(768UL, metadata.VertexBufferSize);
		Assert.Equal(0u, metadata.IndexCount);
		Assert.Equal(0UL, metadata.IndexBufferSize);
		Assert.Equal(VkIndexType.Uint16, metadata.IndexType);
		Assert.False(metadata.IsIndexed);
	}

	[Fact]
	public void CreateMetadata_NonIndexedMeshNormalizesIndexType()
	{
		VulkanMeshBuffers.VulkanMeshMetadata metadata =
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 1,
				vertexStride: 4,
				indexCount: 0,
				VkIndexType.Uint32);

		Assert.Equal(VkIndexType.Uint16, metadata.IndexType);
		Assert.False(metadata.IsIndexed);
	}

	[Fact]
	public void CreateMetadata_UInt16IndicesUseTwoByteSize()
	{
		VulkanMeshBuffers.VulkanMeshMetadata metadata =
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 4,
				vertexStride: 12,
				indexCount: 6,
				VkIndexType.Uint16);

		Assert.Equal(48UL, metadata.VertexBufferSize);
		Assert.Equal(6u, metadata.IndexCount);
		Assert.Equal(12UL, metadata.IndexBufferSize);
		Assert.Equal(VkIndexType.Uint16, metadata.IndexType);
		Assert.True(metadata.IsIndexed);
	}

	[Fact]
	public void CreateMetadata_UInt32IndicesUseFourByteSize()
	{
		VulkanMeshBuffers.VulkanMeshMetadata metadata =
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 5,
				vertexStride: 12,
				indexCount: 9,
				VkIndexType.Uint32);

		Assert.Equal(60UL, metadata.VertexBufferSize);
		Assert.Equal(9u, metadata.IndexCount);
		Assert.Equal(36UL, metadata.IndexBufferSize);
		Assert.Equal(VkIndexType.Uint32, metadata.IndexType);
		Assert.True(metadata.IsIndexed);
	}

	[Fact]
	public void CreateMetadata_RejectsZeroVertices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 0,
				vertexStride: 12,
				indexCount: 0,
				VkIndexType.Uint16));
	}

	[Fact]
	public void CreateMetadata_RejectsZeroVertexStride()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 1,
				vertexStride: 0,
				indexCount: 0,
				VkIndexType.Uint16));
	}

	[Fact]
	public void CreateMetadata_RejectsUnsupportedIndexType()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.CreateMetadata(
				vertexCount: 3,
				vertexStride: 12,
				indexCount: 3,
				(VkIndexType)int.MaxValue));
	}

	[Fact]
	public void GetIndexSize_ReturnsWidthForSupportedTypes()
	{
		Assert.Equal(
			2u,
			VulkanMeshBuffers.GetIndexSize(VkIndexType.Uint16));

		Assert.Equal(
			4u,
			VulkanMeshBuffers.GetIndexSize(VkIndexType.Uint32));
	}

	[Fact]
	public void GetIndexSize_RejectsUnsupportedType()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.GetIndexSize(
				(VkIndexType)int.MaxValue));
	}

	[Fact]
	public void ValidateUploadSize_AcceptsVertexOnlyData()
	{
		VulkanMeshBuffers.ValidateUploadSize(
			vertexByteCount: 12,
			indexByteCount: 0);

		VulkanMeshBuffers.ValidateUploadSize(
			vertexByteCount: 1,
			indexByteCount: 0);
	}

	[Fact]
	public void ValidateUploadSize_AcceptsCombinedData()
	{
		VulkanMeshBuffers.ValidateUploadSize(
			vertexByteCount: 48,
			indexByteCount: 12);

		VulkanMeshBuffers.ValidateUploadSize(
			vertexByteCount: int.MaxValue,
			indexByteCount: 0);
	}

	[Fact]
	public void ValidateUploadSize_RejectsEmptyVertexData()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateUploadSize(
				vertexByteCount: 0,
				indexByteCount: 12));
	}

	[Fact]
	public void ValidateUploadSize_RejectsOversizedVertexData()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateUploadSize(
				vertexByteCount: (ulong)int.MaxValue + 1,
				indexByteCount: 0));
	}

	[Fact]
	public void ValidateUploadSize_RejectsOversizedIndexData()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateUploadSize(
				vertexByteCount: 12,
				indexByteCount: (ulong)int.MaxValue + 1));
	}

	[Fact]
	public void ValidateUploadSize_RejectsOversizedCombinedData()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateUploadSize(
				vertexByteCount: int.MaxValue,
				indexByteCount: 1));
	}

	[Fact]
	public void ValidateIndices_UInt16AcceptsLastVertex()
	{
		ushort[] indices = [0, 1, 2, 3];

		VulkanMeshBuffers.ValidateIndices(indices, vertexCount: 4);
	}

	[Fact]
	public void ValidateIndices_UInt16RejectsEmptyIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateIndices(
				ReadOnlySpan<ushort>.Empty,
				vertexCount: 4));
	}

	[Fact]
	public void ValidateIndices_UInt16RejectsIndexOutsideVertexRange()
	{
		ushort[] indices = [0, 1, 2, 4];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateIndices(
				indices,
				vertexCount: 4));
	}

	[Fact]
	public void ValidateIndices_UInt16RejectsZeroVertexCount()
	{
		ushort[] indices = [0];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateIndices(
				indices,
				vertexCount: 0));
	}

	[Fact]
	public void ValidateIndices_UInt32AcceptsLargeVertexIndex()
	{
		uint[] indices = [0, 65_535];

		VulkanMeshBuffers.ValidateIndices(
			indices,
			vertexCount: 65_536);
	}

	[Fact]
	public void ValidateIndices_UInt32RejectsEmptyIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateIndices(
				ReadOnlySpan<uint>.Empty,
				vertexCount: 10));
	}

	[Fact]
	public void ValidateIndices_UInt32RejectsIndexOutsideVertexRange()
	{
		uint[] indices = [0, 70_000];

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshBuffers.ValidateIndices(
				indices,
				vertexCount: 10));
	}

	[Fact]
	public void Create_RejectsNullInstanceBeforeUploading()
	{
		TestVertex[] vertices = [new(0, 0, 0)];

		Assert.Throws<ArgumentNullException>(() =>
		{
			_ = VulkanMeshBuffers.Create<TestVertex>(
				null!,
				null!,
				default,
				vertices);
		});
	}

	[VulkanIntegrationFact]
	public void Create_NonIndexedUploadsDeviceLocalVertexData()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			TestVertex[] vertices =
			[
				new(0, 0, 0),
				new(1, 0, 0),
				new(0, 1, 0)
			];

			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					BufferUsageFlags.TransferSrcBit);

			Assert.Equal(3u, mesh.VertexCount);
			Assert.Equal(
				(uint)Marshal.SizeOf<TestVertex>(),
				mesh.VertexStride);
			Assert.Equal(
				(ulong)(vertices.Length * Marshal.SizeOf<TestVertex>()),
				mesh.VertexBufferSize);
			Assert.Equal(0u, mesh.IndexCount);
			Assert.Equal(0UL, mesh.IndexBufferSize);
			Assert.False(mesh.IsIndexed);
			Assert.Null(mesh.IndexBuffer);

			Assert.True(
				(mesh.VertexBuffer.Usage &
					BufferUsageFlags.VertexBufferBit) != 0);

			Assert.True(
				(mesh.VertexBuffer.MemoryProperties &
					MemoryPropertyFlags.DeviceLocalBit) != 0);

			byte[] expected = MemoryMarshal
				.AsBytes(vertices.AsSpan())
				.ToArray();

			Assert.Equal(
				expected,
				Readback(
					instance,
					device,
					physicalDevice,
					mesh.VertexBuffer));
		});
	}

	[VulkanIntegrationFact]
	public void Create_UInt16IndexedUploadsBothBuffers()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			TestVertex[] vertices =
			[
				new(0, 0, 0),
				new(1, 0, 0),
				new(0, 1, 0),
				new(1, 1, 0)
			];

			ushort[] indices =
			[
				0, 1, 2,
				2, 1, 3
			];

			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					indices,
					BufferUsageFlags.TransferSrcBit);

			Assert.Equal(4u, mesh.VertexCount);
			Assert.Equal(
				(uint)Marshal.SizeOf<TestVertex>(),
				mesh.VertexStride);
			Assert.Equal(
				(uint)indices.Length,
				mesh.IndexCount);
			Assert.Equal(
				(ulong)(indices.Length * sizeof(ushort)),
				mesh.IndexBufferSize);
			Assert.Equal(VkIndexType.Uint16, mesh.IndexType);
			Assert.True(mesh.IsIndexed);

			VulkanBuffer indexBuffer =
				Assert.IsType<VulkanBuffer>(mesh.IndexBuffer);

			Assert.True(
				(indexBuffer.Usage &
					BufferUsageFlags.IndexBufferBit) != 0);

			Assert.True(
				(indexBuffer.MemoryProperties &
					MemoryPropertyFlags.DeviceLocalBit) != 0);

			Assert.Equal(
				MemoryMarshal
					.AsBytes(vertices.AsSpan())
					.ToArray(),
				Readback(
					instance,
					device,
					physicalDevice,
					mesh.VertexBuffer));

			Assert.Equal(
				MemoryMarshal
					.AsBytes(indices.AsSpan())
					.ToArray(),
				Readback(
					instance,
					device,
					physicalDevice,
					indexBuffer));
		});
	}

	[VulkanIntegrationFact]
	public void Create_UInt32IndexedUploadsBothBuffers()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			TestVertex[] vertices =
			[
				new(0, 0, 0),
				new(1, 0, 0),
				new(0, 1, 0)
			];

			uint[] indices = [0, 1, 2];

			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					indices,
					BufferUsageFlags.TransferSrcBit);

			Assert.Equal(
				(uint)indices.Length,
				mesh.IndexCount);
			Assert.Equal(
				(ulong)(indices.Length * sizeof(uint)),
				mesh.IndexBufferSize);
			Assert.Equal(VkIndexType.Uint32, mesh.IndexType);
			Assert.True(mesh.IsIndexed);

			VulkanBuffer indexBuffer =
				Assert.IsType<VulkanBuffer>(mesh.IndexBuffer);

			Assert.Equal(
				MemoryMarshal
					.AsBytes(indices.AsSpan())
					.ToArray(),
				Readback(
					instance,
					device,
					physicalDevice,
					indexBuffer));
		});
	}

	[VulkanIntegrationFact]
	public void Create_RejectsInvalidInputBeforeUploading()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			TestVertex[] vertices =
			[
				new(0, 0, 0),
				new(1, 0, 0)
			];

			Assert.Throws<ArgumentOutOfRangeException>(() =>
			{
				_ = VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					ReadOnlySpan<TestVertex>.Empty);
			});

			Assert.Throws<ArgumentOutOfRangeException>(() =>
			{
				_ = VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					ReadOnlySpan<ushort>.Empty);
			});

			Assert.Throws<ArgumentOutOfRangeException>(() =>
			{
				_ = VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					new ushort[] { 0, 2 });
			});
		});
	}

	[VulkanIntegrationFact]
	public void Dispose_ReleasesBuffersAndIsIdempotent()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			TestVertex[] vertices =
			[
				new(0, 0, 0),
				new(1, 0, 0),
				new(0, 1, 0)
			];

			ushort[] indices = [0, 1, 2];

			VulkanMeshBuffers mesh = VulkanMeshBuffers.Create(
				instance,
				device,
				physicalDevice,
				vertices,
				indices);

			VulkanBuffer vertexBuffer = mesh.VertexBuffer;

			VulkanBuffer indexBuffer =
				Assert.IsType<VulkanBuffer>(mesh.IndexBuffer);

			mesh.Dispose();
			mesh.Dispose();

			Assert.Throws<ObjectDisposedException>(
				() => _ = mesh.VertexBuffer);

			Assert.Throws<ObjectDisposedException>(
				() => _ = mesh.IndexBuffer);

			Assert.Throws<ObjectDisposedException>(
				() => _ = mesh.VertexCount);

			Assert.Throws<ObjectDisposedException>(
				() => _ = vertexBuffer.Handle);

			Assert.Throws<ObjectDisposedException>(
				() => _ = indexBuffer.Handle);

			using VulkanMeshBuffers second =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					vertices,
					indices);

			Assert.Equal(
				(uint)indices.Length,
				second.IndexCount);
		});
	}

	private readonly struct TestVertex
	{
		public readonly float X;
		public readonly float Y;
		public readonly float Z;

		public TestVertex(float x, float y, float z)
		{
			X = x;
			Y = y;
			Z = z;
		}
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
			ApplicationName: "VOLT Vulkan Mesh Buffer Tests",
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
}
