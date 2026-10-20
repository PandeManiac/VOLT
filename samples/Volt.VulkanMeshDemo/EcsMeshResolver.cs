using Volt.Rendering.Resources;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Maps a mesh handle to one of the sample's uploaded meshes.
/// </summary>
/// <remarks>
/// BufferIndex selects an entry in <see cref="DemoMeshes.Buffers"/>.
/// The geometry describes how that upload is recorded. The complete
/// handle, including its generation, must match a binding; an index
/// alone is never treated as proof that a resource exists.
/// </remarks>
internal readonly record struct EcsMeshBinding(
	int BufferIndex,
	MeshGeometry Geometry);

/// <summary>
/// Resolves generational mesh handles for the ECS scene.
/// </summary>
/// <remarks>
/// This is a deliberately small sample-local table, not a general
/// resource manager. It proves that the rendering stage checks whole
/// handles before touching a GPU buffer.
/// </remarks>
internal sealed class EcsMeshResolver
{
	private readonly MeshHandle[] _handles;
	private readonly EcsMeshBinding[] _bindings;

	private EcsMeshResolver(
		MeshHandle[] handles,
		EcsMeshBinding[] bindings)
	{
		_handles = handles;
		_bindings = bindings;
	}

	/// <summary>
	/// Creates the resolver for the sample's three uploaded meshes.
	/// </summary>
	/// <param name="geometries">
	/// The uploaded geometries, in upload order.
	/// </param>
	public static EcsMeshResolver CreateDefault(
		IReadOnlyList<MeshGeometry> geometries)
	{
		ArgumentNullException.ThrowIfNull(geometries);

		if (geometries.Count != 3 ||
			geometries[0].IndexFormat != MeshIndexFormat.None ||
			geometries[1].IndexFormat != MeshIndexFormat.UInt16 ||
			geometries[2].IndexFormat != MeshIndexFormat.UInt32)
		{
			throw new ArgumentException(
				"The ECS scene requires a non-indexed, a 16-bit " +
				"indexed, and a 32-bit indexed mesh, in that order.",
				nameof(geometries));
		}

		return new EcsMeshResolver(
			[
				EcsDemoScene.TriangleMesh,
				EcsDemoScene.QuadMesh,
				EcsDemoScene.HexagonMesh
			],
			[
				new EcsMeshBinding(0, geometries[0]),
				new EcsMeshBinding(1, geometries[1]),
				new EcsMeshBinding(2, geometries[2])
			]);
	}

	/// <summary>
	/// Attempts to resolve a complete mesh handle.
	/// </summary>
	/// <returns>
	/// True for a live binding whose index and generation both match.
	/// </returns>
	public bool TryResolve(
		MeshHandle handle,
		out EcsMeshBinding binding)
	{
		for (int index = 0; index < _handles.Length; index++)
		{
			if (_handles[index] == handle)
			{
				binding = _bindings[index];
				return true;
			}
		}

		binding = default;
		return false;
	}
}
