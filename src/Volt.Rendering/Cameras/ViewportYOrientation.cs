namespace Volt.Rendering.Cameras;

/// <summary>
/// Describes which way a viewport's Y axis points in clip space.
/// </summary>
/// <remarks>
/// <see cref="System.Numerics.Matrix4x4"/> perspective projections
/// place world-space +Y towards a positive clip-space Y, which is the
/// convention of OpenGL and most window systems. A Vulkan framebuffer
/// has its origin at the top left, so the same projection needs its Y
/// row negated to keep the world upright.
/// </remarks>
public enum ViewportYOrientation
{
	/// <summary>
	/// Clip-space Y increases upwards. This is the convention
	/// produced by <see cref="System.Numerics.Matrix4x4"/>.
	/// </summary>
	YUp,

	/// <summary>
	/// Clip-space Y increases downwards, as it does in a Vulkan
	/// framebuffer viewport.
	/// </summary>
	YDown
}
