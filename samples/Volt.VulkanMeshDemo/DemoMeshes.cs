using System.Runtime.ExceptionServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Owns the three uploaded demo meshes for the lifetime of the
/// logical device.
/// </summary>
/// <remarks>
/// Mesh data does not depend on a swapchain generation, so it is
/// uploaded once and reused across swapchain recreation. The caller
/// must establish GPU completion before disposing these buffers.
///
/// Disposal is idempotent. Cleanup continues when one mesh fails to
/// dispose. Multiple failures are reported as an AggregateException.
/// </remarks>
internal sealed class DemoMeshes : IDisposable
{
	private readonly MeshGeometry[] _geometries;
	private readonly VulkanMeshBuffers[] _buffers;

	private bool _disposed;

	public DemoScene Scene { get; }

	public IReadOnlyList<MeshGeometry> Geometries => _geometries;

	public IReadOnlyList<VulkanMeshBuffers> Buffers => _buffers;

	private DemoMeshes(
		DemoScene scene,
		MeshGeometry[] geometries,
		VulkanMeshBuffers[] buffers)
	{
		Scene = scene;
		_geometries = geometries;
		_buffers = buffers;
	}

	/// <summary>
	/// Creates and uploads the three meshes used by the selected
	/// sample scene.
	/// </summary>
	/// <remarks>
	/// The gallery scene remains the default. Every scene provides a
	/// non-indexed mesh, a 16-bit indexed mesh, and a 32-bit indexed
	/// mesh in the same order, so the upload and draw-plan paths are
	/// shared. The camera and ECS scenes reuse one uploaded triangle
	/// mesh for both of their triangle instances; the ECS scene
	/// resolves those uploads through generational mesh handles
	/// instead of a static draw plan.
	/// </remarks>
	public static DemoMeshes Create(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		DemoScene scene = DemoScene.Gallery)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(device);

		if (physicalDevice.Handle == 0)
		{
			throw new ArgumentException(
				"A valid physical device is required.",
				nameof(physicalDevice));
		}

		MeshGeometry[] geometries = scene switch
		{
			DemoScene.Gallery => MeshGeometry.CreateDemoMeshes(),
			DemoScene.DepthOcclusion =>
				MeshGeometry.CreateDepthOcclusionMeshes(),
			DemoScene.CameraTransforms =>
				MeshGeometry.CreateCameraTransformMeshes(),
			DemoScene.EcsScene =>
				MeshGeometry.CreateCameraTransformMeshes(),
			DemoScene.TargetRange =>
				MeshGeometry.CreateTargetRangeMeshes(),
			_ => throw new ArgumentOutOfRangeException(
				nameof(scene),
				scene,
				"Unsupported demo scene.")
		};

		VulkanMeshBuffers[] buffers =
			new VulkanMeshBuffers[geometries.Length];

		int createdCount = 0;

		try
		{
			for (int index = 0; index < geometries.Length; index++)
			{
				buffers[index] = CreateMeshBuffers(
					instance,
					device,
					physicalDevice,
					geometries[index]);

				createdCount++;
			}
		}

		catch
		{
			DisposeBuffers(buffers, createdCount);
			throw;
		}

		return new DemoMeshes(scene, geometries, buffers);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		DisposeBuffers(_buffers, _buffers.Length);
		_disposed = true;
	}

	private static VulkanMeshBuffers CreateMeshBuffers(
		VulkanInstance instance,
		VulkanLogicalDevice device,
		PhysicalDevice physicalDevice,
		MeshGeometry geometry)
	{
		return geometry.IndexFormat switch
		{
			MeshIndexFormat.None => VulkanMeshBuffers.Create(
				instance,
				device,
				physicalDevice,
				geometry.Vertices.AsSpan()),

			MeshIndexFormat.UInt16 => VulkanMeshBuffers.Create(
				instance,
				device,
				physicalDevice,
				geometry.Vertices.AsSpan(),
				geometry.Indices16.AsSpan()),

			MeshIndexFormat.UInt32 => VulkanMeshBuffers.Create(
				instance,
				device,
				physicalDevice,
				geometry.Vertices.AsSpan(),
				geometry.Indices32.AsSpan()),

			_ => throw new ArgumentOutOfRangeException(
				nameof(geometry),
				geometry.IndexFormat,
				"Unsupported mesh index format.")
		};
	}

	private static void DisposeBuffers(
		VulkanMeshBuffers?[] buffers,
		int count)
	{
		List<Exception>? failures = null;

		for (int index = count - 1; index >= 0; index--)
		{
			try
			{
				buffers[index]?.Dispose();
			}

			catch (Exception exception)
			{
				(failures ??= []).Add(exception);
			}
		}

		if (failures is { Count: 1 })
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}

		if (failures is { Count: > 1 })
		{
			throw new AggregateException(
				"Multiple failures occurred while disposing demo meshes.",
				failures);
		}
	}
}
