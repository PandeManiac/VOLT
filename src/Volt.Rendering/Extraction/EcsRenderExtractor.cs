using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Transforms;

namespace Volt.Rendering.Extraction;

/// <summary>
/// Extracts renderable entities from an ECS world into caller-owned
/// storage.
/// </summary>
/// <remarks>
/// <para>
/// An entity is renderable when it has
/// <see cref="PositionComponent"/>, <see cref="RotationComponent"/>,
/// <see cref="ScaleComponent"/>, and
/// <see cref="MeshRendererComponent"/>, and both of its resource
/// handles are valid. Mesh and material handles are copied as
/// complete generational values: the extractor never treats an index
/// alone as proof that a resource exists, and it never resolves or
/// allocates GPU resources.
/// </para>
/// <para>
/// Entities with an invalid mesh or material handle are omitted from
/// the results. The entity itself is left untouched in the world.
/// Handle resolution, including stale generations, belongs to the
/// stage that maps handles onto live resources.
/// </para>
/// <para>
/// Extraction reads archetype chunks through the ECS query API and
/// writes one <see cref="RenderInstance"/> per matching row. It does
/// not modify the world. An entity's record is a snapshot: later
/// component mutations require another extraction.
/// </para>
/// <para>
/// The output order follows the world's chunk and row traversal. That
/// order is an implementation detail of the ECS, not a stable public
/// contract, so callers must not depend on it for correctness.
/// </para>
/// <para>
/// Extraction is single-threaded. A structural change bumps the
/// world's structural version, which invalidates the active query
/// enumeration: the read fails instead of returning a mixed snapshot.
/// Callers that read while other threads may change structure must
/// hold a <see cref="World.FreezeStructure"/> scope for the whole read
/// and synchronize component writes themselves. The extractor does not
/// take a freeze per call on purpose: that scope is a reference type,
/// so one would be allocated on every frame. A single-threaded caller,
/// such as the sample, needs no freeze at all.
/// </para>
/// <para>
/// The extractor caches its component query and query iterator, and
/// performs no heap allocation per extraction.
/// </para>
/// </remarks>
public sealed class EcsRenderExtractor
{
	private readonly ComponentQuery _query;
	private readonly WorldQuery _chunks;

	/// <summary>
	/// Creates an extractor bound to one world.
	/// </summary>
	/// <param name="world">
	/// The world to read. It must outlive the extractor.
	/// </param>
	public EcsRenderExtractor(World world)
	{
		ArgumentNullException.ThrowIfNull(world);

		ComponentSignature required = new(
			ComponentRegistry.Get<PositionComponent>(),
			ComponentRegistry.Get<RotationComponent>(),
			ComponentRegistry.Get<ScaleComponent>(),
			ComponentRegistry.Get<MeshRendererComponent>());

		_query = new ComponentQuery(required: required);
		_chunks = world.Query(_query);
	}

	/// <summary>
	/// Counts the records a complete extraction would produce.
	/// </summary>
	/// <remarks>
	/// This is the preflight counterpart to
	/// <see cref="Extract"/>. It performs the same archetype and row
	/// traversal without writing anything.
	/// </remarks>
	public int CountRequiredInstances()
	{
		int count = 0;

		foreach (WorldQueryChunk chunk in _chunks)
		{
			Span<MeshRendererComponent> renderers =
				chunk.GetSpan<MeshRendererComponent>();

			for (int row = 0; row < renderers.Length; row++)
			{
				if (IsRenderable(renderers[row]))
				{
					count++;
				}
			}
		}

		return count;
	}

	/// <summary>
	/// Writes every renderable entity into a caller-owned buffer.
	/// </summary>
	/// <param name="destination">
	/// Storage for the extracted records. It is left unmodified when
	/// it is too small.
	/// </param>
	/// <returns>
	/// The written and required record counts. When the destination
	/// is too small, nothing is written and
	/// <see cref="RenderExtractionResult.RequiredCount"/> reports the
	/// capacity a complete extraction needs.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The world was structurally modified between the capacity check
	/// and the write, so the preflight count is no longer valid.
	/// </exception>
	public RenderExtractionResult Extract(
		Span<RenderInstance> destination)
	{
		int required = CountRequiredInstances();

		if (required > destination.Length)
		{
			return new RenderExtractionResult(
				WrittenCount: 0,
				RequiredCount: required);
		}

		int written = ExtractCore(destination);

		if (written != required)
		{
			throw new InvalidOperationException(
				"The world changed during render extraction; " +
				$"expected {required} instances but extracted " +
				$"{written}. Structural changes must not occur " +
				"during extraction.");
		}

		return new RenderExtractionResult(
			WrittenCount: written,
			RequiredCount: required);
	}

	private int ExtractCore(Span<RenderInstance> destination)
	{
		int written = 0;

		foreach (WorldQueryChunk chunk in _chunks)
		{
			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			Span<PositionComponent> positions =
				chunk.GetSpan<PositionComponent>();

			Span<RotationComponent> rotations =
				chunk.GetSpan<RotationComponent>();

			Span<ScaleComponent> scales =
				chunk.GetSpan<ScaleComponent>();

			Span<MeshRendererComponent> renderers =
				chunk.GetSpan<MeshRendererComponent>();

			for (int row = 0; row < entities.Length; row++)
			{
				MeshRendererComponent renderer = renderers[row];

				if (!IsRenderable(renderer))
				{
					continue;
				}

				if ((uint)written >= (uint)destination.Length)
				{
					throw new InvalidOperationException(
						"The world grew during render extraction; " +
						"the destination buffer is too small.");
				}

				Matrix4x4 model =
					TransformMatrices.CreateModelMatrix(
						positions[row],
						rotations[row],
						scales[row]);

				destination[written] = new RenderInstance(
					entities[row],
					renderer.Mesh,
					renderer.Material,
					model);

				written++;
			}
		}

		return written;
	}

	private static bool IsRenderable(
		in MeshRendererComponent renderer)
	{
		return renderer.Mesh.IsValid && renderer.Material.IsValid;
	}
}
