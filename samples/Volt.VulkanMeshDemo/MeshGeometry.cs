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
	/// Creates the three meshes drawn by the sample.
	/// </summary>
	/// <remarks>
	/// The geometries are laid out side by side in normalized device
	/// coordinates. The shader applies aspect correction, so the
	/// left, center, and right meshes remain separated on screen.
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
