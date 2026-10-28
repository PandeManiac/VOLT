using System.Numerics;
using Volt.Ui.Rendering;

namespace Volt.Ui.Vulkan;

/// <summary>
/// Converts pixel-space UI vertices into Vulkan clip space.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UiGeometry"/> emits physical pixels whose origin is the
/// top-left corner of the surface with Y increasing downward. That is
/// also the coordinate system of <see cref="UiBatch.Scissor"/>, of
/// every clip rectangle in the draw list, and of UI hit testing.
/// Vulkan clip space shares that orientation: (-1, -1) is the
/// top-left corner of the viewport and (1, 1) its bottom-right. The
/// mapping is therefore a scale and a translation with no axis flip,
/// so the geometry and the scissor that clips it always agree. Flipping
/// Y here would mirror the whole frame while clipping stayed in
/// framebuffer coordinates.
/// </para>
/// <para>
/// Texture coordinates pass through unchanged: font atlases are packed
/// and uploaded with row zero at the top, and glyph UVs measure from
/// the top-left of that coverage, which matches the same origin.
/// </para>
/// </remarks>
public static class UiClipSpace
{
	/// <summary>
	/// Converts pixel-space vertices into clip space for a viewport of
	/// the given physical pixel extent.
	/// </summary>
	/// <param name="source">Pixel-space vertices to convert.</param>
	/// <param name="destination">
	/// Receives the converted vertices in the same order. It must hold
	/// at least as many vertices as <paramref name="source"/>.
	/// </param>
	/// <param name="extentWidth">Viewport width in physical pixels.</param>
	/// <param name="extentHeight">Viewport height in physical pixels.</param>
	public static void Convert(
		ReadOnlySpan<UiVertex> source,
		Span<UiVertex> destination,
		float extentWidth,
		float extentHeight)
	{
		if (destination.Length < source.Length)
		{
			throw new ArgumentException(
				"The destination must hold every source vertex.",
				nameof(destination));
		}

		// A zero-sized extent cannot describe a viewport; clamping it
		// keeps the mapping finite instead of producing NaN.
		float width = MathF.Max(1f, extentWidth);
		float height = MathF.Max(1f, extentHeight);

		for (int index = 0; index < source.Length; index++)
		{
			UiVertex vertex = source[index];

			destination[index] = new UiVertex(
				new Vector2(
					(vertex.Position.X / width * 2f) - 1f,
					(vertex.Position.Y / height * 2f) - 1f),
				vertex.Uv,
				vertex.Color);
		}
	}
}
