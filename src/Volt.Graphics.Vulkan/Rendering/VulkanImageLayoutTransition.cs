using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Records the pipeline barriers that move a colour or depth image
/// between the layouts VOLT's renderers use.
/// </summary>
/// <remarks>
/// <para>
/// The engine reuses a small set of layouts, so this helper maps each
/// transition to the stage and access masks that make it correct
/// instead of leaving that mapping to every call site:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="ImageLayout.Undefined"/> to a writable layout: everything
/// before it is unordered, so the source side is
/// <see cref="PipelineStageFlags.TopOfPipeBit"/> with no access mask.
/// </description></item>
/// <item><description>
/// <see cref="ImageLayout.ColorAttachmentOptimal"/> to
/// <see cref="ImageLayout.ShaderReadOnlyOptimal"/>: the colour write
/// must be visible to the sampling fragment stage.
/// </description></item>
/// <item><description>
/// <see cref="ImageLayout.ShaderReadOnlyOptimal"/> to
/// <see cref="ImageLayout.ColorAttachmentOptimal"/>: the sampled read
/// must finish before the attachment write.
/// </description></item>
/// </list>
/// <para>
/// Transitions the helper does not model throw instead of guessing,
/// because a wrong mask is a data race rather than a visible error.
/// </para>
/// </remarks>
public static class VulkanImageLayoutTransition
{
	/// <summary>
	/// Records a barrier that moves one image subresource into a new
	/// layout.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="commandBuffer">
	/// A command buffer in the recording state.
	/// </param>
	/// <param name="image">The image to transition.</param>
	/// <param name="oldLayout">The layout the image is currently in.</param>
	/// <param name="newLayout">The layout the image must reach.</param>
	/// <param name="aspectMask">
	/// The image aspect to transition: colour for colour images, depth
	/// for depth attachments.
	/// </param>
	/// <param name="mipLevels">The number of mip levels to cover.</param>
	/// <param name="arrayLayers">
	/// The number of array layers to cover.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="instance"/> is null.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The command buffer or image handle is not valid.
	/// </exception>
	/// <exception cref="NotSupportedException">
	/// The transition is not one this helper models.
	/// </exception>
	public static unsafe void Record(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		Image image,
		ImageLayout oldLayout,
		ImageLayout newLayout,
		ImageAspectFlags aspectMask,
		uint mipLevels = 1,
		uint arrayLayers = 1)
	{
		ArgumentNullException.ThrowIfNull(instance);

		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid command buffer is required.",
				nameof(commandBuffer));
		}

		if (image.Handle == 0)
		{
			throw new ArgumentException(
				"A valid image is required.",
				nameof(image));
		}

		if (oldLayout == newLayout)
		{
			return;
		}

		(PipelineStageFlags sourceStage,
			AccessFlags sourceAccess,
			PipelineStageFlags destinationStage,
			AccessFlags destinationAccess) = Resolve(oldLayout, newLayout);

		ImageMemoryBarrier barrier = new()
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = oldLayout,
			NewLayout = newLayout,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = image,
			SubresourceRange = new ImageSubresourceRange(
				aspectMask,
				0,
				mipLevels,
				0,
				arrayLayers),
			SrcAccessMask = sourceAccess,
			DstAccessMask = destinationAccess
		};

		instance.Api.CmdPipelineBarrier(
			commandBuffer,
			sourceStage,
			destinationStage,
			0,
			0,
			null,
			0,
			null,
			1,
			&barrier);
	}

	/// <summary>
	/// Selects the stage and access masks for one transition.
	/// </summary>
	/// <param name="oldLayout">The current layout.</param>
	/// <param name="newLayout">The requested layout.</param>
	/// <returns>
	/// The source stage, source access, destination stage, and
	/// destination access masks.
	/// </returns>
	/// <exception cref="NotSupportedException">
	/// The transition is not modelled.
	/// </exception>
	/// <remarks>
	/// This helper performs no Vulkan access, so the mapping can be
	/// checked without a device.
	/// </remarks>
	public static (
		PipelineStageFlags SourceStage,
		AccessFlags SourceAccess,
		PipelineStageFlags DestinationStage,
		AccessFlags DestinationAccess) Resolve(
			ImageLayout oldLayout,
			ImageLayout newLayout)
	{
		if (oldLayout == ImageLayout.Undefined &&
			newLayout == ImageLayout.ColorAttachmentOptimal)
		{
			return (
				PipelineStageFlags.TopOfPipeBit,
				AccessFlags.None,
				PipelineStageFlags.ColorAttachmentOutputBit,
				AccessFlags.ColorAttachmentWriteBit);
		}

		if (oldLayout == ImageLayout.Undefined &&
			newLayout == ImageLayout.DepthStencilAttachmentOptimal)
		{
			return (
				PipelineStageFlags.TopOfPipeBit,
				AccessFlags.None,
				PipelineStageFlags.EarlyFragmentTestsBit |
					PipelineStageFlags.LateFragmentTestsBit,
				AccessFlags.DepthStencilAttachmentWriteBit);
		}

		if (oldLayout == ImageLayout.ColorAttachmentOptimal &&
			newLayout == ImageLayout.ShaderReadOnlyOptimal)
		{
			return (
				PipelineStageFlags.ColorAttachmentOutputBit,
				AccessFlags.ColorAttachmentWriteBit,
				PipelineStageFlags.FragmentShaderBit,
				AccessFlags.ShaderReadBit);
		}

		if (oldLayout == ImageLayout.ShaderReadOnlyOptimal &&
			newLayout == ImageLayout.ColorAttachmentOptimal)
		{
			return (
				PipelineStageFlags.FragmentShaderBit,
				AccessFlags.ShaderReadBit,
				PipelineStageFlags.ColorAttachmentOutputBit,
				AccessFlags.ColorAttachmentWriteBit);
		}

		throw new NotSupportedException(
			$"The image layout transition from {oldLayout} to " +
			$"{newLayout} is not supported.");
	}
}
