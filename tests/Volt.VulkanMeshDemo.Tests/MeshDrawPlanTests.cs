using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class MeshDrawPlanTests
{
	[Fact]
	public void CreateDrawPlan_GalleryRecordsFourDrawsAcrossThreeMeshes()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.Gallery);

		Assert.Equal(4, plan.Length);

		int[] meshesDrawn = [0, 1, 2];

		Assert.Equal(
			meshesDrawn,
			plan.Select(draw => draw.MeshIndex).Distinct().ToArray());
	}

	[Fact]
	public void CreateDrawPlan_GalleryDrawsTriangleAndHexagonAsCompleteMeshes()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();
		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(
			DemoScene.Gallery,
			geometries);

		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 0, 0, 3, Matrix4x4.Identity),
			plan[0]);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.CompleteMesh,
				2,
				0,
				18,
				Matrix4x4.Identity),
			plan[3]);
	}

	[Fact]
	public void CreateDrawPlan_GalleryDrawsQuadAsTwoPartialRangesOfOneMesh()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.Gallery);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.ElementRange,
				1,
				0,
				3,
				Matrix4x4.Identity),
			plan[1]);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.ElementRange,
				1,
				3,
				3,
				Matrix4x4.Identity),
			plan[2]);
	}

	[Fact]
	public void CreateDrawPlan_GalleryQuadRangesCoverBothIndexBufferTriangles()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();
		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(
			DemoScene.Gallery,
			geometries);

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
	public void CreateDrawPlan_GalleryKeepsIdentityModelTransforms()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.Gallery);

		Assert.All(
			plan,
			draw => Assert.Equal(Matrix4x4.Identity, draw.Model));
	}

	[Fact]
	public void CreateDrawPlan_OcclusionSceneDrawsNearBeforeFar()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.DepthOcclusion);

		Assert.Equal(4, plan.Length);

		// The near red triangle is recorded first. The farther blue
		// quad follows it, split into two ranges of one uploaded mesh,
		// and the separate reference hexagon is recorded last.
		Assert.Equal(
			new MeshDraw(MeshDrawKind.CompleteMesh, 0, 0, 3, Matrix4x4.Identity),
			plan[0]);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.ElementRange,
				1,
				0,
				3,
				Matrix4x4.Identity),
			plan[1]);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.ElementRange,
				1,
				3,
				3,
				Matrix4x4.Identity),
			plan[2]);

		Assert.Equal(
			new MeshDraw(
				MeshDrawKind.CompleteMesh,
				2,
				0,
				18,
				Matrix4x4.Identity),
			plan[3]);
	}

	[Fact]
	public void CreateDrawPlan_OcclusionSceneUsesIdentityModels()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.DepthOcclusion);

		Assert.All(
			plan,
			draw => Assert.Equal(Matrix4x4.Identity, draw.Model));
	}

	[Fact]
	public void CreateDrawPlan_OcclusionQuadRangesCoverBothTriangles()
	{
		MeshGeometry[] geometries =
			MeshGeometry.CreateDepthOcclusionMeshes();

		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(
			DemoScene.DepthOcclusion,
			geometries);

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
	public void CreateDrawPlan_CameraSceneRecordsFiveDrawsFromThreeMeshes()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.CameraTransforms);

		Assert.Equal(5, plan.Length);

		Assert.Equal(
			[0, 0, 1, 1, 2],
			plan.Select(draw => draw.MeshIndex).ToArray());

		Assert.Equal(
			[0, 1, 2],
			plan.Select(draw => draw.MeshIndex).Distinct().ToArray());

		Assert.Equal(
			[
				MeshDrawKind.CompleteMesh,
				MeshDrawKind.CompleteMesh,
				MeshDrawKind.ElementRange,
				MeshDrawKind.ElementRange,
				MeshDrawKind.CompleteMesh
			],
			plan.Select(draw => draw.Kind).ToArray());
	}

	[Fact]
	public void CreateDrawPlan_CameraReusesOneTriangleMeshWithDifferentModels()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.CameraTransforms);

		MeshDraw triangleA = plan[0];
		MeshDraw triangleB = plan[1];

		// Two draw instances of the same uploaded mesh. The mesh
		// index is shared, so no second triangle GPU buffer exists.
		Assert.Equal(0, triangleA.MeshIndex);
		Assert.Equal(0, triangleB.MeshIndex);

		Assert.Equal(0u, triangleA.FirstElement);
		Assert.Equal(0u, triangleB.FirstElement);
		Assert.Equal(3u, triangleA.ElementCount);
		Assert.Equal(3u, triangleB.ElementCount);

		// The instances differ only by their model transforms.
		Assert.Equal(CameraTransforms.TriangleAModel, triangleA.Model);
		Assert.Equal(CameraTransforms.TriangleBModel, triangleB.Model);
		Assert.NotEqual(triangleA.Model, triangleB.Model);
	}

	[Fact]
	public void CreateDrawPlan_CameraQuadRangesShareOneModelAndCoverBothTriangles()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateCameraTransformMeshes();

		MeshDraw[] plan = MeshRenderer.CreateDrawPlan(
			DemoScene.CameraTransforms,
			geometries);

		ushort[] indices = geometries[1].Indices16;

		Assert.Equal(1, plan[2].MeshIndex);
		Assert.Equal(1, plan[3].MeshIndex);

		Assert.Equal(0u, plan[2].FirstElement);
		Assert.Equal(3u, plan[2].ElementCount);
		Assert.Equal(3u, plan[3].FirstElement);
		Assert.Equal(3u, plan[3].ElementCount);

		Assert.Equal(CameraTransforms.QuadModel, plan[2].Model);
		Assert.Equal(plan[2].Model, plan[3].Model);

		Assert.Equal(
			checked((uint)indices.Length),
			plan[3].FirstElement + plan[3].ElementCount);

		Assert.Equal([0, 1, 2], indices[..3]);
		Assert.Equal([0, 2, 3], indices[3..]);
	}

	[Fact]
	public void CreateDrawPlan_CameraOrdersRedTrianglesBeforeBackground()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.CameraTransforms);

		// The near triangles are recorded before the farther quad, so
		// only the depth test can keep their overlap red.
		Assert.Equal(
			CameraTransforms.TriangleAModel,
			plan[0].Model);

		Assert.Equal(
			CameraTransforms.TriangleBModel,
			plan[1].Model);

		Assert.Equal(
			CameraTransforms.QuadModel,
			plan[2].Model);

		Assert.Equal(
			CameraTransforms.QuadModel,
			plan[3].Model);

		Assert.Equal(
			CameraTransforms.HexagonModel,
			plan[4].Model);
	}

	[Fact]
	public void CreateDrawPlan_CameraUsesNonIdentityModels()
	{
		MeshDraw[] plan = CreatePlan(DemoScene.CameraTransforms);

		Assert.All(
			plan,
			draw => Assert.NotEqual(Matrix4x4.Identity, draw.Model));
	}

	[Fact]
	public void CreateDrawPlan_RejectsUnexpectedMeshOrder()
	{
		MeshGeometry[] geometries = MeshGeometry.CreateDemoMeshes();

		(geometries[1], geometries[2]) = (geometries[2], geometries[1]);

		Assert.Throws<ArgumentException>(
			() => MeshRenderer.CreateDrawPlan(
				DemoScene.Gallery,
				geometries));
	}

	[Fact]
	public void CreateDrawPlan_RejectsUnknownScene()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => MeshRenderer.CreateDrawPlan(
				(DemoScene)int.MaxValue,
				MeshGeometry.CreateDemoMeshes()));
	}

	private static MeshDraw[] CreatePlan(DemoScene scene)
	{
		MeshGeometry[] geometries = scene switch
		{
			DemoScene.Gallery => MeshGeometry.CreateDemoMeshes(),
			DemoScene.DepthOcclusion => MeshGeometry.CreateDepthOcclusionMeshes(),
			DemoScene.CameraTransforms =>
				MeshGeometry.CreateCameraTransformMeshes(),
			_ => throw new ArgumentOutOfRangeException(nameof(scene))
		};

		return MeshRenderer.CreateDrawPlan(scene, geometries);
	}
}
