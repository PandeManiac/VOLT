
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Volt.Graphics.Vulkan.Memory;

/// <summary>
/// Describes a destination buffer for native batch allocation.
/// </summary>
internal readonly record struct VulkanBufferAllocationRequest(
	ulong Size,
	BufferUsageFlags Usage);

/// <summary>
/// Creates independently owned buffers backed by shared device
/// memory when their native requirements permit it.
/// </summary>
internal static unsafe class VulkanBatchDestinationAllocator
{
	internal static VulkanBuffer[] Create(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<VulkanBufferAllocationRequest> requests)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (requests.IsEmpty)
		{
			throw new ArgumentException(
				"At least one destination is required.",
				nameof(requests));
		}

		if (requests.Length == 1)
		{
			return CreateDedicated(
				instance,
				device,
				physicalDevice,
				requests);
		}

		for (int index = 0; index < requests.Length; index++)
		{
			if (requests[index].Size == 0 ||
				requests[index].Usage == BufferUsageFlags.None)
			{
				throw new ArgumentException(
					"Every destination requires a positive size " +
					"and nonempty usage.",
					nameof(requests));
			}
		}

		ReadOnlySpan<MemoryPropertyFlags> memoryTypes =
			device.GetMemoryTypeFlags(physicalDevice);

		bool hasShareableMemoryType = false;

		foreach (MemoryPropertyFlags properties in memoryTypes)
		{
			bool deviceLocal =
				(properties & MemoryPropertyFlags.DeviceLocalBit) != 0;

			bool hostVisible =
				(properties & MemoryPropertyFlags.HostVisibleBit) != 0;

			if (deviceLocal && !hostVisible)
			{
				hasShareableMemoryType = true;
				break;
			}
		}

		if (!hasShareableMemoryType)
		{
			return CreateDedicated(
				instance,
				device,
				physicalDevice,
				requests);
		}

		Vk api = instance.Api;
		Device deviceHandle = device.Handle;

		VkBuffer[] handles = new VkBuffer[requests.Length];

		MemoryRequirements[] requirements =
			new MemoryRequirements[requests.Length];

		DeviceMemory memory = default;
		bool ownershipTransferred = false;

		try
		{
			uint compatibleMemoryTypes = uint.MaxValue;
			bool requiresDedicated = false;

			for (int index = 0; index < requests.Length; index++)
			{
				VulkanBufferAllocationRequest request =
					requests[index];

				BufferCreateInfo description = new()
				{
					SType = StructureType.BufferCreateInfo,
					Size = request.Size,
					Usage = request.Usage,
					SharingMode = SharingMode.Exclusive
				};

				Result createResult = api.CreateBuffer(
					deviceHandle,
					in description,
					null,
					out handles[index]);

				EnsureSuccess(
					createResult,
					"Create batched Vulkan destination buffer");

				if (handles[index].Handle == 0)
				{
					throw new InvalidOperationException(
						"Vulkan returned a null buffer handle.");
				}

				BufferMemoryRequirementsInfo2 info = new()
				{
					SType =
						StructureType.BufferMemoryRequirementsInfo2,
					Buffer = handles[index]
				};

				MemoryDedicatedRequirements dedicated = new()
				{
					SType = StructureType.MemoryDedicatedRequirements
				};

				MemoryRequirements2 result = new()
				{
					SType = StructureType.MemoryRequirements2,
					PNext = &dedicated
				};

				api.GetBufferMemoryRequirements2(
					deviceHandle,
					&info,
					&result);

				requirements[index] = result.MemoryRequirements;

				compatibleMemoryTypes
					&= result.MemoryRequirements.MemoryTypeBits;

				requiresDedicated
					|= dedicated.RequiresDedicatedAllocation;
			}

			int selectedMemoryType = -1;

			if (!requiresDedicated)
			{
				for (int index = 0;
					 index < memoryTypes.Length;
					 index++)
				{
					uint bit = 1u << index;

					if ((compatibleMemoryTypes & bit) == 0)
					{
						continue;
					}

					MemoryPropertyFlags properties =
						memoryTypes[index];

					if ((properties
							& MemoryPropertyFlags.DeviceLocalBit) == 0)
					{
						continue;
					}

					if ((properties
							& MemoryPropertyFlags.HostVisibleBit) != 0)
					{
						continue;
					}

					selectedMemoryType = index;
					break;
				}
			}

			if (selectedMemoryType < 0)
			{
				return CreateDedicated(
					instance,
					device,
					physicalDevice,
					requests);
			}

			VulkanBufferSuballocationLayout layout =
				VulkanBufferSuballocationLayout.Create(
					requirements,
					(uint)selectedMemoryType);

			MemoryAllocateInfo allocation = new()
			{
				SType = StructureType.MemoryAllocateInfo,
				AllocationSize = layout.AllocationSize,
				MemoryTypeIndex = (uint)selectedMemoryType
			};

			Result allocationResult = api.AllocateMemory(
				deviceHandle,
				in allocation,
				null,
				out memory);

			EnsureSuccess(
				allocationResult,
				"Allocate shared Vulkan destination memory");

			if (memory.Handle == 0)
			{
				throw new InvalidOperationException(
					"Vulkan returned null shared device memory.");
			}

			for (int index = 0; index < handles.Length; index++)
			{
				Result bindResult = api.BindBufferMemory(
					deviceHandle,
					handles[index],
					memory,
					layout.GetOffset(index));

				EnsureSuccess(
					bindResult,
					"Bind shared Vulkan destination buffer");
			}

			VulkanSharedBufferMemory sharedMemory = new(
				api,
				deviceHandle,
				memory,
				handles.Length);

			VulkanBuffer[] destinations =
				new VulkanBuffer[handles.Length];

			for (int index = 0; index < handles.Length; index++)
			{
				destinations[index] = new VulkanBuffer(
					instance,
					device,
					handles[index],
					memory,
					sharedMemory,
					layout.GetOffset(index),
					requests[index].Size,
					requests[index].Usage,
					memoryTypes[selectedMemoryType]);
			}

			ownershipTransferred = true;
			return destinations;
		}

		finally
		{
			if (!ownershipTransferred)
			{
				foreach (VkBuffer handle in handles)
				{
					if (handle.Handle != 0)
					{
						api.DestroyBuffer(
							deviceHandle,
							handle,
							null);
					}
				}

				if (memory.Handle != 0)
				{
					api.FreeMemory(
						deviceHandle,
						memory,
						null);
				}
			}
		}
	}

	private static VulkanBuffer[] CreateDedicated(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<VulkanBufferAllocationRequest> requests)
	{
		VulkanBuffer[] destinations =
			new VulkanBuffer[requests.Length];

		int created = 0;

		try
		{
			for (int index = 0; index < requests.Length; index++)
			{
				destinations[index] = new VulkanBuffer(
					instance,
					device,
					physicalDevice,
					requests[index].Size,
					requests[index].Usage,
					MemoryPropertyFlags.DeviceLocalBit);

				created++;
			}

			return destinations;
		}

		catch
		{
			for (int index = 0; index < created; index++)
			{
				destinations[index].Dispose();
			}

			throw;
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
}
