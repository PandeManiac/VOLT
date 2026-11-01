using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.Editor.Rendering;

/// <summary>
/// Resolves an authored mesh and material reference to geometry a
/// renderer can draw.
/// </summary>
/// <remarks>
/// <para>
/// Scenes store stable identifiers, never runtime handles. A reference
/// may name a built-in primitive or a mesh imported into the project, and
/// every caller - the two viewports, picking, and focusing - goes through
/// here so an object cannot look one way and pick another.
/// </para>
/// <para>
/// The material is applied by tinting the baked vertex colours. The
/// scene shader carries one albedo per vertex and no material binding,
/// so this is what makes an authored colour visible in both the CPU and
/// GPU paths without a second pipeline.
/// </para>
/// </remarks>
internal static class ProjectMeshResolver
{
	private static readonly Vector3 s_light =
		Vector3.Normalize(new Vector3(0.35f, 0.85f, 0.40f));

	private static readonly Vector3 s_albedo =
		MaterialLoader.DefaultAlbedo;

	private static readonly ConditionalWeakTable<
		ImportedMesh,
		VulkanSceneVertex[]> s_vertices = new();

	private static readonly ConditionalWeakTable<
		ImportedMesh,
		ConcurrentDictionary<int, VulkanSceneVertex[]>> s_tinted = new();

	private static readonly ConditionalWeakTable<ImportedMesh, Vector2[]>
		s_uvs = new();

	private static readonly ConditionalWeakTable<
		ImportedMesh,
		ConcurrentDictionary<int, VulkanSceneVertex[]>> s_flat = new();

	private static readonly Dictionary<(string Id, int Rgb), VulkanSceneVertex[]>
		s_builtinTints = [];

	private static readonly Lock s_tintGate = new();

	/// <summary>
	/// Resolves a mesh reference with the default material.
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
		return TryResolve(
			project,
			meshId,
			materialId: null,
			out info,
			out vertices,
			out _);
	}

	/// <summary>
	/// Resolves a mesh and material reference without the texture.
	/// </summary>
	/// <remarks>
	/// Picking and bounds only need the geometry descriptor, so they do
	/// not have to decode an image or build a mapping they will not use.
	/// </remarks>
	public static bool TryResolve(
		EditorProject? project,
		string? meshId,
		string? materialId,
		out EditorMeshInfo info,
		out VulkanSceneVertex[]? vertices)
	{
		return TryResolve(
			project,
			meshId,
			materialId,
			out info,
			out vertices,
			out _);
	}

	public static bool TryResolve(
		EditorProject? project,
		string? meshId,
		out EditorMeshInfo info,
		out VulkanSceneVertex[]? vertices,
		out ViewportTexture? texture)
	{
		return TryResolve(
			project,
			meshId,
			materialId: null,
			out info,
			out vertices,
			out texture);
	}

	/// <summary>
	/// Resolves a mesh and material reference.
	/// </summary>
	/// <param name="project">The open project, when one is open.</param>
	/// <param name="meshId">The mesh reference stored in the scene.</param>
	/// <param name="materialId">
	/// The material reference stored in the scene. Empty and
	/// <c>builtin:default</c> mean no tint, and an unresolvable reference
	/// falls back to the same untinted shading rather than hiding the
	/// object.
	/// </param>
	/// <param name="info">
	/// The descriptor renderers and picking use for this mesh.
	/// </param>
	/// <param name="vertices">
	/// The geometry to draw: imported geometry, tinted built-in geometry
	/// when a material applies, or <see langword="null"/> when the
	/// renderer's own untinted built-in mesh should be used.
	/// </param>
	/// <returns>Whether the reference names something drawable.</returns>
	public static bool TryResolve(
		EditorProject? project,
		string? meshId,
		string? materialId,
		out EditorMeshInfo info,
		out VulkanSceneVertex[]? vertices,
		out ViewportTexture? texture)
	{
		info = default;
		vertices = null;
		texture = null;

		if (string.IsNullOrWhiteSpace(meshId))
		{
			return false;
		}

		MaterialAsset? material = ResolveMaterial(project, materialId);
		Vector3? tint = material?.Albedo;

		if (EditorMeshLibrary.Resolve(meshId) is EditorMeshInfo builtin)
		{
			info = builtin;

			if (tint is Vector3 builtinTint)
			{
				vertices = TintedBuiltin(builtin.Id, builtinTint);
			}

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

		texture = ResolveTexture(project, material, mesh);

		if (texture is not null)
		{
			// A sampled surface is lit by the rasterizer itself, so its
			// vertex colour carries the material albedo alone. Baking the
			// face shade into the colour as well would darken the image
			// twice and make a texture look dimmer than the same material
			// without one.
			Vector3 surface = material?.Albedo ?? Vector3.One;

			vertices = s_flat
				.GetValue(
					mesh,
					_ => new ConcurrentDictionary<int, VulkanSceneVertex[]>())
				.GetOrAdd(RgbKey(surface), _ => Flat(mesh, surface));

			return true;
		}

		VulkanSceneVertex[] source = s_vertices.GetValue(mesh, CreateVertices);

		vertices = tint is Vector3 meshTint
			? s_tinted
				.GetValue(mesh, _ => new ConcurrentDictionary<int, VulkanSceneVertex[]>())
				.GetOrAdd(RgbKey(meshTint), _ => Tint(source, meshTint))
			: source;

		return true;
	}

	private static MaterialAsset? ResolveMaterial(
		EditorProject? project,
		string? materialId)
	{
		if (string.IsNullOrWhiteSpace(materialId) ||
			string.Equals(
				materialId,
				MaterialLoader.DefaultId,
				StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return project is not null &&
			project.TryLoadMaterial(
				materialId,
				out MaterialAsset material,
				out _)
				? material
				: null;
	}

	/// <summary>
	/// Pairs a material's image with an imported mesh's corner mapping.
	/// </summary>
	/// <remarks>
	/// A texture without coordinates, or coordinates without an image,
	/// draw exactly as before in the material colour. The mapping is
	/// cached per mesh because a re-parsed mesh is a new instance, while
	/// the same mesh resolved for many entities shares one array.
	/// </remarks>
	private static ViewportTexture? ResolveTexture(
		EditorProject? project,
		MaterialAsset? material,
		ImportedMesh mesh)
	{
		if (project is null ||
			material?.Texture is not string path ||
			!mesh.HasTextureCoordinates ||
			!project.TryLoadTexture(path, out PngImage image, out _))
		{
			return null;
		}

		return new ViewportTexture(
			image,
			s_uvs.GetValue(mesh, static source => [.. source.Uvs]));
	}

	private static VulkanSceneVertex[] TintedBuiltin(
		string meshId,
		Vector3 albedo)
	{
		(string Id, int Rgb) key = (meshId, RgbKey(albedo));

		lock (s_tintGate)
		{
			if (s_builtinTints.TryGetValue(key, out VulkanSceneVertex[]? cached))
			{
				return cached;
			}

			VulkanSceneVertex[] tinted = Tint(
				EditorMeshLibrary.CreateVertices(meshId),
				albedo);

			s_builtinTints[key] = tinted;

			return tinted;
		}
	}

	private static VulkanSceneVertex[] Flat(
		ImportedMesh mesh,
		Vector3 albedo)
	{
		VulkanSceneVertex[] vertices =
			new VulkanSceneVertex[mesh.Indices.Count];

		for (int index = 0; index < vertices.Length; index++)
		{
			vertices[index] = new VulkanSceneVertex(
				mesh.Positions[mesh.Indices[index]],
				albedo);
		}

		return vertices;
	}

	private static VulkanSceneVertex[] Tint(
		VulkanSceneVertex[] source,
		Vector3 albedo)
	{
		VulkanSceneVertex[] tinted = new VulkanSceneVertex[source.Length];

		for (int index = 0; index < source.Length; index++)
		{
			VulkanSceneVertex vertex = source[index];

			tinted[index] = new VulkanSceneVertex(
				vertex.Position,
				vertex.Color * albedo);
		}

		return tinted;
	}

	private static int RgbKey(Vector3 color)
	{
		int Channel(float value)
		{
			return (int)Math.Clamp(
				MathF.Round(value * 255f),
				0f,
				255f);
		}

		return (Channel(color.X) << 16) |
			(Channel(color.Y) << 8) |
			Channel(color.Z);
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
