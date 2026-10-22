using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Swapchain;

/// <summary>
/// Records a transfer-based clear of a swapchain color image
/// and transitions that image into the presentation layout.
/// </summary>
/// <remarks>
/// The image must have been acquired from the swapchain before
/// the recorded work is submitted. Its swapchain must have been
/// created with ImageUsageFlags.TransferDstBit.
///
/// The caller is responsible for beginning and ending the primary
/// command buffer, waiting for the image-available semaphore at
/// a suitable pipeline stage, and signaling a semaphore for
/// presentation. This class does not submit any GPU work.
///
/// Each clear discards the image's previous contents by starting
/// from ImageLayout.Undefined. This is intentional and is only
/// suitable for a full-image clear.
///
/// The graphics and presentation queues must use a compatible
/// queue-family ownership arrangement. VOLT currently uses the
/// same queue family for both operations.
/// </remarks>
public static unsafe class VulkanSwapchainImageCommands
{
	/// <summary>
	/// Records a full-image clear and transition to PresentSrcKhr.
	/// </summary>
	/// <remarks>
	/// This method must be called only while the supplied primary
	/// command buffer is in the recording state, outside a render
	/// pass or dynamic rendering instance.
	/// </remarks>
	public static void RecordClear(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		Image image,
		ImageUsageFlags imageUsage,
		ClearColorValue clearColor)
	{
		ArgumentNullException.ThrowIfNull(instance);

		ValidateArguments(
			commandBuffer,
			image,
			imageUsage);

		Vk api = instance.Api;

		ImageMemoryBarrier toTransfer =
			CreateTransferDestinationBarrier(image);

		api.CmdPipelineBarrier(
			commandBuffer,
			PipelineStageFlags.TopOfPipeBit,
			PipelineStageFlags.TransferBit,
			DependencyFlags.None,
			0,
			null,
			0,
			null,
			1,
			in toTransfer);

		ImageSubresourceRange colorRange =
			CreateColorRange();

		api.CmdClearColorImage(
			commandBuffer,
			image,
			ImageLayout.TransferDstOptimal,
			in clearColor,
			1,
			in colorRange);

		ImageMemoryBarrier toPresent =
			CreatePresentBarrier(image);

		api.CmdPipelineBarrier(
			commandBuffer,
			PipelineStageFlags.TransferBit,
			PipelineStageFlags.BottomOfPipeBit,
			DependencyFlags.None,
			0,
			null,
			0,
			null,
			1,
			in toPresent);
	}

	/// <summary>
	/// Creates a floating-point RGBA clear color.
	/// </summary>
	public static ClearColorValue CreateClearColor(
		float red,
		float green,
		float blue,
		float alpha = 1.0f)
	{
		if (!float.IsFinite(red) ||
			!float.IsFinite(green) ||
			!float.IsFinite(blue) ||
			!float.IsFinite(alpha))
		{
			throw new ArgumentOutOfRangeException(
				nameof(red),
				"Clear color components must all be finite.");
		}

		return new ClearColorValue
		{
			Float32_0 = red,
			Float32_1 = green,
			Float32_2 = blue,
			Float32_3 = alpha
		};
	}

	/// <summary>
	/// Creates the barrier that makes a newly acquired image
	/// writable by transfer commands, discarding old contents.
	/// </summary>
	internal static ImageMemoryBarrier CreateTransferDestinationBarrier(
		Image image)
	{
		ValidateImage(image);

		return new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			SrcAccessMask = AccessFlags.None,
			DstAccessMask = AccessFlags.TransferWriteBit,
			OldLayout = ImageLayout.Undefined,
			NewLayout = ImageLayout.TransferDstOptimal,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = image,
			SubresourceRange = CreateColorRange()
		};
	}

	/// <summary>
	/// Creates the barrier that makes the transfer clear
	/// available before the image is presented.
	/// </summary>
	internal static ImageMemoryBarrier CreatePresentBarrier(
		Image image)
	{
		ValidateImage(image);

		return new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			SrcAccessMask = AccessFlags.TransferWriteBit,
			DstAccessMask = AccessFlags.None,
			OldLayout = ImageLayout.TransferDstOptimal,
			NewLayout = ImageLayout.PresentSrcKhr,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = image,
			SubresourceRange = CreateColorRange()
		};
	}

	/// <summary>
	/// Selects the one color mip level and array layer
	/// owned by a standard VOLT swapchain image.
	/// </summary>
	internal static ImageSubresourceRange CreateColorRange()
	{
		return new ImageSubresourceRange
		{
			AspectMask = ImageAspectFlags.ColorBit,
			BaseMipLevel = 0,
			LevelCount = 1,
			BaseArrayLayer = 0,
			LayerCount = 1
		};
	}

	/// <summary>
	/// Validates the handles and image usage required by
	/// transfer-based clearing, without accessing Vulkan.
	/// </summary>
	internal static void ValidateArguments(
		CommandBuffer commandBuffer,
		Image image,
		ImageUsageFlags imageUsage)
	{
		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan command buffer is required.",
				nameof(commandBuffer));
		}

		ValidateImage(image);

		if ((imageUsage & ImageUsageFlags.TransferDstBit) == 0)
		{
			throw new NotSupportedException(
				"The swapchain image was not created with " +
				"ImageUsageFlags.TransferDstBit. A transfer " +
				"clear would violate Vulkan image usage rules.");
		}
	}

	private static void ValidateImage(Image image)
	{
		if (image.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan image handle is required.",
				nameof(image));
		}
	}
}
