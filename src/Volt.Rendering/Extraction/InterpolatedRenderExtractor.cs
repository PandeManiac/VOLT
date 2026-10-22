using System.Numerics;
using Volt.Ecs;
using Volt.Rendering.Transforms;

namespace Volt.Rendering.Extraction;

/// <summary>
/// Extracts render instances whose model matrices come from
/// interpolated transform history instead of the current ECS
/// components.
/// </summary>
/// <remarks>
/// <para>
/// Extraction, renderable filtering, resource handles, and output
/// order are exactly those of <see cref="EcsRenderExtractor"/>; only
/// the model matrix of an entity with history is replaced. The
/// un-interpolated matrix is computed as part of extraction and
/// discarded, which keeps this type a thin presentation layer rather
/// than a second ECS read path.
/// </para>
/// <para>
/// An entity without history keeps its extracted current-pose matrix,
/// so a scene can enable interpolation for only part of its content.
/// Nothing in the ECS is modified: the interpolated pose exists only
/// in the returned records.
/// </para>
/// <para>
/// The type caches its queries and allocates nothing per extraction.
/// It is single-threaded and follows the same structural-freeze rules
/// as <see cref="EcsRenderExtractor"/>.
/// </para>
/// </remarks>
public sealed class InterpolatedRenderExtractor
{
	private readonly EcsRenderExtractor _extractor;

	/// <summary>
	/// Creates an interpolated extractor bound to one world.
	/// </summary>
	/// <param name="world">The world to read. It must outlive the extractor.</param>
	public InterpolatedRenderExtractor(World world)
	{
		_extractor = new EcsRenderExtractor(world);
	}

	/// <summary>
	/// Counts the records a complete extraction would produce.
	/// </summary>
	public int CountRequiredInstances()
	{
		return _extractor.CountRequiredInstances();
	}

	/// <summary>
	/// Writes every renderable entity into a caller-owned buffer,
	/// interpolating the pose of entities that have history.
	/// </summary>
	/// <param name="destination">
	/// Storage for the extracted records. It is left unmodified when
	/// it is too small.
	/// </param>
	/// <param name="history">The transform history to read.</param>
	/// <param name="alpha">
	/// The interpolation fraction, normally
	/// <see cref="Core.Timing.GameLoop.InterpolationAlpha"/>.
	/// </param>
	/// <returns>
	/// The written and required record counts.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="history"/> is null.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The world changed during extraction.
	/// </exception>
	/// <remarks>
	/// The alpha is clamped by <see cref="TransformPose.Lerp"/>: at or
	/// below zero the previous pose is used, and at or above one the
	/// current pose is used. Paused rendering should pass one, or use
	/// <see cref="InterpolationWeight"/> to keep the displayed pose
	/// continuous across pause and resume.
	/// </remarks>
	public RenderExtractionResult Extract(
		Span<RenderInstance> destination,
		TransformHistory history,
		float alpha)
	{
		ArgumentNullException.ThrowIfNull(history);

		RenderExtractionResult result = _extractor.Extract(destination);

		if (!result.Succeeded)
		{
			return result;
		}

		for (int index = 0; index < result.WrittenCount; index++)
		{
			ref RenderInstance instance = ref destination[index];

			if (!history.TryGetPose(
				instance.Entity,
				out TransformPose previous,
				out TransformPose current))
			{
				continue;
			}

			Matrix4x4 model = TransformPose
				.Lerp(previous, current, alpha)
				.CreateModelMatrix();

			instance = new RenderInstance(
				instance.Entity,
				instance.Mesh,
				instance.Material,
				model);
		}

		return result;
	}
}
