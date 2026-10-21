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
	CameraTransforms,

	/// <summary>
	/// The perspective scene populated from ECS entities.
	/// </summary>
	/// <remarks>
	/// Draw instances, model matrices, and the camera pose are read
	/// from an ECS world through the reusable render extractor.
	/// </remarks>
	EcsScene,

	/// <summary>
	/// The playable first-person target range.
	/// </summary>
	/// <remarks>
	/// Targets, the camera, and the floor are ECS entities drawn
	/// through the same extractor as <see cref="EcsScene"/>. The
	/// aiming reticle is sample-local overlay geometry, not an entity.
	/// </remarks>
	TargetRange
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

	/// <summary>
	/// Creates the four meshes drawn by the target range.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The array order is the upload order and the order the
	/// sample-local resolver binds generational handles in: unhit
	/// target, hit target, floor, reticle.
	/// </para>
	/// <para>
	/// Every mesh is non-indexed. The ECS scene's 16-bit quad is drawn
	/// as two index ranges, which is a property of that scene's
	/// uploaded geometry; the range deliberately avoids 16-bit meshes
	/// so each extracted record is exactly one draw command.
	/// </para>
	/// <para>
	/// The target meshes are unit spheres of radius one: vertex positions
	/// reach radius 1 on every axis, so a target's world-space radius is
	/// its uniform <c>ScaleComponent</c> value, which is what the
	/// collision sphere uses. The faceted surface sits just inside that
	/// radius, so the collider tightly bounds the drawn target rather
	/// than being smaller than it.
	/// </para>
	/// </remarks>
	public static MeshGeometry[] CreateTargetRangeMeshes()
	{
		return
		[
			CreateTargetSphere(
				"Target: unhit unit sphere (radius 1, vkCmdDraw)",
				new Vector3(0.95f, 0.16f, 0.14f),
				new Vector3(0.72f, 0.10f, 0.10f)),
			CreateTargetSphere(
				"Target: hit unit sphere (radius 1, vkCmdDraw)",
				new Vector3(0.15f, 0.55f, 1.0f),
				new Vector3(0.10f, 0.35f, 0.75f)),
			CreateRangeFloor(),
			CreateReticle()
		];
	}

	/// <summary>
	/// Creates a unit sphere out of latitude bands with alternating
	/// shades.
	/// </summary>
	/// <remarks>
	/// The alternating shades read as horizontal stripes, which makes a
	/// target easy to tell apart from the dark background and from the
	/// floor. Positions span exactly -1..1 on every axis, so the model
	/// matrix's scale is the sphere radius.
	/// </remarks>
	private static MeshGeometry CreateTargetSphere(
		string name,
		Vector3 firstShade,
		Vector3 secondShade)
	{
		const int longitudeCount = 12;
		const int latitudeCount = 6;

		MeshVertex[] vertices =
			new MeshVertex[longitudeCount * latitudeCount * 6];

		int vertexIndex = 0;

		for (int latitude = 0; latitude < latitudeCount; latitude++)
		{
			float bottom = (latitude / (float)latitudeCount) - 0.5f;
			float top = ((latitude + 1) / (float)latitudeCount) - 0.5f;

			float bottomAngle = bottom * MathF.PI;
			float topAngle = top * MathF.PI;

			Vector3 color = (latitude % 2) == 0
				? firstShade
				: secondShade;

			for (int longitude = 0; longitude < longitudeCount; longitude++)
			{
				float left = longitude / (float)longitudeCount;
				float right = (longitude + 1) / (float)longitudeCount;

				float leftAngle = left * MathF.Tau;
				float rightAngle = right * MathF.Tau;

				Vector3 bottomLeft = SpherePoint(bottomAngle, leftAngle);
				Vector3 topLeft = SpherePoint(topAngle, leftAngle);
				Vector3 topRight = SpherePoint(topAngle, rightAngle);
				Vector3 bottomRight =
					SpherePoint(bottomAngle, rightAngle);

				vertices[vertexIndex++] =
					new MeshVertex(bottomLeft, color);

				vertices[vertexIndex++] =
					new MeshVertex(topLeft, color);

				vertices[vertexIndex++] =
					new MeshVertex(topRight, color);

				vertices[vertexIndex++] =
					new MeshVertex(bottomLeft, color);

				vertices[vertexIndex++] =
					new MeshVertex(topRight, color);

				vertices[vertexIndex++] =
					new MeshVertex(bottomRight, color);
			}
		}

		return new MeshGeometry(
			name,
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	/// <summary>
	/// Creates the range floor: one horizontal quad in the XZ plane.
	/// </summary>
	private static MeshGeometry CreateRangeFloor()
	{
		Vector3 color = new(0.15f, 0.17f, 0.21f);

		MeshVertex[] vertices =
		[
			new(new Vector3(-1.0f, 0.0f, -1.0f), color),
			new(new Vector3(-1.0f, 0.0f, 1.0f), color),
			new(new Vector3(1.0f, 0.0f, 1.0f), color),
			new(new Vector3(-1.0f, 0.0f, -1.0f), color),
			new(new Vector3(1.0f, 0.0f, 1.0f), color),
			new(new Vector3(1.0f, 0.0f, -1.0f), color)
		];

		return new MeshGeometry(
			"Range: floor quad in the XZ plane (vkCmdDraw)",
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	/// <summary>
	/// Creates the aiming reticle as a plus made of two rectangles.
	/// </summary>
	/// <remarks>
	/// Positions are clip-space units, not world units: the renderer
	/// scales them to a fixed pixel size around the clip-space origin,
	/// which is exactly where the camera's forward vector projects. The
	/// arms are 25% of the half size thick, so the centre of the plus
	/// is solid and stays readable at small framebuffer sizes.
	/// </remarks>
	private static MeshGeometry CreateReticle()
	{
		Vector3 color = new(0.95f, 0.95f, 0.95f);

		const float thickness = 0.25f;

		MeshVertex[] vertices =
		[
			new(new Vector3(-1.0f, -thickness, 0.0f), color),
			new(new Vector3(1.0f, -thickness, 0.0f), color),
			new(new Vector3(1.0f, thickness, 0.0f), color),
			new(new Vector3(-1.0f, -thickness, 0.0f), color),
			new(new Vector3(1.0f, thickness, 0.0f), color),
			new(new Vector3(-1.0f, thickness, 0.0f), color),
			new(new Vector3(-thickness, -1.0f, 0.0f), color),
			new(new Vector3(thickness, -1.0f, 0.0f), color),
			new(new Vector3(thickness, 1.0f, 0.0f), color),
			new(new Vector3(-thickness, -1.0f, 0.0f), color),
			new(new Vector3(thickness, 1.0f, 0.0f), color),
			new(new Vector3(-thickness, 1.0f, 0.0f), color)
		];

		return new MeshGeometry(
			"Range: aiming reticle (clip-space plus)",
			MeshIndexFormat.None,
			vertices,
			[],
			[]);
	}

	private static Vector3 SpherePoint(float latitude, float longitude)
	{
		float radius = MathF.Cos(latitude);

		return new Vector3(
			radius * MathF.Cos(longitude),
			MathF.Sin(latitude),
			radius * MathF.Sin(longitude));
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
