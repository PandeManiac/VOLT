using Silk.NET.Vulkan;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Describes a Vulkan colour image that another renderer samples as a
/// texture.
/// </summary>
/// <remarks>
/// <para>
/// The interface exists so a UI or compositing renderer can bind and
/// transition an image it does not own. Ownership, extent management,
/// and recreation stay with the implementer; this contract only
/// exposes what sampling needs.
/// </para>
/// <para>
/// <see cref="View"/> must be a view the shader's sampler declaration
/// accepts. VOLT's UI shader declares a <c>sampler2DArray</c>, so an
/// implementation sampled by the UI must expose a 2D-array view.
/// </para>
/// <para>
/// Implementations are not thread-safe and must be called from the
/// thread that records the frame.
/// </para>
/// </remarks>
public interface IVulkanSampledImage
{
	/// <summary>
	/// Gets the owned image handle.
	/// </summary>
	Image Image { get; }

	/// <summary>
	/// Gets the owned image view handle.
	/// </summary>
	ImageView View { get; }

	/// <summary>
	/// Gets the image format.
	/// </summary>
	Format Format { get; }

	/// <summary>
	/// Gets the image extent in pixels.
	/// </summary>
	Extent2D Extent { get; }

	/// <summary>
	/// Records whatever layout transition is required before the image
	/// is sampled, and reports whether one was recorded.
	/// </summary>
	/// <param name="commandBuffer">
	/// A command buffer in the recording state.
	/// </param>
	/// <returns>
	/// True when a pipeline barrier was recorded, false when the image
	/// was already sampleable.
	/// </returns>
	bool EnsureSampled(CommandBuffer commandBuffer);
}
