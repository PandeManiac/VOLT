using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class MeshGeometryTests
{
	[Fact]
	public void CreateDemoMeshes_CoversEveryDrawPath()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDemoMeshes();

		Assert.Equal(3, meshes.Length);

		Assert.Equal(MeshIndexFormat.None, meshes[0].IndexFormat);
		Assert.Equal(MeshIndexFormat.UInt16, meshes[1].IndexFormat);
		Assert.Equal(MeshIndexFormat.UInt32, meshes[2].IndexFormat);

		Assert.Contains("vkCmdDraw", meshes[0].Name);
		Assert.Contains("uint16", meshes[1].Name);
		Assert.Contains("uint32", meshes[2].Name);
	}

	[Fact]
	public void CreateDemoMeshes_UsesExpectedVertexAndIndexCounts()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDemoMeshes();

		Assert.Equal(3, meshes[0].Vertices.Length);
		Assert.Empty(meshes[0].Indices16);
		Assert.Empty(meshes[0].Indices32);

		Assert.Equal(4, meshes[1].Vertices.Length);
		Assert.Equal(6, meshes[1].Indices16.Length);
		Assert.Empty(meshes[1].Indices32);

		Assert.Equal(7, meshes[2].Vertices.Length);
		Assert.Empty(meshes[2].Indices16);
		Assert.Equal(18, meshes[2].Indices32.Length);
	}

	[Fact]
	public void CreateDemoMeshes_EveryIndexReferencesAnExistingVertex()
	{
		foreach (MeshGeometry mesh in MeshGeometry.CreateDemoMeshes())
		{
			switch (mesh.IndexFormat)
			{
				case MeshIndexFormat.None:
					Assert.Empty(mesh.Indices16);
					Assert.Empty(mesh.Indices32);
					break;

				case MeshIndexFormat.UInt16:
					Assert.Equal(0, mesh.Indices16.Length % 3);
					Assert.All(
						mesh.Indices16,
						index => Assert.True(
							index < mesh.Vertices.Length,
							$"Index {index} exceeds " +
							$"{mesh.Vertices.Length} vertices."));
					break;

				case MeshIndexFormat.UInt32:
					Assert.Equal(0, mesh.Indices32.Length % 3);
					Assert.All(
						mesh.Indices32,
						index => Assert.True(
							index < mesh.Vertices.Length,
							$"Index {index} exceeds " +
							$"{mesh.Vertices.Length} vertices."));
					break;

				default:
					throw new InvalidOperationException(
						$"Unexpected index format: {mesh.IndexFormat}.");
			}
		}
	}

	[Fact]
	public void CreateDemoMeshes_EveryVertexKeepsZeroDepth()
	{
		foreach (MeshGeometry mesh in MeshGeometry.CreateDemoMeshes())
		{
			Assert.All(
				mesh.Vertices,
				vertex => Assert.Equal(0.0f, vertex.Position.Z));
		}
	}

	[Fact]
	public void CreateDepthOcclusionMeshes_UsesExpectedDepthAndColors()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDepthOcclusionMeshes();

		Assert.All(
			meshes[0].Vertices,
			vertex => Assert.Equal(0.2f, vertex.Position.Z));

		Assert.All(
			meshes[1].Vertices,
			vertex => Assert.Equal(0.8f, vertex.Position.Z));

		Assert.All(
			meshes[2].Vertices,
			vertex => Assert.Equal(0.5f, vertex.Position.Z));

		// The near triangle is red and the far quad is blue, so the
		// overlap colour identifies which one won the depth test.
		Assert.All(
			meshes[0].Vertices,
			vertex => Assert.True(
				vertex.Color.X > 0.9f &&
				vertex.Color.Y < 0.2f &&
				vertex.Color.Z < 0.2f));

		Assert.All(
			meshes[1].Vertices,
			vertex => Assert.True(
				vertex.Color.Z > 0.9f &&
				vertex.Color.X < 0.2f &&
				vertex.Color.Y < 0.4f));

		// The near triangle is authored in front of the far quad and
		// is also drawn first.
		Assert.True(
			meshes[0].Vertices[0].Position.Z <
			meshes[1].Vertices[0].Position.Z);
	}

	[Fact]
	public void CreateDepthOcclusionMeshes_CoversEveryDrawPath()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDepthOcclusionMeshes();

		Assert.Equal(3, meshes.Length);
		Assert.Equal(MeshIndexFormat.None, meshes[0].IndexFormat);
		Assert.Equal(MeshIndexFormat.UInt16, meshes[1].IndexFormat);
		Assert.Equal(MeshIndexFormat.UInt32, meshes[2].IndexFormat);

		Assert.Equal(3, meshes[0].Vertices.Length);
		Assert.Equal(4, meshes[1].Vertices.Length);
		Assert.Equal(6, meshes[1].Indices16.Length);
		Assert.Equal(7, meshes[2].Vertices.Length);
		Assert.Equal(18, meshes[2].Indices32.Length);

		Assert.Contains("red", meshes[0].Name);
		Assert.Contains("blue", meshes[1].Name);
		Assert.Contains("uint32", meshes[2].Name);
	}

	[Fact]
	public void CreateDepthOcclusionMeshes_EveryIndexReferencesAnExistingVertex()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDepthOcclusionMeshes();

		Assert.Empty(meshes[0].Indices16);
		Assert.Empty(meshes[0].Indices32);

		Assert.Equal(0, meshes[1].Indices16.Length % 3);
		Assert.All(
			meshes[1].Indices16,
			index => Assert.True(index < meshes[1].Vertices.Length));

		Assert.Equal(0, meshes[2].Indices32.Length % 3);
		Assert.All(
			meshes[2].Indices32,
			index => Assert.True(index < meshes[2].Vertices.Length));
	}

	[Fact]
	public void CreateDepthOcclusionMeshes_ProbesLieInsideExpectedGeometry()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDepthOcclusionMeshes();

		Vector2[] nearPixels =
			[.. meshes[0].Vertices.Select(v => ToPixels(v.Position))];

		Vector2[] quadPixels =
			[.. meshes[1].Vertices.Select(v => ToPixels(v.Position))];

		Vector2 overlap = new(580.0f, 400.0f);
		Vector2 farOnly = new(720.0f, 400.0f);

		Assert.True(IsInsideTriangle(
			overlap, nearPixels[0], nearPixels[1], nearPixels[2]));

		Assert.True(IsInsideQuad(overlap, quadPixels));

		Assert.False(IsInsideTriangle(
			farOnly, nearPixels[0], nearPixels[1], nearPixels[2]));

		Assert.True(IsInsideQuad(farOnly, quadPixels));

		// Both probes stay well away from triangle and quad edges,
		// so they cannot land on a rasterized or antialiased edge.
		Assert.True(MinTriangleEdgeDistance(
			overlap, nearPixels[0], nearPixels[1], nearPixels[2]) > 20.0f);

		Assert.True(MinTriangleEdgeDistance(
			farOnly, nearPixels[0], nearPixels[1], nearPixels[2]) > 20.0f);

		Assert.True(MinQuadEdgeDistance(overlap, quadPixels) > 20.0f);
		Assert.True(MinQuadEdgeDistance(farOnly, quadPixels) > 20.0f);
	}

	[Fact]
	public void CreateDepthOcclusionMeshes_ReferenceIsSeparated()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateDepthOcclusionMeshes();

		Vector2[] referencePixels =
			[.. meshes[2].Vertices.Select(v => ToPixels(v.Position))];

		float referenceMaxX = referencePixels.Max(p => p.X);

		float nearMinX = meshes[0].Vertices
			.Select(v => ToPixels(v.Position).X)
			.Min();

		float quadMinX = meshes[1].Vertices
			.Select(v => ToPixels(v.Position).X)
			.Min();

		Assert.True(referenceMaxX + 20.0f < nearMinX);
		Assert.True(referenceMaxX + 20.0f < quadMinX);
	}

	private const int ProbeWidth = 1280;
	private const int ProbeHeight = 720;

	/// <summary>
	/// Mirrors the sample vertex shader's aspect correction and Y flip
	/// for the fixed 1280x720 test viewport.
	/// </summary>
	private static Vector2 ToPixels(Vector3 position)
	{
		float aspect = ProbeWidth / (float)ProbeHeight;
		float horizontalScale = 1.0f / MathF.Max(aspect, 1.0f);
		float verticalScale = MathF.Min(aspect, 1.0f);

		float ndcX = position.X * horizontalScale;
		float ndcY = -position.Y * verticalScale;

		return new Vector2(
			(ndcX + 1.0f) * 0.5f * ProbeWidth,
			(ndcY + 1.0f) * 0.5f * ProbeHeight);
	}

	private static bool IsInsideTriangle(
		Vector2 point,
		Vector2 a,
		Vector2 b,
		Vector2 c)
	{
		static float Cross(Vector2 p, Vector2 q, Vector2 r)
		{
			return ((q.X - p.X) * (r.Y - p.Y)) -
				((q.Y - p.Y) * (r.X - p.X));
		}

		float first = Cross(a, b, point);
		float second = Cross(b, c, point);
		float third = Cross(c, a, point);

		bool hasNegative = first < 0.0f || second < 0.0f || third < 0.0f;
		bool hasPositive = first > 0.0f || second > 0.0f || third > 0.0f;

		return !(hasNegative && hasPositive);
	}

	private static bool IsInsideQuad(Vector2 point, Vector2[] quad)
	{
		return IsInsideTriangle(point, quad[0], quad[1], quad[2]) ||
			IsInsideTriangle(point, quad[0], quad[2], quad[3]);
	}

	private static float MinTriangleEdgeDistance(
		Vector2 point,
		Vector2 a,
		Vector2 b,
		Vector2 c)
	{
		return MathF.Min(
			MathF.Min(
				DistanceToSegment(point, a, b),
				DistanceToSegment(point, b, c)),
			DistanceToSegment(point, c, a));
	}

	private static float MinQuadEdgeDistance(Vector2 point, Vector2[] quad)
	{
		float minimum = float.MaxValue;

		for (int index = 0; index < quad.Length; index++)
		{
			minimum = MathF.Min(
				minimum,
				DistanceToSegment(
					point,
					quad[index],
					quad[(index + 1) % quad.Length]));
		}

		return minimum;
	}

	private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
	{
		Vector2 edge = b - a;
		float lengthSquared = edge.LengthSquared();

		float t = lengthSquared == 0.0f
			? 0.0f
			: Math.Clamp(
				Vector2.Dot(point - a, edge) / lengthSquared,
				0.0f,
				1.0f);

		return Vector2.Distance(point, a + (edge * t));
	}

	[Fact]
	public void CreateDemoMeshes_HexagonFansFromCenterVertexWithNondegenerateTriangles()
	{
		MeshGeometry hexagon = MeshGeometry.CreateDemoMeshes()[2];

		const int ringVertexCount = 6;
		const uint centerVertexIndex = 0;

		Assert.Equal(
			ringVertexCount * 3,
			hexagon.Indices32.Length);

		for (int triangle = 0; triangle < ringVertexCount; triangle++)
		{
			int offset = triangle * 3;

			uint center = hexagon.Indices32[offset];
			uint firstRing = hexagon.Indices32[offset + 1];
			uint secondRing = hexagon.Indices32[offset + 2];

			Assert.Equal(centerVertexIndex, center);
			Assert.Equal(
				checked((uint)(triangle + 1)),
				firstRing);
			Assert.Equal(
				checked((uint)(((triangle + 1) % ringVertexCount) + 1)),
				secondRing);

			Assert.NotEqual(center, firstRing);
			Assert.NotEqual(center, secondRing);
			Assert.NotEqual(firstRing, secondRing);

			Vector3 centerPosition =
				hexagon.Vertices[checked((int)center)].Position;

			Vector3 firstPosition =
				hexagon.Vertices[checked((int)firstRing)].Position;

			Vector3 secondPosition =
				hexagon.Vertices[checked((int)secondRing)].Position;

			Vector3 firstEdge = firstPosition - centerPosition;
			Vector3 secondEdge = secondPosition - centerPosition;

			float area =
				Vector3.Cross(firstEdge, secondEdge).Length() * 0.5f;

			Assert.True(
				area > 0.001f,
				$"Triangle {triangle} has zero or near-zero area.");
		}
	}
}
