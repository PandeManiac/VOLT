using System.Numerics;
using System.Runtime.InteropServices;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Vertex input shared by every mesh in the demo.
/// </summary>
/// <remarks>
/// Position and color are each three tightly packed floats.
/// The vertex shader consumes them at locations zero and one.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct MeshVertex
{
	public readonly Vector3 Position;
	public readonly Vector3 Color;

	public MeshVertex(Vector3 position, Vector3 color)
	{
		Position = position;
		Color = color;
	}
}

/// <summary>
/// Identifies the index format used by one demo mesh.
/// </summary>
internal enum MeshIndexFormat
{
	None,
	UInt16,
	UInt32
}

/// <summary>
/// Selects the mesh set and draw scene for one sample run.
/// </summary>
internal enum DemoScene
{
	/// <summary>
	/// The original three separated meshes.
	/// </summary>
	Gallery,

	/// <summary>
	/// Overlapping near and far meshes that prove depth testing.
	/// </summary>
	DepthOcclusion,

	/// <summary>
	/// Local-space meshes drawn through model, view, and projection
	/// matrices from a fixed perspective camera.
	/// </summary>
	CameraTransforms
}

/// <summary>
/// Describes one mesh uploaded once at startup.
/// </summary>
internal sealed class MeshGeometry
{
	private MeshGeometry(
		string name,
		MeshIndexFormat indexFormat,
		MeshVertex[] vertices,
		ushort[] indices16,
		uint[] indices32)
	{
		Name = name;
		IndexFormat = indexFormat;
		Vertices = vertices;
		Indices16 = indices16;
		Indices32 = indices32;
	}

	public string Name { get; }

	public MeshIndexFormat IndexFormat { get; }

	public MeshVertex[] Vertices { get; }

	public ushort[] Indices16 { get; }

	public uint[] Indices32 { get; }

	/// <summary>
	/// Creates the three meshes drawn by the default gallery scene.
	/// </summary>
	/// <remarks>
	/// The geometries are laid out side by side in normalized device
	/// coordinates. The shader applies aspect correction, so the
	/// left, center, and right meshes remain separated on screen.
	/// Every vertex uses depth 0.0, so enabling depth testing leaves
	/// the gallery image unchanged.
	/// </remarks>
	public static MeshGeometry[] CreateDemoMeshes()
	{
		return
		[
			CreateTriangle(),
			CreateQuadWithUInt16Indices(),
			CreateHexagonWithUInt32Indices()
		];
	}

	/// <summary>
	/// Creates the three meshes drawn by the depth-occlusion scene.
	/// </summary>
	/// <remarks>
	/// The array order is the draw order: the near red triangle is
	/// recorded first, the far blue quad second, and the separate
	/// reference hexagon last. The quad is drawn after the triangle
	/// even though it is farther away, so only the depth test can
	/// keep the red overlap visible.
	///
	/// Depths are authored as clip-space Z values in the valid
	/// Vulkan 0..1 range with W = 1.0. Both overlapping meshes use
	/// one depth for every vertex, so the comparison never depends
	/// on interpolated depth differences.
	/// </remarks>
	public static MeshGeometry[] CreateDepthOcclusionMeshes()
	{
		return
		[
			CreateNearTriangle(),
			CreateFarQuadWithUInt16Indices(),
			CreateReferenceHexagonWithUInt32Indices()
		];
	}

	/// <summary>
	/// Creates the three local-space meshes drawn by the camera scene.
	/// </summary>
	/// <remarks>
	/// Positions stay in local space and are small and centred near
	/// the origin. Placement, rotation, and scale come from the
	/// per-draw model matrices in <see cref="CameraTransforms"/>, so
	/// the same uploaded triangle is drawn twice with different
	/// transforms. The array order is the draw order: triangle A,
	/// triangle B, the two index ranges of the quad, then the
	/// hexagon.
	///
	/// Every vertex uses uniform colors so screenshot probes do not
	/// depend on interpolation between differently colored vertices.
	/// </remarks>
	public static MeshGeometry[] CreateCameraTransformMeshes()
	{
		return
		[
			CreateLocalTriangle(),
			CreateLocalQuadWithUInt16Indices(),
			CreateLocalHexagonWithUInt32Indices()
		];
	}

	private static MeshGeometry CreateLocalTriangle()
	{
		Vector3 color = new(0.95f, 0.10f, 0.12f);

		MeshVertex[] vertices =
		[
			new(new Vector3(-0.60f, -0.45f, 0.0f), color),
			new(new Vector3(0.60f, -0.45f, 0.0f), color),
			new(new Vector3(0.0f, 0.55f, 0.0f), color)
		];

		return new MeshGeometry(
			"Local: red triangle (vkCmdDraw, two model transforms)",
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	private static MeshGeometry CreateLocalQuadWithUInt16Indices()
	{
		Vector3 color = new(0.08f, 0.25f, 0.95f);

		MeshVertex[] vertices =
		[
			new(new Vector3(-1.0f, -1.0f, 0.0f), color),
			new(new Vector3(1.0f, -1.0f, 0.0f), color),
			new(new Vector3(1.0f, 1.0f, 0.0f), color),
			new(new Vector3(-1.0f, 1.0f, 0.0f), color)
		];

		ushort[] indices = [0, 1, 2, 0, 2, 3];

		return new MeshGeometry(
			"Local: blue quad (uint16 indices)",
			MeshIndexFormat.UInt16,
			vertices,
			indices,
			[]);
	}

	private static MeshGeometry CreateLocalHexagonWithUInt32Indices()
	{
		const int ringVertexCount = 6;

		Vector3 color = new(0.10f, 0.85f, 0.25f);

		MeshVertex[] vertices = new MeshVertex[ringVertexCount + 1];

		vertices[0] = new MeshVertex(Vector3.Zero, color);

		for (int index = 0; index < ringVertexCount; index++)
		{
			float angle = MathF.Tau * index / ringVertexCount;

			vertices[index + 1] = new MeshVertex(
				new Vector3(
					MathF.Cos(angle),
					MathF.Sin(angle),
					0.0f),
				color);
		}

		uint[] indices = new uint[ringVertexCount * 3];

		for (int index = 0; index < ringVertexCount; index++)
		{
			int offset = index * 3;

			indices[offset] = 0;
			indices[offset + 1] = checked((uint)(index + 1));
			indices[offset + 2] =
				checked((uint)(((index + 1) % ringVertexCount) + 1));
		}

		return new MeshGeometry(
			"Local: green hexagon (uint32 indices)",
			MeshIndexFormat.UInt32,
			vertices,
			[],
			indices);
	}

	private static MeshGeometry CreateNearTriangle()
	{
		const float depth = 0.2f;

		Vector3 color = new(0.95f, 0.10f, 0.12f);

		MeshVertex[] vertices =
		[
			new(new Vector3(-0.30f, 0.24f, depth), color),
			new(new Vector3(-0.62f, -0.24f, depth), color),
			new(new Vector3(0.02f, -0.24f, depth), color)
		];

		return new MeshGeometry(
			"Near: red triangle (depth 0.2, vkCmdDraw)",
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	private static MeshGeometry CreateFarQuadWithUInt16Indices()
	{
		const float depth = 0.8f;

		Vector3 color = new(0.08f, 0.25f, 0.95f);

		MeshVertex[] vertices =
		[
			new(new Vector3(-0.28f, 0.08f, depth), color),
			new(new Vector3(0.39f, 0.08f, depth), color),
			new(new Vector3(0.39f, -0.25f, depth), color),
			new(new Vector3(-0.28f, -0.25f, depth), color)
		];

		ushort[] indices = [0, 1, 2, 0, 2, 3];

		return new MeshGeometry(
			"Far: blue quad (depth 0.8, uint16 indices)",
			MeshIndexFormat.UInt16,
			vertices,
			indices,
			[]);
	}

	private static MeshGeometry CreateReferenceHexagonWithUInt32Indices()
	{
		const int ringVertexCount = 6;
		const float centerX = -0.85f;
		const float radius = 0.14f;
		const float depth = 0.5f;

		Vector3 color = new(0.10f, 0.85f, 0.25f);

		MeshVertex[] vertices = new MeshVertex[ringVertexCount + 1];

		vertices[0] = new MeshVertex(
			new Vector3(centerX, 0.0f, depth),
			color);

		for (int index = 0; index < ringVertexCount; index++)
		{
			float angle =
				MathF.Tau * index / ringVertexCount;

			float x = centerX + (radius * MathF.Cos(angle));
			float y = radius * MathF.Sin(angle);

			vertices[index + 1] = new MeshVertex(
				new Vector3(x, y, depth),
				color);
		}

		uint[] indices = new uint[ringVertexCount * 3];

		for (int index = 0; index < ringVertexCount; index++)
		{
			int offset = index * 3;

			indices[offset] = 0;
			indices[offset + 1] = checked((uint)(index + 1));
			indices[offset + 2] = checked((uint)(((index + 1) % ringVertexCount) + 1));
		}

		return new MeshGeometry(
			"Reference: green hexagon (depth 0.5, uint32 indices)",
			MeshIndexFormat.UInt32,
			vertices,
			[],
			indices);
	}

	private static MeshGeometry CreateTriangle()
	{
		MeshVertex[] vertices =
		[
			new(
				new Vector3(-0.62f, 0.27f, 0.0f),
				new Vector3(0.96f, 0.28f, 0.24f)),
			new(
				new Vector3(-0.84f, -0.23f, 0.0f),
				new Vector3(1.0f, 0.62f, 0.18f)),
			new(
				new Vector3(-0.40f, -0.23f, 0.0f),
				new Vector3(0.98f, 0.40f, 0.46f))
		];

		return new MeshGeometry(
			"Left: triangle (vkCmdDraw)",
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	private static MeshGeometry CreateQuadWithUInt16Indices()
	{
		MeshVertex[] vertices =
		[
			new(
				new Vector3(-0.23f, 0.23f, 0.0f),
				new Vector3(0.14f, 0.86f, 0.44f)),
			new(
				new Vector3(0.23f, 0.23f, 0.0f),
				new Vector3(0.20f, 0.96f, 0.64f)),
			new(
				new Vector3(0.23f, -0.23f, 0.0f),
				new Vector3(0.34f, 0.74f, 0.90f)),
			new(
				new Vector3(-0.23f, -0.23f, 0.0f),
				new Vector3(0.14f, 0.62f, 0.58f))
		];

		ushort[] indices = [0, 1, 2, 0, 2, 3];

		return new MeshGeometry(
			"Center: quad (uint16 indices)",
			MeshIndexFormat.UInt16,
			vertices,
			indices,
			[]);
	}

	private static MeshGeometry CreateHexagonWithUInt32Indices()
	{
		const int ringVertexCount = 6;
		const float centerX = 0.62f;
		const float radius = 0.27f;

		MeshVertex[] vertices = new MeshVertex[ringVertexCount + 1];

		vertices[0] = new MeshVertex(
			new Vector3(centerX, 0.0f, 0.0f),
			new Vector3(0.56f, 0.46f, 1.0f));

		for (int index = 0; index < ringVertexCount; index++)
		{
			float angle =
				MathF.Tau * index / ringVertexCount;

			float x = centerX + (radius * MathF.Cos(angle));
			float y = radius * MathF.Sin(angle);

			float blend = index / (float)ringVertexCount;

			vertices[index + 1] = new MeshVertex(
				new Vector3(x, y, 0.0f),
				new Vector3(
					0.28f + (0.18f * blend),
					0.62f + (0.22f * blend),
					1.0f));
		}

		const uint centerVertexIndex = 0;

		uint[] indices = new uint[ringVertexCount * 3];

		for (int index = 0; index < ringVertexCount; index++)
		{
			int offset = index * 3;
			uint next = checked((uint)(((index + 1) % ringVertexCount) + 1));

			indices[offset] = centerVertexIndex;
			indices[offset + 1] = checked((uint)(index + 1));
			indices[offset + 2] = next;
		}

		return new MeshGeometry(
			"Right: hexagon (uint32 indices)",
			MeshIndexFormat.UInt32,
			vertices,
			[],
			indices);
	}
}
