using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Records complete mesh draw commands into an existing command
/// buffer.
/// </summary>
/// <remarks>
/// This helper binds the mesh vertex buffer and, when present,
/// the index buffer before recording one draw call. It does not
/// begin or end command-buffer recording or render passes.
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
	internal readonly record struct VulkanMeshDrawInfo(
		bool IsIndexed,
		uint VertexCount,
		uint IndexCount,
		VkIndexType IndexType,
		uint InstanceCount);

	/// <summary>
	/// Binds the mesh buffers and records one draw command.
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
	/// firstVertex, firstIndex, vertexOffset, and firstInstance are
	/// always zero.
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

		VulkanMeshDrawInfo drawInfo = CreateDrawInfo(
			mesh.IsIndexed,
			mesh.VertexCount,
			mesh.IndexCount,
			mesh.IndexType,
			instanceCount);

		Vk api = instance.Api;

		BindMeshBuffers(instance, commandBuffer, mesh);

		if (drawInfo.IsIndexed)
		{
			api.CmdDrawIndexed(
				commandBuffer,
				drawInfo.IndexCount,
				drawInfo.InstanceCount,
				firstIndex: 0,
				vertexOffset: 0,
				firstInstance: 0);
		}

		else
		{
			api.CmdDraw(
				commandBuffer,
				drawInfo.VertexCount,
				drawInfo.InstanceCount,
				firstVertex: 0,
				firstInstance: 0);
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
	/// Selects and validates a mesh draw configuration without
	/// recording commands.
	/// </summary>
	internal static VulkanMeshDrawInfo CreateDrawInfo(
		bool isIndexed,
		uint vertexCount,
		uint indexCount,
		VkIndexType indexType,
		uint instanceCount)
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

			return new VulkanMeshDrawInfo(
				true,
				vertexCount,
				indexCount,
				indexType,
				instanceCount);
		}

		if (indexCount != 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(indexCount),
				"A non-indexed draw cannot have indices.");
		}

		return new VulkanMeshDrawInfo(
			false,
			vertexCount,
			0,
			VkIndexType.Uint16,
			instanceCount);
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
