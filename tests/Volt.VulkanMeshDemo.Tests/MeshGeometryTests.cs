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
