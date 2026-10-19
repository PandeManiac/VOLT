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
	public void CreateRangeDrawInfo_NonIndexedPartialRangeSelectsDraw()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: false,
			vertexCount: 6,
			indexCount: 0,
			firstElement: 2,
			elementCount: 3);

		Assert.False(info.IsIndexed);
		Assert.Equal(3u, info.VertexCount);
		Assert.Equal(0u, info.IndexCount);
		Assert.Equal(2u, info.FirstElement);
		Assert.Equal(VkIndexType.Uint16, info.IndexType);
		Assert.Equal(1u, info.InstanceCount);
		Assert.Equal(0u, info.FirstInstance);
		Assert.Equal(0, info.VertexOffset);
	}

	[Fact]
	public void CreateRangeDrawInfo_UInt16IndexedPartialRangeSelectsDrawIndexed()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 4,
			indexCount: 6,
			firstElement: 3,
			elementCount: 3,
			indexType: VkIndexType.Uint16);

		Assert.True(info.IsIndexed);
		Assert.Equal(4u, info.VertexCount);
		Assert.Equal(3u, info.IndexCount);
		Assert.Equal(3u, info.FirstElement);
		Assert.Equal(VkIndexType.Uint16, info.IndexType);
	}

	[Fact]
	public void CreateRangeDrawInfo_UInt32IndexedPartialRangePreservesIndexType()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 7,
			indexCount: 18,
			firstElement: 9,
			elementCount: 9,
			indexType: VkIndexType.Uint32);

		Assert.True(info.IsIndexed);
		Assert.Equal(9u, info.IndexCount);
		Assert.Equal(9u, info.FirstElement);
		Assert.Equal(VkIndexType.Uint32, info.IndexType);
	}

	[Fact]
	public void CreateRangeDrawInfo_CompleteRangeMatchesCompleteMeshSelection()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo complete =
			VulkanMeshDrawCommands.CreateDrawInfo(
				isIndexed: true,
				vertexCount: 4,
				indexCount: 6,
				VkIndexType.Uint32,
				instanceCount: 3);

		VulkanMeshDrawCommands.VulkanMeshDrawInfo range = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 4,
			indexCount: 6,
			firstElement: 0,
			elementCount: 6,
			instanceCount: 3,
			indexType: VkIndexType.Uint32);

		Assert.Equal(complete, range);
	}

	[Fact]
	public void CreateRangeDrawInfo_ForwardsFirstElement()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: false,
			vertexCount: 65_536,
			indexCount: 0,
			firstElement: 4_096,
			elementCount: 1);

		Assert.Equal(4_096u, info.FirstElement);
		Assert.Equal(1u, info.VertexCount);
	}

	[Fact]
	public void CreateRangeDrawInfo_ForwardsInstanceCountAndFirstInstance()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 4,
			indexCount: 6,
			firstElement: 0,
			elementCount: 6,
			instanceCount: 5,
			firstInstance: 7);

		Assert.Equal(5u, info.InstanceCount);
		Assert.Equal(7u, info.FirstInstance);
	}

	[Theory]
	[InlineData(3)]
	[InlineData(-2)]
	public void CreateRangeDrawInfo_ForwardsIndexedVertexOffset(int vertexOffset)
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 4,
			indexCount: 6,
			firstElement: 3,
			elementCount: 3,
			vertexOffset: vertexOffset);

		Assert.Equal(vertexOffset, info.VertexOffset);
		Assert.Equal(3u, info.FirstElement);
		Assert.Equal(3u, info.IndexCount);
	}

	[Theory]
	[InlineData(int.MaxValue)]
	[InlineData(int.MinValue)]
	public void CreateRangeDrawInfo_AcceptsVertexOffsetBeyondTheMesh(
		int vertexOffset)
	{
		// Whether an offset resolves to valid vertices cannot be
		// checked from the index count alone, so the selection
		// accepts it and leaves that validation to the caller.
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: true,
			vertexCount: 4,
			indexCount: 6,
			firstElement: 3,
			elementCount: 3,
			vertexOffset: vertexOffset);

		Assert.Equal(vertexOffset, info.VertexOffset);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CreateRangeDrawInfo_AcceptsRangeEndingAtLastElement(
		bool isIndexed)
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: isIndexed,
			vertexCount: 6,
			indexCount: isIndexed ? 6u : 0u,
			firstElement: 5,
			elementCount: 1);

		Assert.Equal(5u, info.FirstElement);
		Assert.Equal(
			1u,
			isIndexed ? info.IndexCount : info.VertexCount);
	}

	[Fact]
	public void CreateRangeDrawInfo_AcceptsRangeNearMaximumCounts()
	{
		VulkanMeshDrawCommands.VulkanMeshDrawInfo info = CreateRangeInfo(
			isIndexed: false,
			vertexCount: uint.MaxValue,
			indexCount: 0,
			firstElement: uint.MaxValue - 2,
			elementCount: 2);

		Assert.Equal(uint.MaxValue - 2, info.FirstElement);
		Assert.Equal(2u, info.VertexCount);
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsRangeOverflowNearMaximumCounts()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: false,
				vertexCount: uint.MaxValue,
				indexCount: 0,
				firstElement: uint.MaxValue - 2,
				elementCount: 4));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsFirstElementBeyondTheMesh()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CreateRangeInfo(
					isIndexed: false,
					vertexCount: 6,
					indexCount: 0,
					firstElement: 7,
					elementCount: 1));

		Assert.Equal("firstElement", exception.ParamName);
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsRangeStartingAtEndOfBuffer()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CreateRangeInfo(
					isIndexed: true,
					vertexCount: 4,
					indexCount: 6,
					firstElement: 6,
					elementCount: 1));

		Assert.Equal("elementCount", exception.ParamName);
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsElementCountBeyondRemaining()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CreateRangeInfo(
					isIndexed: true,
					vertexCount: 4,
					indexCount: 6,
					firstElement: 4,
					elementCount: 3));

		Assert.Equal("elementCount", exception.ParamName);
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsZeroElementCount()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: false,
				vertexCount: 6,
				indexCount: 0,
				firstElement: 0,
				elementCount: 0));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsZeroElementCountAtEndOfBuffer()
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CreateRangeInfo(
					isIndexed: false,
					vertexCount: 6,
					indexCount: 0,
					firstElement: 6,
					elementCount: 0));

		Assert.Equal("elementCount", exception.ParamName);
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsZeroInstanceCount()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: false,
				vertexCount: 6,
				indexCount: 0,
				firstElement: 0,
				elementCount: 3,
				instanceCount: 0));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsZeroVertices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: false,
				vertexCount: 0,
				indexCount: 0,
				firstElement: 0,
				elementCount: 0));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsIndexedMeshWithoutIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: true,
				vertexCount: 3,
				indexCount: 0,
				firstElement: 0,
				elementCount: 0));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsNonIndexedMeshWithIndices()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: false,
				vertexCount: 3,
				indexCount: 3,
				firstElement: 0,
				elementCount: 1));
	}

	[Fact]
	public void CreateRangeDrawInfo_RejectsUnsupportedIndexType()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			CreateRangeInfo(
				isIndexed: true,
				vertexCount: 3,
				indexCount: 3,
				firstElement: 0,
				elementCount: 1,
				indexType: (VkIndexType)int.MaxValue));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(-1)]
	public void CreateRangeDrawInfo_RejectsVertexOffsetForNonIndexedMesh(
		int vertexOffset)
	{
		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(() =>
				CreateRangeInfo(
					isIndexed: false,
					vertexCount: 6,
					indexCount: 0,
					firstElement: 0,
					elementCount: 3,
					vertexOffset: vertexOffset));

		Assert.Equal("vertexOffset", exception.ParamName);
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

	[Fact]
	public void RecordRange_RejectsNullInstance()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanMeshDrawCommands.RecordRange(
				null!,
				new CommandBuffer(1),
				null!,
				firstElement: 0,
				elementCount: 1));
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

	[VulkanIntegrationFact]
	public void RecordRange_RejectsInvalidArgumentsBeforeRecording()
	{
		RunWithDevice((instance, device, physicalDevice) =>
		{
			using VulkanMeshBuffers nonIndexed =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices());

			ushort[] indices = [0, 1, 2];

			using VulkanMeshBuffers indexed =
				VulkanMeshBuffers.Create(
					instance,
					device,
					physicalDevice,
					CreateVertices(),
					indices);

			using VulkanCommandPool pool = new(instance, device);

			using VulkanCommandBuffers commandBuffers = new(
				instance,
				device,
				pool,
				count: 1);

			CommandBuffer commandBuffer =
				commandBuffers.GetBuffer(0);

			VulkanCommandRecording.Begin(instance, commandBuffer);

			try
			{
				Assert.Throws<ArgumentNullException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						null!,
						commandBuffer,
						nonIndexed,
						firstElement: 0,
						elementCount: 1));

				Assert.Throws<ArgumentNullException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						null!,
						firstElement: 0,
						elementCount: 1));

				Assert.Throws<ArgumentException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						default,
						nonIndexed,
						firstElement: 0,
						elementCount: 1));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 0,
						elementCount: 0));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 0,
						elementCount: 1,
						instanceCount: 0));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 3,
						elementCount: 1));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 4,
						elementCount: 1));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 1,
						elementCount: 3));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						nonIndexed,
						firstElement: 0,
						elementCount: 1,
						vertexOffset: 1));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						indexed,
						firstElement: 4,
						elementCount: 1));

				Assert.Throws<ArgumentOutOfRangeException>(() =>
					VulkanMeshDrawCommands.RecordRange(
						instance,
						commandBuffer,
						indexed,
						firstElement: 2,
						elementCount: 2));

				// A rejected range records no commands, so the
				// command buffer is still recordable afterwards.
				VulkanMeshDrawCommands.BindMeshBuffers(
					instance,
					commandBuffer,
					nonIndexed);
			}

			finally
			{
				VulkanCommandRecording.End(
					instance,
					commandBuffer);
			}

			nonIndexed.Dispose();

			Assert.Throws<ObjectDisposedException>(() =>
				VulkanMeshDrawCommands.RecordRange(
					instance,
					commandBuffer,
					nonIndexed,
					firstElement: 0,
					elementCount: 1));
		});
	}

	private static VulkanMeshDrawCommands.VulkanMeshDrawInfo CreateRangeInfo(
		bool isIndexed,
		uint vertexCount,
		uint indexCount,
		uint firstElement,
		uint elementCount,
		uint instanceCount = 1,
		uint firstInstance = 0,
		int vertexOffset = 0,
		VkIndexType indexType = VkIndexType.Uint16)
	{
		return VulkanMeshDrawCommands.CreateRangeDrawInfo(
			isIndexed,
			vertexCount,
			indexCount,
			indexType,
			firstElement,
			elementCount,
			instanceCount,
			firstInstance,
			vertexOffset);
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
