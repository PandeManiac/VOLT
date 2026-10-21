using System.Numerics;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Places the aiming reticle in clip space.
/// </summary>
/// <remarks>
/// <para>
/// The reticle is not a world-space object. The aiming ray starts at
/// the camera and follows its forward vector, and that direction
/// always projects to the centre of the viewport, whatever the camera
/// pose, aspect ratio, or framebuffer size. Drawing the reticle around
/// the clip-space origin with no view or projection therefore puts it
/// exactly on the shot ray by construction, at every window size.
/// </para>
/// <para>
/// The reticle mesh is authored in clip-space units spanning -1..1.
/// The transform only scales those units so the drawn size stays a
/// fixed fraction of the framebuffer height in pixels; the clip-space
/// origin is untouched, so the reticle never drifts off centre.
/// </para>
/// <para>
/// The scale is a diagonal matrix, so it produces the same result
/// whether it is read as a row-vector or column-vector transform. The
/// Z axis is left alone, so the authored clip-space depth of zero
/// passes the sample's less-than depth test against everything the
/// scene draws.
/// </para>
/// </remarks>
internal static class TargetRangeReticle
{
	/// <summary>
	/// Half the reticle's pixel size as a fraction of framebuffer
	/// height.
	/// </summary>
	/// <remarks>
	/// At 1280x720 this is 15.8 pixels, so the plus spans about 32
	/// pixels; at 960x540 it is 11.9 pixels and the plus spans about
	/// 24. Scaling with height keeps the reticle's apparent size
	/// stable when the window shrinks.
	/// </remarks>
	public const float HalfSizeHeightFraction = 0.022f;

	/// <summary>
	/// Smallest half size in pixels, so a very small window still
	/// shows a reticle instead of a degenerate point.
	/// </summary>
	public const float MinimumHalfSizePixels = 4.0f;

	/// <summary>
	/// Resolves the reticle's half size for one framebuffer height.
	/// </summary>
	public static float ResolveHalfSizePixels(int framebufferHeight)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
			framebufferHeight);

		return MathF.Max(
			MinimumHalfSizePixels,
			HalfSizeHeightFraction * framebufferHeight);
	}

	/// <summary>
	/// Creates the clip-space transform for one framebuffer size.
	/// </summary>
	/// <param name="framebufferWidth">Framebuffer width in pixels.</param>
	/// <param name="framebufferHeight">
	/// Framebuffer height in pixels.
	/// </param>
	/// <remarks>
	/// Clip-space X spans two units across the framebuffer width and
	/// clip-space Y spans two units across its height, so each axis is
	/// scaled independently. A square reticle in pixels therefore needs
	/// different clip-space factors on a non-square framebuffer.
	/// </remarks>
	public static Matrix4x4 CreateOverlayTransform(
		int framebufferWidth,
		int framebufferHeight)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
			framebufferWidth);

		float halfSize = ResolveHalfSizePixels(framebufferHeight);

		return Matrix4x4.CreateScale(
			(2.0f * halfSize) / framebufferWidth,
			(2.0f * halfSize) / framebufferHeight,
			1.0f);
	}
}
