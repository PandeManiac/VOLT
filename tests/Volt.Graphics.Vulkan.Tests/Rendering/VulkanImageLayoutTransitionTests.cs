using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.Graphics.Vulkan.Tests.Rendering;

/// <summary>
/// Pins the stage and access masks for the colour transitions the
/// renderers record.
/// </summary>
/// <remarks>
/// A first-use sample is legal in Vulkan: an image that was created and
/// never written is still in <see cref="ImageLayout.Undefined"/>, and a
/// transition away from Undefined discards its contents. The editor
/// samples a viewport target on the frame it is first painted, so the
/// helper has to model that case instead of refusing it.
/// </remarks>
public sealed class VulkanImageLayoutTransitionTests
{
	[Fact]
	public void UndefinedToSampled_SourcesFromTopOfPipe()
	{
		var resolved = VulkanImageLayoutTransition.Resolve(
			ImageLayout.Undefined,
			ImageLayout.ShaderReadOnlyOptimal);

		Assert.Equal(
			PipelineStageFlags.TopOfPipeBit,
			resolved.SourceStage);

		Assert.Equal(AccessFlags.None, resolved.SourceAccess);

		Assert.Equal(
			PipelineStageFlags.FragmentShaderBit,
			resolved.DestinationStage);

		Assert.Equal(
			AccessFlags.ShaderReadBit,
			resolved.DestinationAccess);
	}

	[Fact]
	public void UndefinedToColorAttachment_SourcesFromTopOfPipe()
	{
		var resolved = VulkanImageLayoutTransition.Resolve(
			ImageLayout.Undefined,
			ImageLayout.ColorAttachmentOptimal);

		Assert.Equal(
			PipelineStageFlags.TopOfPipeBit,
			resolved.SourceStage);

		Assert.Equal(AccessFlags.None, resolved.SourceAccess);

		Assert.Equal(
			PipelineStageFlags.ColorAttachmentOutputBit,
			resolved.DestinationStage);

		Assert.Equal(
			AccessFlags.ColorAttachmentWriteBit,
			resolved.DestinationAccess);
	}

	[Fact]
	public void ColorAttachmentToSampled_WaitsForTheColorWrite()
	{
		var resolved = VulkanImageLayoutTransition.Resolve(
			ImageLayout.ColorAttachmentOptimal,
			ImageLayout.ShaderReadOnlyOptimal);

		Assert.Equal(
			PipelineStageFlags.ColorAttachmentOutputBit,
			resolved.SourceStage);

		Assert.Equal(
			AccessFlags.ColorAttachmentWriteBit,
			resolved.SourceAccess);

		Assert.Equal(
			PipelineStageFlags.FragmentShaderBit,
			resolved.DestinationStage);

		Assert.Equal(
			AccessFlags.ShaderReadBit,
			resolved.DestinationAccess);
	}

	[Fact]
	public void SampledToColorAttachment_WaitsForTheSampledRead()
	{
		var resolved = VulkanImageLayoutTransition.Resolve(
			ImageLayout.ShaderReadOnlyOptimal,
			ImageLayout.ColorAttachmentOptimal);

		Assert.Equal(
			PipelineStageFlags.FragmentShaderBit,
			resolved.SourceStage);

		Assert.Equal(AccessFlags.ShaderReadBit, resolved.SourceAccess);

		Assert.Equal(
			PipelineStageFlags.ColorAttachmentOutputBit,
			resolved.DestinationStage);

		Assert.Equal(
			AccessFlags.ColorAttachmentWriteBit,
			resolved.DestinationAccess);
	}

	[Fact]
	public void UnmodelledTransition_IsRejectedInsteadOfGuessed()
	{
		Assert.Throws<NotSupportedException>(() =>
			VulkanImageLayoutTransition.Resolve(
				ImageLayout.ShaderReadOnlyOptimal,
				ImageLayout.TransferSrcOptimal));
	}
}
