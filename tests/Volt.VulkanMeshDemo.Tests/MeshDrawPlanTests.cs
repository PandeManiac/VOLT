using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class MeshDrawPlanTests
{
	[Fact]
	public void CreateDrawPlan_RecordsFourDrawsAcrossThreeMeshes()
	{
		MeshDraw[] plan = CreatePlan();

		Assert.Equal(4, plan.Length);

		int[] meshesDrawn = [0, 1, 2];

		Assert.Equal(
			meshesDrawn,
			plan.Select(draw => draw.MeshIndex).Distinct().ToArray());
	}

	[Fact]
	public void CreateDrawPlan_DrawsTriangleAndHexagonAsCompleteMeshes()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();
		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(geometries);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 0, 0, 3),
			plan[0]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 2, 0, 18),
			plan[3]);
	}

	[Fact]
	public void CreateDrawPlan_DrawsQuadAsTwoPartialRangesOfOneMesh()
	{
		MeshDraw[] plan = CreatePlan();

		Assert.Equal(
			new MeshDraw(MeshDrawKind.ElementRange, 1, 0, 3),
			plan[1]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.ElementRange, 1, 3, 3),
			plan[2]);
	}

	[Fact]
	public void CreateDrawPlan_QuadRangesCoverBothIndexBufferTriangles()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();
		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(geometries);

		ushort[] indices = geometries[1].Indices16;

		MeshDraw firstHalf = plan[1];
		MeshDraw secondHalf = plan[2];

		// The two ranges are contiguous and exhaustive, and each
		// one covers a whole number of triangles.
		Assert.Equal(0u, firstHalf.FirstElement);
		Assert.Equal(
			firstHalf.FirstElement + firstHalf.ElementCount,
			secondHalf.FirstElement);

		Assert.Equal(
			checked((uint)indices.Length),
			secondHalf.FirstElement + secondHalf.ElementCount);

		Assert.Equal(0u, firstHalf.ElementCount % 3);
		Assert.Equal(0u, secondHalf.ElementCount % 3);

		// Each range selects one of the quad's two triangles.
		ushort[] firstTriangle = [0, 1, 2];
		ushort[] secondTriangle = [0, 2, 3];

		Assert.Equal(firstTriangle, indices[..3]);
		Assert.Equal(secondTriangle, indices[3..]);
	}

	[Fact]
	public void CreateDrawPlan_OcclusionSceneDrawsNearBeforeFar()
	{
		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(
			MeshGeometry.CreateDepthOcclusionMeshes());

		Assert.Equal(4, plan.Length);

		// The near red triangle is recorded first. The farther blue
		// quad follows it, split into two ranges of one uploaded mesh,
		// and the separate reference hexagon is recorded last.
		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 0, 0, 3),
			plan[0]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.ElementRange, 1, 0, 3),
			plan[1]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.ElementRange, 1, 3, 3),
			plan[2]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 2, 0, 18),
			plan[3]);
	}

	[Fact]
	public void CreateDrawPlan_OcclusionQuadRangesCoverBothTriangles()
	{
		MeshGeometry[] geometries =
			MeshGeometry.CreateDepthOcclusionMeshes();

		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(geometries);

		ushort[] indices = geometries[1].Indices16;

		Assert.Equal(0u, plan[1].FirstElement);
		Assert.Equal(
			plan[1].FirstElement + plan[1].ElementCount,
			plan[2].FirstElement);

		Assert.Equal(
			checked((uint)indices.Length),
			plan[2].FirstElement + plan[2].ElementCount);

		Assert.Equal([0, 1, 2], indices[..3]);
		Assert.Equal([0, 2, 3], indices[3..]);
	}

	[Fact]
	public void CreateDrawPlan_GallerySceneKeepsFourDraws()
	{
		MeshDraw[] plan = CreatePlan();

		Assert.Equal(4, plan.Length);
		Assert.Equal(3, plan.Select(draw => draw.MeshIndex).Distinct().Count());

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 0, 0, 3),
			plan[0]);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 2, 0, 18),
			plan[3]);
	}

	[Fact]
	public void CreateDrawPlan_RejectsUnexpectedMeshOrder()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();

		(geometries[1], geometries[2]) = (geometries[2], geometries[1]);

		Assert.Throws<ArgumentException>(
			() => MeshRenderer.CreateDrawPlan(geometries));
	}

	private static MeshDraw[] CreatePlan()
	{
		return MeshRenderer.CreateDrawPlan(MeshGeometry.CreateDemoMeshes());
	}
}
