using System.Numerics;
using System.Runtime.CompilerServices;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.Editor.Rendering;

/// <summary>
/// Resolves an authored mesh reference to geometry a renderer can draw.
/// </summary>
/// <remarks>
/// <para>
/// Scenes store a stable identifier, never a runtime handle. A reference
/// may name a built-in primitive or a mesh imported into the project, and
/// every caller - the two viewports, picking, and focusing - goes through
/// here so an object cannot look one way and pick another.
/// </para>
/// <para>
/// Imported geometry is converted once per loaded mesh. The conversion
/// is cached against the mesh value, which the project already caches
/// against the file's size and write time, so an edited file produces
/// new vertices and everything downstream picks them up.
/// </para>
/// </remarks>
internal static class ProjectMeshResolver
{
	private static readonly Vector3 s_light =
		Vector3.Normalize(new Vector3(0.35f, 0.85f, 0.40f));

	private static readonly Vector3 s_albedo =
		new(0.78f, 0.80f, 0.85f);

	private static readonly ConditionalWeakTable<
		ImportedMesh,
		VulkanSceneVertex[]> s_vertices = new();

	/// <summary>
	/// Resolves a mesh reference.
	/// </summary>
	/// <param name="project">The open project, when one is open.</param>
	/// <param name="meshId">The reference stored in the scene.</param>
	/// <param name="info">
	/// The descriptor renderers and picking use for this mesh.
	/// </param>
	/// <param name="vertices">
	/// The geometry of an imported mesh, or <see langword="null"/> for a
	/// built-in primitive.
	/// </param>
	/// <returns>Whether the reference names something drawable.</returns>
	public static bool TryResolve(
		EditorProject? project,
		string? meshId,
		out EditorMeshInfo info,
		out VulkanSceneVertex[]? vertices)
	{
		info = default;
		vertices = null;

		if (string.IsNullOrWhiteSpace(meshId))
		{
			return false;
		}

		if (EditorMeshLibrary.Resolve(meshId) is EditorMeshInfo builtin)
		{
			info = builtin;
			return true;
		}

		if (project is null ||
			!project.TryLoadMesh(meshId, out ImportedMesh mesh, out _))
		{
			return false;
		}

		info = new EditorMeshInfo(
			mesh.Id,
			Path.GetFileNameWithoutExtension(mesh.Id),
			mesh.Center,
			mesh.HalfExtent);

		vertices = s_vertices.GetValue(mesh, CreateVertices);

		return true;
	}

	/// <summary>
	/// Converts imported geometry into the scene pipeline's vertex form.
	/// </summary>
	/// <remarks>
	/// The scene shader reads one albedo colour per vertex and no normal,
	/// so the face shade is baked here. Without it an imported mesh would
	/// render as a single flat silhouette, which makes a successful
	/// import look like a failure.
	/// </remarks>
	private static VulkanSceneVertex[] CreateVertices(ImportedMesh mesh)
	{
		VulkanSceneVertex[] vertices = new VulkanSceneVertex[mesh.Indices.Count];

		for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
		{
			int offset = triangle * 3;

			Vector3 first = mesh.Positions[mesh.Indices[offset]];
			Vector3 second = mesh.Positions[mesh.Indices[offset + 1]];
			Vector3 third = mesh.Positions[mesh.Indices[offset + 2]];

			Vector3 normal = Vector3.Cross(second - first, third - first);
			float lengthSquared = normal.LengthSquared();

			float shade = lengthSquared > 1e-12f
				? MathF.Abs(Vector3.Dot(
					normal / MathF.Sqrt(lengthSquared),
					s_light))
				: 0.5f;

			Vector3 color = s_albedo * (0.55f + (0.45f * shade));

			vertices[offset] = new VulkanSceneVertex(first, color);
			vertices[offset + 1] = new VulkanSceneVertex(second, color);
			vertices[offset + 2] = new VulkanSceneVertex(third, color);
		}

		return vertices;
	}
}
