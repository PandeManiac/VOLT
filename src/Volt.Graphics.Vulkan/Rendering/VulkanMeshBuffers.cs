using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Memory;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Owns uploaded device-local vertex and optional index buffers
/// for one mesh.
/// </summary>
/// <remarks>
/// Vertex and index data are uploaded together through one
/// batched Vulkan transfer when indices are present. Mesh
/// buffers are created with VertexBufferBit and IndexBufferBit
/// usage respectively, plus any caller-supplied additional usage.
/// Draw commands bind both buffers at offset zero.
///
/// This class does not own a graphics pipeline, descriptor set,
/// render pass, swapchain, material, or transform. It performs
/// no drawing and provides no ECS integration.
///
/// The mesh owns both buffers and releases them on disposal.
/// Callers must not dispose the buffers separately. The buffers
/// must remain alive while recorded or submitted GPU commands
/// reference them, and this object must be disposed before its
/// Vulkan logical device is destroyed.
///
/// Disposal may block if the logical device has unresolved
/// transfer execution. This class is not thread-safe.
/// </remarks>
public sealed class VulkanMeshBuffers : IDisposable
{
	private readonly VulkanBuffer _vertexBuffer;
	private readonly VulkanMeshMetadata _metadata;

	private VulkanBuffer? _indexBuffer;

	private bool _disposed;

	/// <summary>
	/// Describes the geometry and buffer sizes of an uploaded mesh.
	/// </summary>
	internal readonly record struct VulkanMeshMetadata(
		uint VertexCount,
		uint VertexStride,
		ulong VertexBufferSize,
		uint IndexCount,
		ulong IndexBufferSize,
		VkIndexType IndexType)
	{
		/// <summary>
		/// Gets whether the mesh has an index buffer.
		/// </summary>
		public bool IsIndexed => IndexCount > 0;
	}

	/// <summary>
	/// Gets the number of vertices in the mesh.
	/// </summary>
	public uint VertexCount
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.VertexCount;
		}
	}

	/// <summary>
	/// Gets the byte stride of one vertex.
	/// </summary>
	public uint VertexStride
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.VertexStride;
		}
	}

	/// <summary>
	/// Gets the byte size of the vertex buffer.
	/// </summary>
	public ulong VertexBufferSize
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.VertexBufferSize;
		}
	}

	/// <summary>
	/// Gets the number of indices in the mesh.
	/// </summary>
	/// <remarks>
	/// This value is zero for a non-indexed mesh.
	/// </remarks>
	public uint IndexCount
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.IndexCount;
		}
	}

	/// <summary>
	/// Gets the byte size of the index buffer.
	/// </summary>
	/// <remarks>
	/// This value is zero for a non-indexed mesh.
	/// </remarks>
	public ulong IndexBufferSize
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.IndexBufferSize;
		}
	}

	/// <summary>
	/// Gets the index type used by the index buffer.
	/// </summary>
	/// <remarks>
	/// The value is Uint16 for a non-indexed mesh and is only
	/// meaningful when <see cref="IsIndexed"/> is true.
	/// </remarks>
	public VkIndexType IndexType
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.IndexType;
		}
	}

	/// <summary>
	/// Gets whether the mesh has an index buffer.
	/// </summary>
	public bool IsIndexed
	{
		get
		{
			ThrowIfDisposed();
			return _metadata.IsIndexed;
		}
	}

	/// <summary>
	/// Gets the owned vertex buffer.
	/// </summary>
	/// <remarks>
	/// Ownership remains with this mesh. Do not dispose the
	/// returned buffer.
	/// </remarks>
	public VulkanBuffer VertexBuffer
	{
		get
		{
			ThrowIfDisposed();
			return _vertexBuffer;
		}
	}

	/// <summary>
	/// Gets the owned index buffer, or null for a non-indexed mesh.
	/// </summary>
	/// <remarks>
	/// Ownership remains with this mesh. Do not dispose the
	/// returned buffer.
	/// </remarks>
	public VulkanBuffer? IndexBuffer
	{
		get
		{
			ThrowIfDisposed();
			return _indexBuffer;
		}
	}

	private VulkanMeshBuffers(
		VulkanBuffer vertexBuffer,
		VulkanBuffer? indexBuffer,
		VulkanMeshMetadata metadata)
	{
		ArgumentNullException.ThrowIfNull(vertexBuffer);

		_vertexBuffer = vertexBuffer;
		_indexBuffer = indexBuffer;
		_metadata = metadata;
	}

	/// <summary>
	/// Uploads a non-indexed mesh into one device-local vertex
	/// buffer.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device used for the upload.</param>
	/// <param name="physicalDevice">
	/// The physical device that owns the logical device.
	/// </param>
	/// <param name="vertices">
	/// Nonempty vertex data. The element size becomes the
	/// vertex stride.
	/// </param>
	/// <param name="additionalUsage">
	/// Additional usage flags applied to both buffers. Supply
	/// TransferSrcBit to permit readback.
	/// </param>
	/// <returns>
	/// A mesh that owns the uploaded vertex buffer.
	/// </returns>
	/// <remarks>
	/// The caller may modify or release the source data after
	/// this method returns.
	/// </remarks>
	public static VulkanMeshBuffers Create<TVertex>(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<TVertex> vertices,
		BufferUsageFlags additionalUsage = BufferUsageFlags.None)
		where TVertex : unmanaged
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (vertices.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertices),
				"A mesh must contain at least one vertex.");
		}

		return CreateCore(
			instance,
			device,
			physicalDevice,
			vertices,
			ReadOnlySpan<byte>.Empty,
			indexCount: 0,
			VkIndexType.Uint16,
			additionalUsage);
	}

	/// <summary>
	/// Uploads a mesh with 16-bit indices into one device-local
	/// vertex buffer and one device-local index buffer.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device used for the upload.</param>
	/// <param name="physicalDevice">
	/// The physical device that owns the logical device.
	/// </param>
	/// <param name="vertices">
	/// Nonempty vertex data. The element size becomes the
	/// vertex stride.
	/// </param>
	/// <param name="indices">
	/// Nonempty 16-bit indices. Every index must reference an
	/// existing vertex.
	/// </param>
	/// <param name="additionalUsage">
	/// Additional usage flags applied to both buffers. Supply
	/// TransferSrcBit to permit readback.
	/// </param>
	/// <returns>
	/// A mesh that owns both uploaded buffers.
	/// </returns>
	/// <remarks>
	/// Both buffers are uploaded through one batched transfer.
	/// The caller may modify or release the source data after
	/// this method returns.
	/// </remarks>
	public static VulkanMeshBuffers Create<TVertex>(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<TVertex> vertices,
		ReadOnlySpan<ushort> indices,
		BufferUsageFlags additionalUsage = BufferUsageFlags.None)
		where TVertex : unmanaged
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (vertices.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertices),
				"A mesh must contain at least one vertex.");
		}

		ValidateIndices(indices, vertices.Length);

		return CreateCore(
			instance,
			device,
			physicalDevice,
			vertices,
			MemoryMarshal.AsBytes(indices),
			checked((uint)indices.Length),
			VkIndexType.Uint16,
			additionalUsage);
	}

	/// <summary>
	/// Uploads a mesh with 32-bit indices into one device-local
	/// vertex buffer and one device-local index buffer.
	/// </summary>
	/// <param name="instance">The Vulkan instance.</param>
	/// <param name="device">The logical device used for the upload.</param>
	/// <param name="physicalDevice">
	/// The physical device that owns the logical device.
	/// </param>
	/// <param name="vertices">
	/// Nonempty vertex data. The element size becomes the
	/// vertex stride.
	/// </param>
	/// <param name="indices">
	/// Nonempty 32-bit indices. Every index must reference an
	/// existing vertex.
	/// </param>
	/// <param name="additionalUsage">
	/// Additional usage flags applied to both buffers. Supply
	/// TransferSrcBit to permit readback.
	/// </param>
	/// <returns>
	/// A mesh that owns both uploaded buffers.
	/// </returns>
	/// <remarks>
	/// Both buffers are uploaded through one batched transfer.
	/// The caller may modify or release the source data after
	/// this method returns.
	/// </remarks>
	public static VulkanMeshBuffers Create<TVertex>(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<TVertex> vertices,
		ReadOnlySpan<uint> indices,
		BufferUsageFlags additionalUsage = BufferUsageFlags.None)
		where TVertex : unmanaged
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (vertices.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertices),
				"A mesh must contain at least one vertex.");
		}

		ValidateIndices(indices, vertices.Length);

		return CreateCore(
			instance,
			device,
			physicalDevice,
			vertices,
			MemoryMarshal.AsBytes(indices),
			checked((uint)indices.Length),
			VkIndexType.Uint32,
			additionalUsage);
	}

	/// <summary>
	/// Releases the owned vertex and index buffers.
	/// </summary>
	/// <remarks>
	/// Disposal is idempotent. Cleanup continues when one buffer
	/// fails to dispose. Multiple failures are reported as an
	/// AggregateException.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		List<Exception>? failures = null;

		try
		{
			_indexBuffer?.Dispose();
		}

		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		try
		{
			_vertexBuffer.Dispose();
		}

		catch (Exception exception)
		{
			(failures ??= []).Add(exception);
		}

		_indexBuffer = null;
		_disposed = true;

		if (failures is { Count: 1 })
		{
			ExceptionDispatchInfo.Capture(
				failures[0]).Throw();
		}

		if (failures is { Count: > 1 })
		{
			throw new AggregateException(
				"Multiple failures occurred while disposing " +
				"Vulkan mesh buffers.",
				failures);
		}
	}

	/// <summary>
	/// Computes and validates mesh metadata without calling Vulkan.
	/// </summary>
	internal static VulkanMeshMetadata CreateMetadata(
		uint vertexCount,
		uint vertexStride,
		uint indexCount,
		VkIndexType indexType)
	{
		if (vertexCount == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexCount),
				"A mesh must contain at least one vertex.");
		}

		if (vertexStride == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexStride),
				"A vertex stride must be positive.");
		}

		ulong vertexBufferSize =
			checked((ulong)vertexCount * vertexStride);

		if (indexCount == 0)
		{
			return new VulkanMeshMetadata(
				vertexCount,
				vertexStride,
				vertexBufferSize,
				IndexCount: 0,
				IndexBufferSize: 0,
				VkIndexType.Uint16);
		}

		uint indexSize = GetIndexSize(indexType);

		ulong indexBufferSize =
			checked((ulong)indexCount * indexSize);

		return new VulkanMeshMetadata(
			vertexCount,
			vertexStride,
			vertexBufferSize,
			indexCount,
			indexBufferSize,
			indexType);
	}

	/// <summary>
	/// Returns the byte width of an index type.
	/// </summary>
	internal static uint GetIndexSize(VkIndexType indexType)
	{
		return indexType switch
		{
			VkIndexType.Uint16 => 2,
			VkIndexType.Uint32 => 4,
			_ => throw new ArgumentOutOfRangeException(
				nameof(indexType),
				indexType,
				"Only 16-bit and 32-bit indices are supported.")
		};
	}

	/// <summary>
	/// Validates upload sizes without calling Vulkan.
	/// </summary>
	/// <remarks>
	/// The batch upload infrastructure stages data in managed
	/// storage addressed by a 32-bit byte count.
	/// </remarks>
	internal static void ValidateUploadSize(
		ulong vertexByteCount,
		ulong indexByteCount)
	{
		if (vertexByteCount == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexByteCount),
				"A mesh must contain at least one vertex.");
		}

		if (vertexByteCount > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexByteCount),
				"The vertex data exceeds the supported staging size.");
		}

		if (indexByteCount > int.MaxValue)
		{
			throw new ArgumentOutOfRangeException(
				nameof(indexByteCount),
				"The index data exceeds the supported staging size.");
		}

		if (vertexByteCount >
			(ulong)int.MaxValue - indexByteCount)
		{
			throw new ArgumentOutOfRangeException(
				nameof(indexByteCount),
				"The combined vertex and index data exceeds " +
				"the supported staging size.");
		}
	}

	/// <summary>
	/// Validates that every 16-bit index references a vertex.
	/// </summary>
	internal static void ValidateIndices(
		ReadOnlySpan<ushort> indices,
		int vertexCount)
	{
		if (vertexCount <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexCount),
				"A mesh must contain at least one vertex.");
		}

		if (indices.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(
				nameof(indices),
				"An indexed mesh must contain at least one index.");
		}

		for (int index = 0; index < indices.Length; index++)
		{
			if (indices[index] >= vertexCount)
			{
				throw new ArgumentOutOfRangeException(
					nameof(indices),
					$"Index {index} references vertex " +
					$"{indices[index]}, but the mesh has " +
					$"{vertexCount} vertices.");
			}
		}
	}

	/// <summary>
	/// Validates that every 32-bit index references a vertex.
	/// </summary>
	internal static void ValidateIndices(
		ReadOnlySpan<uint> indices,
		int vertexCount)
	{
		if (vertexCount <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(vertexCount),
				"A mesh must contain at least one vertex.");
		}

		if (indices.IsEmpty)
		{
			throw new ArgumentOutOfRangeException(
				nameof(indices),
				"An indexed mesh must contain at least one index.");
		}

		ulong vertexLimit = (ulong)vertexCount;

		for (int index = 0; index < indices.Length; index++)
		{
			if (indices[index] >= vertexLimit)
			{
				throw new ArgumentOutOfRangeException(
					nameof(indices),
					$"Index {index} references vertex " +
					$"{indices[index]}, but the mesh has " +
					$"{vertexCount} vertices.");
			}
		}
	}

	private static unsafe VulkanMeshBuffers CreateCore<TVertex>(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		ReadOnlySpan<TVertex> vertices,
		ReadOnlySpan<byte> indexData,
		uint indexCount,
		VkIndexType indexType,
		BufferUsageFlags additionalUsage)
		where TVertex : unmanaged
	{
		uint vertexStride = checked((uint)sizeof(TVertex));

		ulong vertexByteCount =
			checked((ulong)vertices.Length * vertexStride);

		ulong indexByteCount = (ulong)indexData.Length;

		ValidateUploadSize(vertexByteCount, indexByteCount);

		VulkanMeshMetadata metadata = CreateMetadata(
			checked((uint)vertices.Length),
			vertexStride,
			indexCount,
			indexType);

		if (indexByteCount != metadata.IndexBufferSize)
		{
			throw new ArgumentException(
				"Index data does not match the index count " +
				"and index type.",
				nameof(indexData));
		}

		VulkanBufferUploadBatch batch = new();

		batch.Add(
			vertices,
			BufferUsageFlags.VertexBufferBit | additionalUsage);

		if (metadata.IsIndexed)
		{
			batch.AddBytes(
				indexData,
				BufferUsageFlags.IndexBufferBit | additionalUsage);
		}

		VulkanBuffer[] uploaded = batch.Submit(
			instance,
			device,
			physicalDevice);

		try
		{
			int expectedBufferCount =
				metadata.IsIndexed ? 2 : 1;

			if (uploaded.Length != expectedBufferCount)
			{
				throw new InvalidOperationException(
					"The batched upload returned an unexpected " +
					"number of buffers.");
			}

			VulkanBuffer vertexBuffer = uploaded[0];

			VulkanBuffer? indexBuffer = metadata.IsIndexed
				? uploaded[1]
				: null;

			if (vertexBuffer.Size != metadata.VertexBufferSize)
			{
				throw new InvalidOperationException(
					"The uploaded vertex buffer has an unexpected size.");
			}

			if (indexBuffer is not null &&
				indexBuffer.Size != metadata.IndexBufferSize)
			{
				throw new InvalidOperationException(
					"The uploaded index buffer has an unexpected size.");
			}

			return new VulkanMeshBuffers(
				vertexBuffer,
				indexBuffer,
				metadata);
		}

		catch (Exception creationError)
		{
			List<Exception>? cleanupErrors = null;

			foreach (VulkanBuffer buffer in uploaded)
			{
				try
				{
					buffer.Dispose();
				}

				catch (Exception exception)
				{
					(cleanupErrors ??= []).Add(exception);
				}
			}

			if (cleanupErrors is not null)
			{
				cleanupErrors.Insert(0, creationError);

				throw new AggregateException(
					"Mesh buffer creation and resource cleanup " +
					"both reported failures.",
					cleanupErrors);
			}

			throw;
		}
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
