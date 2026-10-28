using System.Numerics;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.Editor.Rendering;

/// <summary>
/// Identifies one editor viewport that renders through its own target.
/// </summary>
internal enum ViewportKind
{
	/// <summary>The authoring view of the edited scene.</summary>
	Scene,

	/// <summary>The running game's camera view.</summary>
	Game
}

/// <summary>
/// One mesh instance a viewport draws.
/// </summary>
/// <param name="MeshId">
/// The resolved built-in mesh identifier. Unknown identifiers are
/// skipped by the renderer instead of failing the frame.
/// </param>
/// <param name="World">The world-space model matrix.</param>
internal readonly record struct ViewportMeshInstance(
	string MeshId,
	Matrix4x4 World);

/// <summary>
/// Everything one viewport needs rendered for one frame.
/// </summary>
/// <remarks>
/// A viewport widget publishes a request while it paints. The request
/// only describes what to draw: it owns no GPU resource, and the
/// renderer consumes it later in the same frame. Publishing replaces
/// the previous request, so a viewport that stops painting stops
/// rendering without any explicit teardown.
/// </remarks>
internal sealed class ViewportRenderRequest
{
	/// <summary>
	/// Gets or sets which viewport produced this request.
	/// </summary>
	public ViewportKind Kind { get; set; }

	/// <summary>
	/// Gets or sets whether the viewport has content to render.
	/// </summary>
	public bool HasContent { get; set; }

	/// <summary>
	/// Gets or sets the panel extent in physical pixels.
	/// </summary>
	public int PixelWidth { get; set; }

	/// <summary>
	/// Gets or sets the panel height in physical pixels.
	/// </summary>
	public int PixelHeight { get; set; }

	/// <summary>
	/// Gets or sets the world-to-view matrix.
	/// </summary>
	public Matrix4x4 View { get; set; } = Matrix4x4.Identity;

	/// <summary>
	/// Gets or sets the view-to-clip matrix, already oriented for a
	/// Vulkan framebuffer.
	/// </summary>
	public Matrix4x4 Projection { get; set; } = Matrix4x4.Identity;

	/// <summary>
	/// Gets the instances to draw, in draw order.
	/// </summary>
	public List<ViewportMeshInstance> Instances { get; } = [];

	/// <summary>
	/// Gets or sets whether the ground grid is drawn.
	/// </summary>
	public bool ShowGrid { get; set; } = true;

	/// <summary>
	/// Gets or sets the background clear colour components.
	/// </summary>
	public Vector4 ClearColor { get; set; } =
		new(0.102f, 0.110f, 0.125f, 1f);
}

/// <summary>
/// Bounds of a built-in mesh in its own local space.
/// </summary>
/// <param name="Center">The local-space centre.</param>
/// <param name="HalfExtent">
/// The half extent along each axis. It doubles as the picking box for
/// that primitive.
/// </param>
internal readonly record struct EditorMeshInfo(
	string Id,
	string DisplayName,
	Vector3 Center,
	Vector3 HalfExtent);

/// <summary>
/// Generates and describes the meshes the editor can author.
/// </summary>
/// <remarks>
/// Only primitives the renderer can actually draw are listed. The
/// identifiers are stable project data: a saved scene stores
/// <c>builtin:cube</c>, not a runtime handle.
/// </remarks>
internal static class EditorMeshLibrary
{
	/// <summary>Identifier of the unit cube.</summary>
	public const string Cube = "builtin:cube";

	/// <summary>Identifier of the unit sphere.</summary>
	public const string Sphere = "builtin:sphere";

	/// <summary>Identifier of the unit plane.</summary>
	public const string Plane = "builtin:plane";

	/// <summary>Identifier of the editor ground grid.</summary>
	public const string Grid = "builtin:grid";

	/// <summary>
	/// Gets every authorable mesh, in menu order.
	/// </summary>
	public static IReadOnlyList<EditorMeshInfo> Meshes { get; } =
	[
		new(Cube, "Cube", Vector3.Zero, new Vector3(0.5f)),
		new(Sphere, "Sphere", Vector3.Zero, new Vector3(0.5f)),
		new(
			Plane,
			"Plane",
			Vector3.Zero,
			new Vector3(0.5f, 0.02f, 0.5f))
	];

	/// <summary>
	/// Resolves a saved mesh reference to a built-in mesh.
	/// </summary>
	/// <param name="meshId">The reference stored in the scene.</param>
	/// <returns>
	/// The matching descriptor, or null when the reference names
	/// something this build cannot draw.
	/// </returns>
	public static EditorMeshInfo? Resolve(string? meshId)
	{
		if (string.IsNullOrWhiteSpace(meshId))
		{
			return null;
		}

		foreach (EditorMeshInfo mesh in Meshes)
		{
			if (string.Equals(
				mesh.Id,
				meshId,
				StringComparison.OrdinalIgnoreCase))
			{
				return mesh;
			}
		}

		return null;
	}

	/// <summary>
	/// Creates the vertex data for one built-in mesh.
	/// </summary>
	/// <param name="meshId">The mesh identifier.</param>
	/// <returns>The generated vertices.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="meshId"/> is not a built-in mesh.
	/// </exception>
	public static VulkanSceneVertex[] CreateVertices(string meshId)
	{
		return meshId switch
		{
			Cube => CreateCube(),
			Sphere => CreateSphere(),
			Plane => CreatePlane(),
			Grid => CreateGrid(),
			_ => throw new ArgumentOutOfRangeException(
				nameof(meshId),
				meshId,
				"Unknown built-in mesh.")
		};
	}

	/// <summary>
	/// Creates one unit cube centred on the origin.
	/// </summary>
	/// <remarks>
	/// Each face has its own shade so an untextured cube still reads as
	/// a solid in a preview.
	/// </remarks>
	private static VulkanSceneVertex[] CreateCube()
	{
		Vector3[] faces =
		[
			new(0.78f, 0.80f, 0.85f),
			new(0.68f, 0.71f, 0.77f),
			new(0.86f, 0.88f, 0.92f),
			new(0.62f, 0.65f, 0.71f),
			new(0.74f, 0.77f, 0.82f),
			new(0.58f, 0.61f, 0.67f)
		];

		// Corners of the unit cube in the order the quad templates
		// below expect.
		Vector3[] corners =
		[
			new(-0.5f, -0.5f, -0.5f),
			new(0.5f, -0.5f, -0.5f),
			new(0.5f, 0.5f, -0.5f),
			new(-0.5f, 0.5f, -0.5f),
			new(-0.5f, -0.5f, 0.5f),
			new(0.5f, -0.5f, 0.5f),
			new(0.5f, 0.5f, 0.5f),
			new(-0.5f, 0.5f, 0.5f)
		];

		int[] quads =
		[
			4, 5, 6, 7,
			1, 0, 3, 2,
			3, 2, 6, 7,
			0, 1, 5, 4,
			5, 1, 2, 6,
			0, 4, 7, 3
		];

		VulkanSceneVertex[] vertices =
			new VulkanSceneVertex[(quads.Length / 4) * 6];

		int index = 0;

		for (int face = 0; face < faces.Length; face++)
		{
			int offset = face * 4;

			AddQuad(
				vertices,
				ref index,
				corners[quads[offset]],
				corners[quads[offset + 1]],
				corners[quads[offset + 2]],
				corners[quads[offset + 3]],
				faces[face]);
		}

		return vertices;
	}

	/// <summary>
	/// Creates a unit sphere of radius one half, centred on the origin.
	/// </summary>
	private static VulkanSceneVertex[] CreateSphere()
	{
		const int LongitudeCount = 20;
		const int LatitudeCount = 10;

		VulkanSceneVertex[] vertices =
			new VulkanSceneVertex[LongitudeCount * LatitudeCount * 6];

		Vector3 first = new(0.86f, 0.66f, 0.36f);
		Vector3 second = new(0.74f, 0.54f, 0.28f);

		int index = 0;

		for (int latitude = 0; latitude < LatitudeCount; latitude++)
		{
			float bottom = (latitude / (float)LatitudeCount) - 0.5f;
			float top = ((latitude + 1) / (float)LatitudeCount) - 0.5f;

			float bottomAngle = bottom * MathF.PI;
			float topAngle = top * MathF.PI;

			Vector3 color = latitude % 2 == 0 ? first : second;

			for (int longitude = 0; longitude < LongitudeCount; longitude++)
			{
				float left = longitude / (float)LongitudeCount;
				float right = (longitude + 1) / (float)LongitudeCount;

				float leftAngle = left * MathF.Tau;
				float rightAngle = right * MathF.Tau;

				Vector3 bottomLeft = SpherePoint(bottomAngle, leftAngle);
				Vector3 topLeft = SpherePoint(topAngle, leftAngle);
				Vector3 topRight = SpherePoint(topAngle, rightAngle);
				Vector3 bottomRight = SpherePoint(bottomAngle, rightAngle);

				vertices[index++] = new VulkanSceneVertex(bottomLeft, color);
				vertices[index++] = new VulkanSceneVertex(topLeft, color);
				vertices[index++] = new VulkanSceneVertex(topRight, color);
				vertices[index++] = new VulkanSceneVertex(bottomLeft, color);
				vertices[index++] = new VulkanSceneVertex(topRight, color);
				vertices[index++] = new VulkanSceneVertex(bottomRight, color);
			}
		}

		return vertices;
	}

	/// <summary>
	/// Creates one horizontal unit plane in the XZ plane.
	/// </summary>
	/// <remarks>
	/// The quad is authored with the geometric normal pointing up, and
	/// the derivative-based lighting in the scene shader makes it
	/// readable from either side.
	/// </remarks>
	private static VulkanSceneVertex[] CreatePlane()
	{
		Vector3 color = new(0.52f, 0.55f, 0.60f);

		return
		[
			new(new Vector3(-0.5f, 0f, 0.5f), color),
			new(new Vector3(-0.5f, 0f, -0.5f), color),
			new(new Vector3(0.5f, 0f, -0.5f), color),
			new(new Vector3(-0.5f, 0f, 0.5f), color),
			new(new Vector3(0.5f, 0f, -0.5f), color),
			new(new Vector3(0.5f, 0f, 0.5f), color)
		];
	}

	/// <summary>
	/// Creates the ground grid as thin quads on the XZ plane.
	/// </summary>
	/// <remarks>
	/// The grid is ordinary geometry rather than a UI overlay, so it is
	/// depth tested against the meshes it sits between. Axis lines are
	/// tinted; every other line shares one muted shade. A line is a
	/// rectangle of two triangles, which keeps the builder's vertex
	/// format identical to every other mesh.
	/// </remarks>
	private static VulkanSceneVertex[] CreateGrid()
	{
		const int Extent = 10;
		const float HalfWidth = 0.012f;

		Vector3 minor = new(0.20f, 0.22f, 0.25f);
		Vector3 major = new(0.28f, 0.30f, 0.34f);
		Vector3 axisX = new(0.62f, 0.22f, 0.24f);
		Vector3 axisZ = new(0.24f, 0.36f, 0.66f);

		List<VulkanSceneVertex> vertices = [];

		for (int line = -Extent; line <= Extent; line++)
		{
			Vector3 color = line == 0
				? axisZ
				: line % 5 == 0
					? major
					: minor;

			AddLine(
				vertices,
				new Vector3(line, 0f, -Extent),
				new Vector3(line, 0f, Extent),
				HalfWidth,
				color);

			color = line == 0
				? axisX
				: line % 5 == 0
					? major
					: minor;

			AddLine(
				vertices,
				new Vector3(-Extent, 0f, line),
				new Vector3(Extent, 0f, line),
				HalfWidth,
				color);
		}

		return [.. vertices];
	}

	private static Vector3 SpherePoint(float latitude, float longitude)
	{
		float radius = MathF.Cos(latitude) * 0.5f;

		return new Vector3(
			radius * MathF.Cos(longitude),
			MathF.Sin(latitude) * 0.5f,
			radius * MathF.Sin(longitude));
	}

	private static void AddQuad(
		VulkanSceneVertex[] vertices,
		ref int index,
		Vector3 first,
		Vector3 second,
		Vector3 third,
		Vector3 fourth,
		Vector3 color)
	{
		vertices[index++] = new VulkanSceneVertex(first, color);
		vertices[index++] = new VulkanSceneVertex(second, color);
		vertices[index++] = new VulkanSceneVertex(third, color);
		vertices[index++] = new VulkanSceneVertex(first, color);
		vertices[index++] = new VulkanSceneVertex(third, color);
		vertices[index++] = new VulkanSceneVertex(fourth, color);
	}

	private static void AddLine(
		List<VulkanSceneVertex> vertices,
		Vector3 start,
		Vector3 end,
		float halfWidth,
		Vector3 color)
	{
		Vector3 direction = end - start;

		if (direction.LengthSquared() < 1e-6f)
		{
			return;
		}

		// The grid lies in the XZ plane, so the perpendicular is
		// always horizontal for either direction.
		Vector3 perpendicular = Vector3.Normalize(
			Vector3.Cross(direction, Vector3.UnitY)) * halfWidth;

		Vector3 a = start - perpendicular;
		Vector3 b = start + perpendicular;
		Vector3 c = end + perpendicular;
		Vector3 d = end - perpendicular;

		vertices.Add(new VulkanSceneVertex(a, color));
		vertices.Add(new VulkanSceneVertex(b, color));
		vertices.Add(new VulkanSceneVertex(c, color));
		vertices.Add(new VulkanSceneVertex(a, color));
		vertices.Add(new VulkanSceneVertex(c, color));
		vertices.Add(new VulkanSceneVertex(d, color));
	}
}
