using System.Numerics;
using Volt.Ecs.Components.Rendering;

namespace Volt.Rendering.Cameras;

/// <summary>
/// Creates viewport-ready perspective projections from a
/// <see cref="CameraComponent"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CameraComponent"/> is the authoritative representation
/// of a perspective camera's field of view and clipping planes, and
/// its <see cref="CameraComponent.CreateProjectionMatrix"/> already
/// produces a right-handed projection whose near plane maps to depth
/// 0 and far plane to depth 1. That matrix is reused unchanged for
/// <see cref="ViewportYOrientation.YUp"/>; this type only adds the
/// explicit viewport orientation choice.
/// </para>
/// <para>
/// Field of view is expressed in radians. A camera transform
/// includes position and rotation only; scale is not part of a
/// view or projection matrix.
/// </para>
/// <para>
/// Every method is allocation-free and returns a value type.
/// </para>
/// </remarks>
public static class CameraProjection
{
	/// <summary>
	/// Creates the perspective projection for a viewport.
	/// </summary>
	/// <param name="camera">The camera's projection settings.</param>
	/// <param name="aspectRatio">
	/// The viewport width divided by its height.
	/// </param>
	/// <param name="viewportY">
	/// Which way the viewport's Y axis points. Use
	/// <see cref="ViewportYOrientation.YDown"/> for a Vulkan
	/// framebuffer.
	/// </param>
	/// <returns>The view-to-clip matrix for the viewport.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The camera has invalid projection settings, or
	/// <paramref name="aspectRatio"/> is not finite and positive.
	/// </exception>
	public static Matrix4x4 CreatePerspective(
		in CameraComponent camera,
		float aspectRatio,
		ViewportYOrientation viewportY =
			ViewportYOrientation.YUp)
	{
		Matrix4x4 projection =
			camera.CreateProjectionMatrix(aspectRatio);

		return viewportY switch
		{
			ViewportYOrientation.YUp => projection,
			ViewportYOrientation.YDown => FlipY(projection),
			_ => throw new ArgumentOutOfRangeException(
				nameof(viewportY),
				viewportY,
				"Unknown viewport Y orientation.")
		};
	}

	/// <summary>
	/// Negates the projected Y coordinate so world-space +Y stays at
	/// the top of a downward viewport.
	/// </summary>
	/// <remarks>
	/// Only the Y row of the projection is negated. Depth and W are
	/// untouched, so the 0..1 Vulkan depth range is preserved.
	/// </remarks>
	private static Matrix4x4 FlipY(Matrix4x4 projection)
	{
		projection.M22 = -projection.M22;
		return projection;
	}
}
