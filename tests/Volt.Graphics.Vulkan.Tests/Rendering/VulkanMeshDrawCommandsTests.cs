using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Graphics.Vulkan.Tests.Memory;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Volt.Graphics.Vulkan.Tests.Rendering;

public sealed class VulkanMeshDrawCommandsTests
{
	[Fact]
	public void CreateDrawInfo_NonIndexedMeshSelectsDraw()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: false,
				vertexCount: 6,
				indexCount: 0,
				VkIndexType.Uint16,
				instanceCount: 1);

		Assert.False(info.IsIndexed);
		Assert.Equal(6u, info.VertexCount);
		Assert.Equal(0u, info.IndexCount);
		Assert.Equal(VkIndexType.Uint16, info.IndexType);
		Assert.Equal(1u, info.InstanceCount);
	}

	[Fact]
	public void CreateDrawInfo_NonIndexedMeshIgnoresIndexType()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: false,
				vertexCount: 3,
				indexCount: 0,
				VkIndexType.Uint32,
				instanceCount: 2);

		Assert.False(info.IsIndexed);
		Assert.Equal(VkIndexType.Uint16, info.IndexType);
	}

	[Fact]
	public void CreateDrawInfo_UInt16IndexedMeshSelectsDrawIndexed()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: 4,
				indexCount: 6,
				VkIndexType.Uint16,
				instanceCount: 3);

		Assert.True(info.IsIndexed);
		Assert.Equal(4u, info.VertexCount);
		Assert.Equal(6u, info.IndexCount);
		Assert.Equal(VkIndexType.Uint16, info.IndexType);
		Assert.Equal(3u, info.InstanceCount);
	}

	[Fact]
	public void CreateDrawInfo_UInt32IndexedMeshSelectsDrawIndexed()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: 65_536,
				indexCount: 9,
				VkIndexType.Uint32,
				instanceCount: 1);

		Assert.True(info.IsIndexed);
		Assert.Equal(65_536u, info.VertexCount);
		Assert.Equal(9u, info.IndexCount);
		Assert.Equal(VkIndexType.Uint32, info.IndexType);
	}

	[Fact]
	public void CreateDrawInfo_PreservesMaximumCounts()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: uint.MaxValue,
				indexCount: uint.MaxValue,
				VkIndexType.Uint32,
				instanceCount: uint.MaxValue);

		Assert.Equal(uint.MaxValue, info.VertexCount);
		Assert.Equal(uint.MaxValue, info.IndexCount);
		Assert.Equal(uint.MaxValue, info.InstanceCount);
	}

	[Fact]
	public void CreateDrawInfo_RejectsZeroVertices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: false,
				vertexCount: 0,
				indexCount: 0,
				VkIndexType.Uint16,
				instanceCount: 1));
	}

	[Fact]
	public void CreateDrawInfo_RejectsZeroInstanceCount()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: false,
				vertexCount: 3,
				indexCount: 0,
				VkIndexType.Uint16,
				instanceCount: 0));
	}

	[Fact]
	public void CreateDrawInfo_RejectsIndexedMeshWithoutIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: 3,
				indexCount: 0,
				VkIndexType.Uint16,
				instanceCount: 1));
	}

	[Fact]
	public void CreateDrawInfo_RejectsNonIndexedMeshWithIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: false,
				vertexCount: 3,
				indexCount: 3,
				VkIndexType.Uint16,
				instanceCount: 1));
	}

	[Fact]
	public void CreateDrawInfo_RejectsUnsupportedIndexType()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: 3,
				indexCount: 3,
				(VkIndexType)int.MaxValue,
				instanceCount: 1));
	}

	[Fact]
	public void Record_RejectsNullInstance()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanMeshDrawCommands.Record(
				null!,
				new CommandBuffer(1),
				null!));
	}

	[VulkanIntegrationFact]
	public void BindMeshBuffers_NonIndexedRecordsValidBindings()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices());

			RecordBindings(instance, device, mesh);
		});
	}

	[VulkanIntegrationFact]
	public void BindMeshBuffers_UInt16IndexedRecordsValidBindings()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			ushort[] indices = [0, 1, 2];

			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices(),
					indices);

			RecordBindings(instance, device, mesh);
		});
	}

	[VulkanIntegrationFact]
	public void BindMeshBuffers_UInt32IndexedRecordsValidBindings()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			uint[] indices = [0, 1, 2];

			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices(),
					indices);

			RecordBindings(instance, device, mesh);
		});
	}

	[VulkanIntegrationFact]
	public void Record_RejectsInvalidArgumentsBeforeRecording()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			using VulkanMeshBuffers mesh =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices());

			using VulkanCommandPool pool = new(instance, device);

			using VulkanCommandBuffers commandBuffers = new(
				instance,
				device,
				pool,
				count: 1);

			CommandBuffer commandBuffer =
				commandBuffers.GetBuffer(0);

			Assert.Throws<ArgumentNullException>(() =>
				VulkanMeshDrawCommands.Record(
					instance,
					commandBuffer,
					null!));

			Assert.Throws<ArgumentException>(() =>
				VulkanMeshDrawCommands.Record(
					instance,
					default,
					mesh));

			Assert.Throws<ArgumentOutOfRangeException>(() =>
				VulkanMeshDrawCommands.Record(
					instance,
					commandBuffer,
					mesh,
					instanceCount: 0));

			mesh.Dispose();

			Assert.Throws<ObjectDisposedException>(() =>
				VulkanMeshDrawCommands.Record(
					instance,
					commandBuffer,
					mesh));

			Assert.Throws<ObjectDisposedException>(() =>
				VulkanMeshDrawCommands.BindMeshBuffers(
					instance,
					commandBuffer,
					mesh));
		});
	}

	private static TestVertex[] CreateVertices()
	{
		return
		[
			new(0, 0, 0),
			new(1, 0, 0),
			new(0, 1, 0)
		];
	}

	private static void RecordBindings(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		VulkanMeshBuffers mesh)
	{
		using VulkanCommandPool pool = new(instance, device);

		using VulkanCommandBuffers commandBuffers = new(
			instance,
			device,
			pool,
			count: 1);

		CommandBuffer commandBuffer = commandBuffers.GetBuffer(0);

		VulkanCommandRecording.Begin(instance, commandBuffer);

		try
		{
			VulkanMeshDrawCommands.BindMeshBuffers(
				instance,
				commandBuffer,
				mesh);
		}

		finally
		{
			VulkanCommandRecording.End(instance, commandBuffer);
		}
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

	private static void RunWithDevice(
		Action<VulkanInstance, VulkanLogicalDevice, PhysicalDevice> test)
	{
		bool enableValidation =
			Environment.GetEnvironmentVariable(
				"VOLT_VULKAN_VALIDATION") == "1";

		VulkanInstanceDescription description = new(
			ApplicationName: "VOLT Vulkan Mesh Draw Tests",
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
