using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class EcsDemoSceneTests
{
	private const int Width = 1280;
	private const int Height = 720;
	private const float Aspect = 1280.0f / 720.0f;

	private static readonly Vector3 TriangleProbeLocal =
		new(0.0f, -0.1f, 0.0f);

	[Fact]
	public void Constructor_BuildsFourMeshEntitiesAndOneCamera()
	{
		using EcsDemoScene scene = CreateScene();

		Assert.Equal(5, scene.World.Count);

		Entity[] meshEntities =
		[
			scene.TriangleAEntity,
			scene.TriangleBEntity,
			scene.QuadEntity,
			scene.HexagonEntity
		];

		foreach (Entity entity in meshEntities)
		{
			Assert.True(scene.World.IsAlive(entity));
			Assert.True(
				scene.World.HasComponent<MeshRendererComponent>(entity));
		}

		Assert.True(scene.World.IsAlive(scene.CameraEntity));
		Assert.True(
			scene.World.HasComponent<CameraComponent>(scene.CameraEntity));
		Assert.True(
			scene.World.HasComponent<PositionComponent>(scene.CameraEntity));
		Assert.True(
			scene.World.HasComponent<RotationComponent>(scene.CameraEntity));

		// Camera scale is not part of a view matrix, so the entity
		// deliberately has none.
		Assert.False(
			scene.World.HasComponent<ScaleComponent>(scene.CameraEntity));

		Assert.Equal(4, scene.Extract().Length);
	}

	[Fact]
	public void Extract_ReusesOneTriangleMeshForBothTriangles()
	{
		using EcsDemoScene scene = CreateScene();

		RenderInstance triangleA =
			Find(scene.Extract(), scene.TriangleAEntity);

		RenderInstance triangleB =
			Find(scene.Extract(), scene.TriangleBEntity);

		Assert.Equal(EcsDemoScene.TriangleMesh, triangleA.Mesh);
		Assert.Equal(EcsDemoScene.TriangleMesh, triangleB.Mesh);
		Assert.Equal(triangleA.Mesh, triangleB.Mesh);

		Assert.NotEqual(triangleA.Model, triangleB.Model);

		HashSet<MeshHandle> meshes =
			[.. scene.Extract().ToArray().Select(instance => instance.Mesh)];

		Assert.Equal(3, meshes.Count);
		Assert.Contains(EcsDemoScene.TriangleMesh, meshes);
		Assert.Contains(EcsDemoScene.QuadMesh, meshes);
		Assert.Contains(EcsDemoScene.HexagonMesh, meshes);
	}

	[Fact]
	public void Extract_UsesTheSharedTransformComponents()
	{
		using EcsDemoScene scene = CreateScene();

		ReadOnlySpan<RenderInstance> instances = scene.Extract();

		Assert.Equal(
			CameraTransforms.TriangleAModel,
			Find(instances, scene.TriangleAEntity).Model);

		Assert.Equal(
			CameraTransforms.TriangleBModel,
			Find(instances, scene.TriangleBEntity).Model);

		Assert.Equal(
			CameraTransforms.QuadModel,
			Find(instances, scene.QuadEntity).Model);

		Assert.Equal(
			CameraTransforms.HexagonModel,
			Find(instances, scene.HexagonEntity).Model);
	}

	[Fact]
	public void Extract_ProducesFiveDrawCommandsFromFourEntities()
	{
		using EcsDemoScene scene = CreateScene();

		// Two triangles, the split quad, and the hexagon.
		Assert.Equal(5, scene.CountDrawCommands());
	}

	[Fact]
	public void CameraEntity_MatchesTheFixedPerspectivePose()
	{
		using EcsDemoScene scene = CreateScene();

		CameraEntityMatrices.CreateMatrices(
			scene.World,
			scene.CameraEntity,
			Aspect,
			ViewportYOrientation.YDown,
			out Matrix4x4 view,
			out Matrix4x4 projection);

		Assert.Equal(
			CameraTransforms.CreateView(DemoScene.CameraTransforms),
			view);

		Assert.Equal(
			CameraTransforms.CreateProjection(
				DemoScene.CameraTransforms,
				Aspect),
			projection);
	}

	[Fact]
	public void CameraEntity_KeepsTheSamplesFarPlane()
	{
		using EcsDemoScene scene = CreateScene();

		CameraComponent camera =
			scene.World.GetComponent<CameraComponent>(scene.CameraEntity);

		Assert.Equal(100.0f, camera.FarPlane);
		Assert.NotEqual(CameraComponent.Default.FarPlane, camera.FarPlane);
		Assert.Equal(
			CameraTransforms.FieldOfViewRadians,
			camera.VerticalFieldOfView);
	}

	[Fact]
	public void ApplyScriptedChange_MutatesOnlyTheScriptedEntities()
	{
		using EcsDemoScene scene = CreateScene();

		RenderInstance triangleBefore =
			Find(scene.Extract(), scene.TriangleAEntity);

		RenderInstance quadBefore =
			Find(scene.Extract(), scene.QuadEntity);

		Assert.Equal(
			CameraTransforms.CameraPosition,
			scene.CameraPosition);

		scene.ApplyScriptedChange();

		Assert.Equal(
			CameraTransforms.TriangleATransform.Position.Value +
				new Vector3(EcsDemoScene.ScriptedTriangleShiftX, 0, 0),
			scene.TriangleAPosition);

		Assert.Equal(
			CameraTransforms.CameraPosition +
				new Vector3(EcsDemoScene.ScriptedCameraShiftX, 0, 0),
			scene.CameraPosition);

		RenderInstance triangleAfter =
			Find(scene.Extract(), scene.TriangleAEntity);

		RenderInstance quadAfter =
			Find(scene.Extract(), scene.QuadEntity);

		Assert.Equal(triangleBefore.Entity, triangleAfter.Entity);
		Assert.Equal(triangleBefore.Mesh, triangleAfter.Mesh);
		Assert.Equal(triangleBefore.Material, triangleAfter.Material);
		Assert.NotEqual(triangleBefore.Model, triangleAfter.Model);

		// Unrelated entities keep their own component values.
		Assert.Equal(quadBefore.Model, quadAfter.Model);
		Assert.Equal(quadBefore.Entity, quadAfter.Entity);
	}

	[Fact]
	public void ApplyScriptedChange_IsIdempotent()
	{
		using EcsDemoScene scene = CreateScene();

		scene.ApplyScriptedChange();

		Vector3 firstTriangle = scene.TriangleAPosition;
		Vector3 firstCamera = scene.CameraPosition;
		Matrix4x4 firstModel =
			Find(scene.Extract(), scene.TriangleAEntity).Model;

		scene.ApplyScriptedChange();

		Assert.Equal(firstTriangle, scene.TriangleAPosition);
		Assert.Equal(firstCamera, scene.CameraPosition);
		Assert.Equal(
			firstModel,
			Find(scene.Extract(), scene.TriangleAEntity).Model);
	}

	[Fact]
	public void ScriptedProbes_ProjectToAnalyticPixelPositions()
	{
		using EcsDemoScene scene = CreateScene();

		Vector2 initialTriangle = Project(
			scene,
			scene.TriangleAEntity,
			TriangleProbeLocal,
			Width,
			Height);

		Vector2 initialQuad = Project(
			scene,
			scene.QuadEntity,
			Vector3.Zero,
			Width,
			Height);

		scene.ApplyScriptedChange();

		Vector2 changedTriangle = Project(
			scene,
			scene.TriangleAEntity,
			TriangleProbeLocal,
			Width,
			Height);

		Vector2 changedQuad = Project(
			scene,
			scene.QuadEntity,
			Vector3.Zero,
			Width,
			Height);

		// The triangle centre lands on pixel (484, 296) before the
		// change and (608, 296) after it.
		Assert.InRange(initialTriangle.X, 483.5f, 484.5f);
		Assert.InRange(initialTriangle.Y, 296.0f, 297.0f);

		Assert.InRange(changedTriangle.X, 608.0f, 609.0f);
		Assert.InRange(changedTriangle.Y, 296.0f, 297.0f);

		// The camera moving right shifts the four-unit-distant quad
		// left by about 46 pixels.
		Assert.InRange(initialQuad.X, 639.5f, 640.5f);
		Assert.InRange(changedQuad.X, 593.5f, 595.5f);
		Assert.Equal(initialQuad.Y, changedQuad.Y, 3);

		// At 960x540 every pixel position scales by 0.75.
		Vector2 scaledTriangle = Project(
			scene,
			scene.TriangleAEntity,
			TriangleProbeLocal,
			960,
			540);

		Assert.InRange(scaledTriangle.X, 455.5f, 457.5f);
		Assert.InRange(scaledTriangle.Y, 221.5f, 222.5f);
	}

	private static EcsDemoScene CreateScene()
	{
		return new EcsDemoScene(
			EcsMeshResolver.CreateDefault(
				MeshGeometry.CreateCameraTransformMeshes()));
	}

	private static RenderInstance Find(
		ReadOnlySpan<RenderInstance> instances,
		Entity entity)
	{
		foreach (RenderInstance instance in instances)
		{
			if (instance.Entity == entity)
			{
				return instance;
			}
		}

		throw new InvalidOperationException(
			$"Entity {entity} was not extracted.");
	}

	/// <summary>
	/// Projects one local point through its entity's extracted model
	/// and the camera entity's view and projection.
	/// </summary>
	private static Vector2 Project(
		EcsDemoScene scene,
		Entity entity,
		Vector3 local,
		int width,
		int height)
	{
		CameraEntityMatrices.CreateMatrices(
			scene.World,
			scene.CameraEntity,
			width / (float)height,
			ViewportYOrientation.YDown,
			out Matrix4x4 view,
			out Matrix4x4 projection);

		RenderInstance instance = Find(scene.Extract(), entity);

		Matrix4x4 mvp = TransformMatrices.CreateModelViewProjection(
			instance.Model,
			view,
			projection);

		Vector4 clip = Vector4.Transform(
			new Vector4(local, 1.0f),
			mvp);

		Assert.True(clip.W > 0.0f);

		return new Vector2(
			((clip.X / clip.W) + 1.0f) * 0.5f * width,
			((clip.Y / clip.W) + 1.0f) * 0.5f * height);
	}
}
