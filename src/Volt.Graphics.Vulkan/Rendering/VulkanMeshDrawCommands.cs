using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Records mesh draw commands for a complete mesh, or for a
/// selected element range of that mesh, into an existing command
/// buffer.
/// </summary>
/// <remarks>
/// This helper binds the mesh vertex buffer and, when present,
/// the index buffer before recording one draw call. It does not
/// begin or end command-buffer recording or render passes.
///
/// <see cref="Record"/> draws every element of a mesh.
/// <see cref="RecordRange"/> draws a caller-selected element range.
/// Both paths share one recording implementation, so a range that
/// covers a complete mesh produces the same commands as
/// <see cref="Record"/>.
///
/// The command buffer must be a primary command buffer currently
/// in the recording state. Drawing requires a bound graphics
/// pipeline whose vertex input layout is compatible with the mesh
/// stride and binding zero. The caller must also have established
/// the required render-pass scope, viewport, scissor, descriptor
/// sets, and push constants before recording this draw.
///
/// Host access to the command buffer and mesh buffers must be
/// externally synchronized. The mesh must remain alive until all
/// GPU work referencing its buffers has completed. No implicit
/// GPU wait or submission is performed.
///
/// This class does not own pipelines, render passes, framebuffers,
/// command pools, command buffers, descriptors, materials, or mesh
/// assets.
/// </remarks>
public static unsafe class VulkanMeshDrawCommands
{
	/// <summary>
	/// Describes the draw operation selected for a mesh.
	/// </summary>
	/// <remarks>
	/// VertexCount is the number of vertices drawn by a non-indexed
	/// draw and the mesh vertex count for an indexed draw, which
	/// does not use it. IndexCount is the number of indices drawn by
	/// an indexed draw and zero for a non-indexed draw. FirstElement
	/// is the first vertex of a non-indexed draw or the first index
	/// of an indexed draw. VertexOffset is always zero for a
	/// non-indexed draw.
	/// </remarks>
	internal readonly record struct VulkanMeshDrawInfo(
		bool IsIndexed,
		uint VertexCount,
		uint IndexCount,
		VkIndexType IndexType,
		uint InstanceCount,
		uint FirstElement,
		uint FirstInstance,
		int VertexOffset);

	/// <summary>
	/// Binds the mesh buffers and records one draw command for a
	/// complete mesh.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="commandBuffer">
	/// A valid primary command buffer currently in the recording
	/// state.
	/// </param>
	/// <param name="mesh">The mesh to bind and draw.</param>
	/// <param name="instanceCount">
	/// The number of instances to draw. Defaults to one.
	/// </param>
	/// <remarks>
	/// The vertex buffer is bound at binding zero with offset zero.
	/// The index buffer, when present, is bound at offset zero.
	///
	/// Non-indexed meshes record vkCmdDraw. Indexed meshes record
	/// vkCmdDrawIndexed using the mesh index type and count.
	///
	/// Every element of the mesh is drawn: first element,
	/// vertex offset, and first instance are always zero. Use
	/// <see cref="RecordRange"/> to draw a selected element range.
	/// </remarks>
	public static void Record(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		VulkanMeshBuffers mesh,
		uint instanceCount = 1)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(mesh);

		ValidateCommandBuffer(commandBuffer);

		bool isIndexed = mesh.IsIndexed;

		RecordRangeCore(
			instance,
			commandBuffer,
			mesh,
			firstElement: 0,
			elementCount: isIndexed ? mesh.IndexCount : mesh.VertexCount,
			instanceCount,
			firstInstance: 0,
			vertexOffset: 0);
	}

	/// <summary>
	/// Binds the mesh buffers and records one draw command for a
	/// selected element range of a mesh.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="commandBuffer">
	/// A valid primary command buffer currently in the recording
	/// state.
	/// </param>
	/// <param name="mesh">The mesh to bind and draw.</param>
	/// <param name="firstElement">
	/// The zero-based first element to draw: the first vertex for a
	/// non-indexed mesh, or the first index for an indexed mesh.
	/// </param>
	/// <param name="elementCount">
	/// The number of elements to draw: vertices for a non-indexed
	/// mesh, or indices for an indexed mesh.
	/// </param>
	/// <param name="instanceCount">
	/// The number of instances to draw. Defaults to one.
	/// </param>
	/// <param name="firstInstance">
	/// The instance ID of the first instance to draw. Defaults to
	/// zero.
	/// </param>
	/// <param name="vertexOffset">
	/// A signed offset added to every index before vertex fetch. It
	/// is only valid for an indexed mesh and must be zero for a
	/// non-indexed mesh. Defaults to zero.
	/// </param>
	/// <remarks>
	/// The vertex buffer is bound at binding zero with offset zero.
	/// The index buffer, when present, is bound at offset zero.
	/// Buffers are shared with the mesh: a range never uploads or
	/// duplicates vertex or index data.
	///
	/// A non-indexed mesh records vkCmdDraw with the requested
	/// vertex range. firstElement becomes firstVertex and
	/// elementCount becomes vertexCount. An indexed mesh records
	/// vkCmdDrawIndexed with the requested index range and the
	/// mesh's own index type. firstElement becomes firstIndex and
	/// elementCount becomes indexCount, so the mesh's vertex buffer
	/// and index buffer stay shared between ranges.
	///
	/// The range is validated against the mesh counts before any
	/// Vulkan command is recorded. Use
	/// <c>elementCount &lt;= available - firstElement</c> semantics:
	/// a range that overflows or exceeds the remaining elements is
	/// rejected instead of clamped.
	///
	/// Index data is not inspected. For an indexed range, this API
	/// checks only that the range stays inside the index buffer; it
	/// cannot verify that the indices it selects, plus
	/// vertexOffset, resolve to existing vertices. That validation
	/// remains the caller's responsibility, as does any device
	/// limit that applies to nonzero instancing parameters.
	/// </remarks>
	public static void RecordRange(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		VulkanMeshBuffers mesh,
		uint firstElement,
		uint elementCount,
		uint instanceCount = 1,
		uint firstInstance = 0,
		int vertexOffset = 0)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(mesh);

		ValidateCommandBuffer(commandBuffer);

		RecordRangeCore(
			instance,
			commandBuffer,
			mesh,
			firstElement,
			elementCount,
			instanceCount,
			firstInstance,
			vertexOffset);
	}

	/// <summary>
	/// Validates a mesh element range and records the matching draw
	/// command.
	/// </summary>
	/// <remarks>
	/// This is the shared recording path of <see cref="Record"/> and
	/// <see cref="RecordRange"/>. Every argument is validated before
	/// the first Vulkan command is recorded.
	/// </remarks>
	private static void RecordRangeCore(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		VulkanMeshBuffers mesh,
		uint firstElement,
		uint elementCount,
		uint instanceCount,
		uint firstInstance,
		int vertexOffset)
	{
		VulkanMeshDrawInfo drawInfo = CreateRangeDrawInfo(
			mesh.IsIndexed,
			mesh.VertexCount,
			mesh.IndexCount,
			mesh.IndexType,
			firstElement,
			elementCount,
			instanceCount,
			firstInstance,
			vertexOffset);

		Vk api = instance.Api;

		BindMeshBuffers(instance, commandBuffer, mesh);

		if (drawInfo.IsIndexed)
		{
			api.CmdDrawIndexed(
				commandBuffer,
				drawInfo.IndexCount,
				drawInfo.InstanceCount,
				firstIndex: drawInfo.FirstElement,
				vertexOffset: drawInfo.VertexOffset,
				firstInstance: drawInfo.FirstInstance);
		}

		else
		{
			api.CmdDraw(
				commandBuffer,
				drawInfo.VertexCount,
				drawInfo.InstanceCount,
				firstVertex: drawInfo.FirstElement,
				firstInstance: drawInfo.FirstInstance);
		}
	}

	/// <summary>
	/// Binds a mesh vertex buffer and optional index buffer.
	/// </summary>
	/// <remarks>
	/// This internal entry point allows native recording tests to
	/// validate buffer binding without requiring a graphics pipeline
	/// or render-pass harness. Draw-command selection and arguments
	/// are validated separately through CreateDrawInfo.
	/// </remarks>
	internal static void BindMeshBuffers(
		VulkanInstance instance,
		CommandBuffer commandBuffer,
		VulkanMeshBuffers mesh)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(mesh);

		ValidateCommandBuffer(commandBuffer);

		bool isIndexed = mesh.IsIndexed;
		uint indexCount = mesh.IndexCount;
		VkIndexType indexType = mesh.IndexType;
		VulkanBuffer vertexBuffer = mesh.VertexBuffer;
		VulkanBuffer? indexBuffer = mesh.IndexBuffer;

		if (isIndexed != (indexBuffer is not null))
		{
			throw new InvalidOperationException(
				"The mesh index buffer does not match its indexed state.");
		}

		if (isIndexed && indexCount == 0)
		{
			throw new InvalidOperationException(
				"An indexed mesh requires at least one index.");
		}

		if (isIndexed &&
			indexType != VkIndexType.Uint16 &&
			indexType != VkIndexType.Uint32)
		{
			throw new InvalidOperationException(
				"Only 16-bit and 32-bit indices are supported.");
		}

		Vk api = instance.Api;

		VkBuffer vertexHandle = vertexBuffer.Handle;
		VkBuffer indexHandle = indexBuffer?.Handle ?? default;
		ulong offset = 0;

		api.CmdBindVertexBuffers(
			commandBuffer,
			0,
			1,
			&vertexHandle,
			&offset);

		if (indexBuffer is not null)
		{
			api.CmdBindIndexBuffer(
				commandBuffer,
				indexHandle,
				0,
				indexType);
		}
	}

	/// <summary>
	/// Selects and validates a complete-mesh draw configuration
	/// without recording commands.
	/// </summary>
	/// <remarks>
	/// This is the complete-mesh form of
	/// <see cref="CreateRangeDrawInfo"/>. It forwards the whole mesh
	/// as the element range.
	/// </remarks>
	internal static VulkanMeshDrawInfo CreateDrawInfo(
		bool isIndexed,
		uint vertexCount,
		uint indexCount,
		VkIndexType indexType,
		uint instanceCount)
	{
		return CreateRangeDrawInfo(
			isIndexed,
			vertexCount,
			indexCount,
			indexType,
			firstElement: 0,
			elementCount: isIndexed ? indexCount : vertexCount,
			instanceCount,
			firstInstance: 0,
			vertexOffset: 0);
	}

	/// <summary>
	/// Selects and validates a mesh element range without recording
	/// commands or accessing native buffers.
	/// </summary>
	/// <remarks>
	/// For a non-indexed mesh, firstElement and elementCount describe
	/// a vertex range and are checked against vertexCount. For an
	/// indexed mesh they describe an index range and are checked
	/// against indexCount.
	///
	/// Counts are supplied instead of a mesh so the draw selection
	/// can be validated without a Vulkan device. Bounds are checked
	/// by subtraction, so no range can overflow during validation.
	/// </remarks>
	internal static VulkanMeshDrawInfo CreateRangeDrawInfo(
		bool isIndexed,
		uint vertexCount,
		uint indexCount,
		VkIndexType indexType,
		uint firstElement,
		uint elementCount,
		uint instanceCount,
		uint firstInstance,
		int vertexOffset)
	{
		if (vertexCount == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexCount),
				"A draw requires at least one vertex.");
		}

		if (instanceCount == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(instanceCount),
				"An instance count must be positive.");
		}

		uint availableCount;

		if (isIndexed)
		{
			if (indexCount == 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(indexCount),
					"An indexed draw requires at least one index.");
			}

			if (indexType != VkIndexType.Uint16 &&
				indexType != VkIndexType.Uint32)
			{
				throw new ArgumentOutOfRangeException(
					nameof(indexType),
					indexType,
					"Only 16-bit and 32-bit indices are supported.");
			}

			availableCount = indexCount;
		}

		else
		{
			if (indexCount != 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(indexCount),
					"A non-indexed draw cannot have indices.");
			}

			if (vertexOffset != 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(vertexOffset),
					vertexOffset,
					"A non-indexed draw cannot apply a vertex offset.");
			}

			availableCount = vertexCount;
		}

		ValidateElementRange(availableCount, firstElement, elementCount);

		return new VulkanMeshDrawInfo(
			isIndexed,
			isIndexed ? vertexCount : elementCount,
			isIndexed ? elementCount : 0,
			isIndexed ? indexType : VkIndexType.Uint16,
			instanceCount,
			firstElement,
			firstInstance,
			vertexOffset);
	}

	/// <summary>
	/// Validates an element range against the available element
	/// count using subtraction-based bounds checks.
	/// </summary>
	private static void ValidateElementRange(
		uint availableCount,
		uint firstElement,
		uint elementCount)
	{
		if (elementCount == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(elementCount),
				"A draw range requires at least one element.");
		}

		if (firstElement > availableCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(firstElement),
				firstElement,
				"The first element is outside the available range.");
		}

		if (elementCount > availableCount - firstElement)
		{
			throw new ArgumentOutOfRangeException(
				nameof(elementCount),
				elementCount,
				"The element count exceeds the remaining elements.");
		}
	}

	private static void ValidateCommandBuffer(
		CommandBuffer commandBuffer)
	{
		if (commandBuffer.Handle == 0)
		{
			throw new ArgumentException(
				"A valid Vulkan command buffer handle is required.",
				nameof(commandBuffer));
		}
	}
}
