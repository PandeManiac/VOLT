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

	[Fact]
	public void CreateCameraTransformMeshes_CoversEveryDrawPath()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

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
		Assert.Contains("green", meshes[2].Name);
	}

	[Fact]
	public void CreateCameraTransformMeshes_StoresLocalSpaceGeometry()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		// Local-space geometry is centred near the origin and is not
		// pre-transformed on the CPU. Placement and scale come from
		// the draw instances' model matrices.
		foreach (MeshGeometry mesh in meshes)
		{
			Assert.All(
				mesh.Vertices,
				vertex => Assert.Equal(0.0f, vertex.Position.Z));

			Assert.All(
				mesh.Vertices,
				vertex => Assert.InRange(vertex.Position.X, -1.0f, 1.0f));

			Assert.All(
				mesh.Vertices,
				vertex => Assert.InRange(vertex.Position.Y, -1.0f, 1.0f));
		}

		Assert.Equal(-0.60f, meshes[0].Vertices[0].Position.X);
		Assert.Equal(0.60f, meshes[0].Vertices[1].Position.X);
		Assert.Equal(0.0f, meshes[0].Vertices[2].Position.X);

		Assert.Equal(-1.0f, meshes[1].Vertices[0].Position.X);
		Assert.Equal(1.0f, meshes[1].Vertices[1].Position.X);
	}

	[Fact]
	public void CreateCameraTransformMeshes_UsesUniformInstanceColors()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		foreach (MeshGeometry mesh in meshes)
		{
			Vector3 color = mesh.Vertices[0].Color;

			Assert.All(
				mesh.Vertices,
				vertex => Assert.Equal(color, vertex.Color));
		}

		Assert.True(meshes[0].Vertices[0].Color.X > 0.9f);
		Assert.True(meshes[1].Vertices[0].Color.Z > 0.9f);
		Assert.True(meshes[2].Vertices[0].Color.Y > 0.8f);
	}

	[Fact]
	public void CreateCameraTransformMeshes_EveryIndexReferencesAnExistingVertex()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

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
	public void CreateCameraTransformMeshes_DocumentedProbesLieInsideExpectedGeometry()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		Vector2[] triangleA = ProjectCamera(
			CameraTransforms.TriangleAModel,
			meshes[0].Vertices);

		Vector2[] triangleB = ProjectCamera(
			CameraTransforms.TriangleBModel,
			meshes[0].Vertices);

		Vector2[] quad = ProjectCamera(
			CameraTransforms.QuadModel,
			meshes[1].Vertices);

		// The red probe is the projection of a point well inside
		// triangle A. It must also fall inside the farther quad so
		// the depth test decides which colour appears there.
		Vector2 overlapProbe = ProjectCamera(
			CameraTransforms.TriangleAModel,
			new Vector3(0.0f, -0.1f, 0.0f));

		Assert.InRange(overlapProbe.X, 483.0f, 485.0f);
		Assert.InRange(overlapProbe.Y, 295.5f, 297.5f);

		Assert.True(IsInsideTriangle(
			overlapProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]));

		Assert.True(IsInsideQuad(overlapProbe, quad));

		Assert.True(MinTriangleEdgeDistance(
			overlapProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]) > 25.0f);

		Assert.True(MinQuadEdgeDistance(overlapProbe, quad) > 25.0f);

		// The blue probe is inside the quad only, away from every
		// triangle silhouette and from the quad's edges.
		Vector2 quadOnlyProbe = new(700.0f, 430.0f);

		Assert.True(IsInsideQuad(quadOnlyProbe, quad));
		Assert.True(MinQuadEdgeDistance(quadOnlyProbe, quad) > 25.0f);

		Assert.False(IsInsideTriangle(
			quadOnlyProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]));

		Assert.False(IsInsideTriangle(
			quadOnlyProbe,
			triangleB[0],
			triangleB[1],
			triangleB[2]));

		Assert.True(MinTriangleEdgeDistance(
			quadOnlyProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]) > 25.0f);

		Assert.True(MinTriangleEdgeDistance(
			quadOnlyProbe,
			triangleB[0],
			triangleB[1],
			triangleB[2]) > 25.0f);
	}

	[Fact]
	public void CreateCameraTransformMeshes_AllVerticesStayInsideTheFrustum()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		(Matrix4x4 Model, MeshGeometry Mesh)[] draws =
		[
			(CameraTransforms.TriangleAModel, meshes[0]),
			(CameraTransforms.TriangleBModel, meshes[0]),
			(CameraTransforms.QuadModel, meshes[1]),
			(CameraTransforms.HexagonModel, meshes[2])
		];

		foreach ((Matrix4x4 model, MeshGeometry mesh) in draws)
		{
			foreach (MeshVertex vertex in mesh.Vertices)
			{
				Vector4 clip = ProjectCameraClip(model, vertex.Position);

				Assert.True(
					clip.W > 0.0f,
					$"{mesh.Name} has a non-positive clip W.");

				Assert.InRange(clip.X / clip.W, -0.9f, 0.9f);
				Assert.InRange(clip.Y / clip.W, -0.9f, 0.9f);
				Assert.InRange(clip.Z / clip.W, 0.0f, 1.0f);
			}
		}
	}

	[Fact]
	public void CreateCameraTransformMeshes_ScriptedFinalPoseKeepsEveryMeshVisible()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		CameraController camera = ScriptedCamera();

		(Matrix4x4 Model, MeshGeometry Mesh)[] draws =
		[
			(CameraTransforms.TriangleAModel, meshes[0]),
			(CameraTransforms.TriangleBModel, meshes[0]),
			(CameraTransforms.QuadModel, meshes[1]),
			(CameraTransforms.HexagonModel, meshes[2])
		];

		foreach ((Matrix4x4 model, MeshGeometry mesh) in draws)
		{
			foreach (MeshVertex vertex in mesh.Vertices)
			{
				Vector4 clip = ProjectCameraClip(
					camera,
					model,
					vertex.Position,
					ProbeWidth,
					ProbeHeight);

				Assert.True(
					clip.W > 0.0f,
					$"{mesh.Name} has a non-positive clip W at the " +
					$"scripted final pose.");

				Assert.InRange(clip.X / clip.W, -0.95f, 0.95f);
				Assert.InRange(clip.Y / clip.W, -0.95f, 0.95f);
				Assert.InRange(clip.Z / clip.W, 0.0f, 1.0f);
			}
		}
	}

	[Fact]
	public void CreateCameraTransformMeshes_ScriptedFinalPoseProbesAreInterior()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		CameraController camera = ScriptedCamera();

		Vector2[] triangleA =
			[.. meshes[0].Vertices.Select(v => ProjectCamera(
				camera,
				CameraTransforms.TriangleAModel,
				v.Position))];

		Vector2[] triangleB =
			[.. meshes[0].Vertices.Select(v => ProjectCamera(
				camera,
				CameraTransforms.TriangleBModel,
				v.Position))];

		Vector2[] quad =
			[.. meshes[1].Vertices.Select(v => ProjectCamera(
				camera,
				CameraTransforms.QuadModel,
				v.Position))];

		Vector2[] hexagon =
			[.. meshes[2].Vertices.Select(v => ProjectCamera(
				camera,
				CameraTransforms.HexagonModel,
				v.Position))];

		Vector2 overlapProbe = ProjectCamera(
			camera,
			CameraTransforms.TriangleAModel,
			new Vector3(0.0f, -0.1f, 0.0f));

		// The projected point is derived from the geometry, not chosen
		// from the screenshot. The harness asserts this exact pixel.
		Assert.InRange(overlapProbe.X, 342.0f, 344.0f);
		Assert.InRange(overlapProbe.Y, 279.0f, 281.0f);

		Assert.True(IsInsideTriangle(
			overlapProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]));

		Assert.True(IsInsideQuad(overlapProbe, quad));

		Assert.True(MinTriangleEdgeDistance(
			overlapProbe,
			triangleA[0],
			triangleA[1],
			triangleA[2]) > 25.0f);

		Assert.True(MinQuadEdgeDistance(overlapProbe, quad) > 25.0f);

		// The quad-only probe is the projection of the quad's local
		// origin at the same pose, so it is derived rather than chosen
		// from a screenshot. The quad centre stays far away from every
		// silhouette, so it cannot land on a rasterized edge.
		Vector2 quadOnlyProbe = ProjectCamera(
			camera,
			CameraTransforms.QuadModel,
			Vector3.Zero);

		Assert.InRange(quadOnlyProbe.X, 545.0f, 547.0f);
		Assert.InRange(quadOnlyProbe.Y, 359.0f, 361.0f);

		Assert.True(IsInsideQuad(quadOnlyProbe, quad));
		Assert.True(MinQuadEdgeDistance(quadOnlyProbe, quad) > 25.0f);

		foreach (Vector2[] silhouette in new[]
		{
			triangleA,
			triangleB,
			hexagon
		})
		{
			Assert.False(IsInsideTriangle(
				quadOnlyProbe,
				silhouette[0],
				silhouette[1],
				silhouette[2]));

			Assert.True(MinTriangleEdgeDistance(
				quadOnlyProbe,
				silhouette[0],
				silhouette[1],
				silhouette[2]) > 25.0f);
		}
	}

	[Fact]
	public void CreateCameraTransformMeshes_ScriptedFinalPoseProbesScaleWithViewport()
	{
		MeshGeometry[] meshes = MeshGeometry.CreateCameraTransformMeshes();

		CameraController camera = ScriptedCamera();

		// Both resize extents keep the 16:9 aspect, so the normalized
		// position is unchanged and the pixel position scales by 0.75.
		Vector2 overlapProbe = ProjectCamera(
			camera,
			CameraTransforms.TriangleAModel,
			new Vector3(0.0f, -0.1f, 0.0f),
			960,
			540);

		Vector2 quadOnlyProbe = ProjectCamera(
			camera,
			CameraTransforms.QuadModel,
			Vector3.Zero,
			960,
			540);

		Assert.InRange(overlapProbe.X, 256.0f, 258.0f);
		Assert.InRange(overlapProbe.Y, 209.0f, 211.0f);

		// The quad-only probe scales the 1280x720 probe (544,360) by
		// 0.75. It is derived from the same local point at the same
		// camera pose, not copied from a previous screenshot.
		Vector2 quadLocalProbe = ProjectCamera(
			camera,
			CameraTransforms.QuadModel,
			Vector3.Zero);

		Assert.Equal(quadLocalProbe.X * 0.75f, quadOnlyProbe.X, 3);
		Assert.Equal(quadLocalProbe.Y * 0.75f, quadOnlyProbe.Y, 3);

		Assert.InRange(quadOnlyProbe.X, 408.0f, 411.0f);
		Assert.InRange(quadOnlyProbe.Y, 269.0f, 272.0f);

		Assert.Equal(3, meshes.Length);
	}

	/// <summary>
	/// Advances a fresh camera through the complete deterministic
	/// script so tests analyse the pose the sample actually presents.
	/// </summary>
	private static CameraController ScriptedCamera()
	{
		CameraController camera = new();
		CameraScript script = new();

		while (!script.HasReachedFinalPose)
		{
			script.Advance(camera);
		}

		return camera;
	}

	private static Vector2 ProjectCamera(
		CameraController camera,
		Matrix4x4 model,
		Vector3 local)
	{
		return ProjectCamera(camera, model, local, ProbeWidth, ProbeHeight);
	}

	private static Vector2 ProjectCamera(
		CameraController camera,
		Matrix4x4 model,
		Vector3 local,
		int width,
		int height)
	{
		Vector4 clip = ProjectCameraClip(
			camera,
			model,
			local,
			width,
			height);

		return new Vector2(
			((clip.X / clip.W) + 1.0f) * 0.5f * width,
			((clip.Y / clip.W) + 1.0f) * 0.5f * height);
	}

	private static Vector4 ProjectCameraClip(
		CameraController camera,
		Matrix4x4 model,
		Vector3 local,
		int width,
		int height)
	{
		Matrix4x4 mvp =
			model *
			camera.CreateView() *
			CameraTransforms.CreateProjection(
				DemoScene.CameraTransforms,
				width / (float)height);

		Vector4 clip = Vector4.Transform(
			new Vector4(local, 1.0f),
			mvp);

		Assert.True(clip.W > 0.0f);

		return clip;
	}

	/// <summary>
	/// Projects one local-space vertex through its draw instance's
	/// model matrix and the fixed perspective camera.
	/// </summary>
	private static Vector2 ProjectCamera(Matrix4x4 model, Vector3 local)
	{
		Vector4 clip = ProjectCameraClip(model, local);

		return new Vector2(
			((clip.X / clip.W) + 1.0f) * 0.5f * ProbeWidth,
			((clip.Y / clip.W) + 1.0f) * 0.5f * ProbeHeight);
	}

	private static Vector4 ProjectCameraClip(
		Matrix4x4 model,
		Vector3 local)
	{
		Matrix4x4 mvp = CameraTransforms.CreateModelViewProjection(
			DemoScene.CameraTransforms,
			model,
			ProbeWidth / (float)ProbeHeight);

		Vector4 clip = Vector4.Transform(
			new Vector4(local, 1.0f),
			mvp);

		Assert.True(clip.W > 0.0f);

		return clip;
	}

	private static Vector2[] ProjectCamera(
		Matrix4x4 model,
		MeshVertex[] vertices)
	{
		return
		[
			.. vertices.Select(vertex => ProjectCamera(model, vertex.Position))
		];
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
