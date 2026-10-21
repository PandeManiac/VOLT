using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Transforms;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeSceneTests
{
	private static readonly Vector3 InitialPosition =
		new(0.0f, 1.7f, 8.0f);

	[Fact]
	public void Constructor_BuildsFiveTargetsOneFloorAndOneCamera()
	{
		using TargetRangeScene scene = CreateScene();

		Assert.Equal(7, scene.World.Count);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			Entity entity = scene.GetTargetEntity(index);

			Assert.True(scene.World.IsAlive(entity));
			Assert.True(scene.World.HasComponent<PositionComponent>(entity));
			Assert.True(scene.World.HasComponent<RotationComponent>(entity));
			Assert.True(scene.World.HasComponent<ScaleComponent>(entity));
			Assert.True(
				scene.World.HasComponent<MeshRendererComponent>(entity));
			Assert.True(
				scene.World.HasComponent<TargetStateComponent>(entity));
			Assert.False(scene.IsTargetCompleted(index));
		}

		Assert.True(scene.World.IsAlive(scene.FloorEntity));
		Assert.True(scene.World.IsAlive(scene.CameraEntity));
		Assert.True(
			scene.World.HasComponent<CameraComponent>(scene.CameraEntity));
		Assert.False(
			scene.World.HasComponent<ScaleComponent>(scene.CameraEntity));

		Assert.Equal(6, scene.Extract().Length);
		Assert.Equal(6, scene.CountDrawCommands());
	}

	[Fact]
	public void Extract_IncludesTargetsAndFloorButNotTheCamera()
	{
		using TargetRangeScene scene = CreateScene();

		ReadOnlySpan<RenderInstance> instances = scene.Extract();

		Assert.Equal(6, instances.Length);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			RenderInstance instance = Find(
				instances,
				scene.GetTargetEntity(index));

			Assert.Equal(TargetRangeScene.UnhitTargetMesh, instance.Mesh);
			Assert.Equal(
				TargetRangeScene.VertexColorMaterial,
				instance.Material);
		}

		Assert.Equal(
			TargetRangeScene.FloorMesh,
			Find(instances, scene.FloorEntity).Mesh);

		foreach (RenderInstance instance in instances)
		{
			Assert.NotEqual(scene.CameraEntity, instance.Entity);
		}
	}

	[Fact]
	public void TargetSpheres_MatchTheVisibleGeometry()
	{
		using TargetRangeScene scene = CreateScene();

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			TargetDefinition definition =
				TargetRangeScene.GetDefinition(index);

			TargetSphere sphere = scene.GetTargetSphere(index);

			Assert.Equal(definition.Position, sphere.Center);
			Assert.Equal(definition.Radius, sphere.Radius);
			Assert.Equal(definition.Radius, scene.GetTargetScale(index));
			Assert.False(sphere.Completed);

			// The unit-sphere mesh spans exactly one unit of radius, so
			// the visible surface and the collision sphere agree.
			Assert.InRange(sphere.Radius, 0.4f, 1.0f);
		}
	}

	[Fact]
	public void EveryTargetProjectsInsideTheInitialView()
	{
		using TargetRangeScene scene = CreateScene();

		CameraEntityMatrices.CreateMatrices(
			scene.World,
			scene.CameraEntity,
			1280.0f / 720.0f,
			ViewportYOrientation.YDown,
			out Matrix4x4 view,
			out Matrix4x4 projection);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			TargetSphere sphere = scene.GetTargetSphere(index);

			Vector4 clip = Vector4.Transform(
				new Vector4(sphere.Center, 1.0f),
				view * projection);

			Assert.True(clip.W > 0.0f);

			Assert.InRange(clip.X / clip.W, -0.95f, 0.95f);
			Assert.InRange(clip.Y / clip.W, -0.95f, 0.95f);

			// Every target subtends at least two degrees of radius from
			// the initial pose, which is roughly 24 pixels at 720p and
			// more than 15 pixels of mouse travel at the default
			// sensitivity.
			float distance = Vector3.Distance(
				InitialPosition,
				sphere.Center);

			float angularRadius = MathF.Atan(sphere.Radius / distance);

			Assert.True(
				angularRadius >= (2.0f * MathF.PI / 180.0f),
				$"target {index} subtends only " +
				$"{angularRadius * (180.0f / MathF.PI):F2} degrees.");
		}
	}

	[Fact]
	public void TryMarkTargetHit_SwapsTheMeshAndShrinksTheScale()
	{
		using TargetRangeScene scene = CreateScene();

		float authoredRadius = scene.GetTargetScale(0);

		Assert.True(scene.TryMarkTargetHit(0));

		Assert.True(scene.IsTargetCompleted(0));
		Assert.Equal(
			TargetRangeScene.HitTargetMesh,
			scene.GetTargetMesh(0));

		Assert.Equal(
			authoredRadius * TargetRangeScene.HitScaleFactor,
			scene.GetTargetScale(0),
			5);

		Assert.Equal(
			TargetRangeScene.GetDefinition(0).Position,
			scene.GetTargetPosition(0));

		// Other targets are untouched.
		Assert.False(scene.IsTargetCompleted(1));
		Assert.Equal(
			TargetRangeScene.UnhitTargetMesh,
			scene.GetTargetMesh(1));
	}

	[Fact]
	public void TryMarkTargetHit_IsIdempotent()
	{
		using TargetRangeScene scene = CreateScene();

		scene.TryMarkTargetHit(0);

		float hitScale = scene.GetTargetScale(0);

		Assert.False(scene.TryMarkTargetHit(0));
		Assert.Equal(hitScale, scene.GetTargetScale(0));
		Assert.Equal(1, scene.CountCompletedTargets());
	}

	[Fact]
	public void TryMarkTargetHit_DoesNotChangeWorldStructure()
	{
		using TargetRangeScene scene = CreateScene();

		ComponentSignature required = new(
			ComponentRegistry.Get<PositionComponent>(),
			ComponentRegistry.Get<RotationComponent>(),
			ComponentRegistry.Get<ScaleComponent>(),
			ComponentRegistry.Get<MeshRendererComponent>());

		WorldQuery query = scene.World.Query(
			new ComponentQuery(required: required));

		WorldQuery.Enumerator enumerator = query.GetEnumerator();

		while (enumerator.MoveNext())
		{
		}

		// A structural change bumps the world's structural version and
		// makes the enumerator throw on the next access. A hit and a
		// restore are component updates, so the enumerator stays valid
		// and simply reports the end of its traversal again.
		Assert.True(scene.TryMarkTargetHit(0));
		Assert.Equal(1, scene.CountCompletedTargets());
		Assert.False(enumerator.MoveNext());

		scene.RestoreTargets();
		Assert.Equal(0, scene.CountCompletedTargets());
		Assert.False(enumerator.MoveNext());
	}

	[Fact]
	public void RestoreTargets_RestoresEveryComponentValue()
	{
		using TargetRangeScene scene = CreateScene();

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			scene.TryMarkTargetHit(index);
		}

		Assert.Equal(5, scene.CountCompletedTargets());

		scene.RestoreTargets();

		Assert.Equal(0, scene.CountCompletedTargets());

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			TargetDefinition definition =
				TargetRangeScene.GetDefinition(index);

			Assert.Equal(
				TargetRangeScene.UnhitTargetMesh,
				scene.GetTargetMesh(index));

			Assert.Equal(
				definition.Position,
				scene.GetTargetPosition(index));

			Assert.Equal(
				definition.Radius,
				scene.GetTargetScale(index),
				5);
		}
	}

	[Fact]
	public void SyncCamera_WritesTheControllerPoseIntoTheEntity()
	{
		using TargetRangeScene scene = CreateScene();

		CameraController camera = new(InitialPosition);

		camera.ApplyLook(120.0f, -40.0f);
		camera.Move(CameraMovementInput.ForwardOnly, 0.5f);

		scene.SyncCamera(camera);

		Assert.Equal(camera.Position, scene.CameraPosition);

		Quaternion rotation =
			scene.World.GetComponent<RotationComponent>(
				scene.CameraEntity).Value;

		Assert.Equal(camera.CreateRotation(), rotation);
		Assert.Equal(camera.Forward, scene.CameraForward, new VectorComparer());
	}

	[Fact]
	public void CameraForward_FollowsTheEntityRotation()
	{
		using TargetRangeScene scene = CreateScene();

		CameraController camera = new(InitialPosition);

		camera.ApplyLook(-300.0f, 80.0f);
		scene.SyncCamera(camera);

		Vector3 expected = Vector3.Normalize(
			Vector3.Transform(-Vector3.UnitZ, camera.CreateRotation()));

		Assert.Equal(expected, scene.CameraForward, new VectorComparer());
	}

	[Fact]
	public void Overlay_IsTheReticleMeshAtTheClipSpaceOrigin()
	{
		using TargetRangeScene scene = CreateScene();

		Assert.Equal(TargetRangeScene.ReticleMesh, scene.OverlayMesh);

		Matrix4x4 transform = scene.CreateOverlayTransform(1280, 720);

		// The reticle mesh is symmetric around the clip-space origin, so
		// a transform with no translation keeps it centred. A viewport
		// offset here would move the reticle off the shot ray.
		Assert.Equal(Vector3.Zero, transform.Translation);
		Assert.True(transform.M11 > 0.0f);
		Assert.True(transform.M22 > 0.0f);

		// The reticle is not an entity, so extraction and the aiming
		// candidates cannot contain it.
		foreach (RenderInstance instance in scene.Extract())
		{
			Assert.NotEqual(
				TargetRangeScene.ReticleMesh,
				instance.Mesh);
		}
	}

	private static TargetRangeScene CreateScene()
	{
		return new TargetRangeScene(
			EcsMeshResolver.CreateTargetRange(
				MeshGeometry.CreateTargetRangeMeshes()));
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

	private sealed class VectorComparer : IEqualityComparer<Vector3>
	{
		public bool Equals(Vector3 left, Vector3 right)
		{
			return Vector3.Distance(left, right) < 1.0e-5f;
		}

		public int GetHashCode(Vector3 value)
		{
			return value.GetHashCode();
		}
	}
}
