using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Extraction;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeControllerTests
{
	[Fact]
	public void NewRound_StartsAtZeroWithEveryTargetLive()
	{
		using RangeFixture range = new();

		Assert.Equal(0, range.Controller.Score);
		Assert.Equal(0, range.Controller.CompletedTargets);
		Assert.Equal(
			TargetRangeScene.TargetCount,
			range.Controller.RemainingTargets);
		Assert.False(range.Controller.IsRoundComplete);
		Assert.Equal(0, range.Controller.ShotsFired);
	}

	[Fact]
	public void Fire_FromTheInitialPose_HitsTheCentreTarget()
	{
		using RangeFixture range = new();

		TargetRangeFrameResult result = range.Controller.Fire();

		Assert.True(result.Fired);
		Assert.True(result.Hit);
		Assert.Equal(2, result.TargetIndex);
		Assert.Equal(TargetRangeScene.PointsPerTarget, result.Score);
		Assert.Equal(
			TargetRangeScene.TargetCount - 1,
			result.RemainingTargets);
	}

	[Fact]
	public void Fire_AwayFromEveryTarget_MissesAndScoresNothing()
	{
		using RangeFixture range = new();

		TargetRangeController.AimAt(
			range.Camera,
			range.Camera.Position + new Vector3(0.0f, 10.0f, -10.0f));

		range.Controller.Update(
			TargetRangeCommand.None,
			range.Camera);

		TargetRangeFrameResult result =
			range.Controller.Update(
				TargetRangeCommand.FireOnly,
				range.Camera);

		Assert.True(result.Fired);
		Assert.False(result.Hit);
		Assert.Equal(-1, result.TargetIndex);
		Assert.Equal(0, range.Controller.Score);
		Assert.Equal(0, range.Controller.CompletedTargets);
		Assert.Equal(1, range.Controller.ShotsFired);
	}

	[Fact]
	public void AimAndFire_HitsEachDistinctTargetOnce()
	{
		using RangeFixture range = new();

		int expectedScore = 0;

		for (int index = 0; index < 3; index++)
		{
			TargetRangeController.AimAt(
				range.Camera,
				range.Scene.GetTargetPosition(index));

			TargetRangeFrameResult result = range.Controller.Update(
				TargetRangeCommand.FireOnly,
				range.Camera);

			expectedScore += TargetRangeScene.PointsPerTarget;

			Assert.True(result.Hit);
			Assert.Equal(index, result.TargetIndex);
			Assert.Equal(expectedScore, range.Controller.Score);
			Assert.Equal(index + 1, range.Controller.CompletedTargets);
		}
	}

	[Fact]
	public void Fire_AtACompletedTarget_ScoresNothing()
	{
		using RangeFixture range = new();

		TargetRangeController.AimAt(
			range.Camera,
			range.Scene.GetTargetPosition(0));

		range.Controller.Update(
			TargetRangeCommand.FireOnly,
			range.Camera);

		Assert.Equal(100, range.Controller.Score);

		// Still aimed at the same target; its sphere is now completed.
		TargetRangeFrameResult duplicate = range.Controller.Update(
			TargetRangeCommand.FireOnly,
			range.Camera);

		Assert.True(duplicate.Fired);
		Assert.False(duplicate.Hit);
		Assert.Equal(100, range.Controller.Score);
		Assert.Equal(1, range.Controller.CompletedTargets);
	}

	[Fact]
	public void HittingEveryTarget_CompletesTheRoundOnce()
	{
		using RangeFixture range = new();

		TargetRangeFrameResult result = default;

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			TargetRangeController.AimAt(
				range.Camera,
				range.Scene.GetTargetPosition(index));

			result = range.Controller.Update(
				TargetRangeCommand.FireOnly,
				range.Camera);

			Assert.True(result.Hit);
		}

		Assert.True(range.Controller.IsRoundComplete);
		Assert.True(result.RoundCompleted);
		Assert.Equal(TargetRangeScene.MaximumScore, range.Controller.Score);
		Assert.Equal(0, range.Controller.RemainingTargets);
		Assert.Equal(
			TargetRangeScene.TargetCount,
			range.Scene.CountCompletedTargets());

		// A completed round cannot score again.
		TargetRangeFrameResult afterCompletion = range.Controller.Update(
			TargetRangeCommand.FireOnly,
			range.Camera);

		Assert.False(afterCompletion.Hit);
		Assert.Equal(
			TargetRangeScene.MaximumScore,
			range.Controller.Score);
		Assert.True(range.Controller.IsRoundComplete);
	}

	[Fact]
	public void Restart_ResetsScoreTargetsAndCameraPose()
	{
		using RangeFixture range = new();

		TargetRangeController.AimAt(
			range.Camera,
			range.Scene.GetTargetPosition(3));

		range.Controller.Update(
			TargetRangeCommand.FireOnly,
			range.Camera);

		range.Camera.Move(CameraMovementInput.ForwardOnly, 0.1f);

		range.Controller.Update(TargetRangeCommand.None, range.Camera);

		Assert.Equal(100, range.Controller.Score);
		Assert.NotEqual(
			TargetRangeScene.InitialCameraPosition,
			range.Scene.CameraPosition);

		TargetRangeFrameResult result = range.Controller.Update(
			TargetRangeCommand.RestartOnly,
			range.Camera);

		Assert.True(result.Restarted);
		Assert.Equal(0, range.Controller.Score);
		Assert.Equal(0, range.Controller.CompletedTargets);
		Assert.False(range.Controller.IsRoundComplete);
		Assert.Equal(0, range.Controller.ShotsFired);

		Assert.Equal(
			TargetRangeScene.InitialCameraPosition,
			range.Camera.Position);
		Assert.Equal(0.0f, range.Camera.Yaw, 5);
		Assert.Equal(0.0f, range.Camera.Pitch, 5);

		Assert.Equal(
			TargetRangeScene.InitialCameraPosition,
			range.Scene.CameraPosition);

		Assert.True(
			Vector3.Distance(
				range.Scene.CameraForward,
				-Vector3.UnitZ) < 1.0e-5f);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			Assert.False(range.Scene.IsTargetCompleted(index));
			Assert.Equal(
				TargetRangeScene.UnhitTargetMesh,
				range.Scene.GetTargetMesh(index));
		}
	}

	[Fact]
	public void PlayerMovement_MovesTheEcsCameraEntity()
	{
		using RangeFixture range = new();

		Vector3 before = range.Scene.CameraPosition;

		range.Camera.Move(CameraMovementInput.ForwardOnly, 0.1f);

		// The camera controller has moved, but the ECS entity is only
		// updated by a gameplay update.
		Assert.Equal(before, range.Scene.CameraPosition);

		range.Controller.Update(TargetRangeCommand.None, range.Camera);

		Vector3 after = range.Scene.CameraPosition;

		Assert.True(after.Z < before.Z);
		Assert.Equal(-0.25f, after.Z - before.Z, 5);
		Assert.Equal(before.Y, after.Y);
	}

	[Fact]
	public void MouseLook_ChangesTheEcsCameraRotation()
	{
		using RangeFixture range = new();

		Quaternion before = range.Scene.World
			.GetComponent<RotationComponent>(range.Scene.CameraEntity)
			.Value;

		range.Camera.ApplyLook(200.0f, -60.0f);

		range.Controller.Update(TargetRangeCommand.None, range.Camera);

		Quaternion after = range.Scene.World
			.GetComponent<RotationComponent>(range.Scene.CameraEntity)
			.Value;

		Assert.NotEqual(before, after);
		Assert.Equal(range.Camera.CreateRotation(), after);
		Assert.True(
			Vector3.Distance(
				range.Camera.Forward,
				range.Scene.CameraForward) < 1.0e-5f);
	}

	[Fact]
	public void Extraction_ReflectsTheHitVisuals()
	{
		using RangeFixture range = new();

		RenderInstance before = Find(
			range.Scene.Extract(),
			range.Scene.GetTargetEntity(1));

		TargetRangeController.AimAt(
			range.Camera,
			range.Scene.GetTargetPosition(1));

		range.Controller.Update(
			TargetRangeCommand.FireOnly,
			range.Camera);

		RenderInstance after = Find(
			range.Scene.Extract(),
			range.Scene.GetTargetEntity(1));

		Assert.Equal(before.Entity, after.Entity);
		Assert.NotEqual(before.Mesh, after.Mesh);
		Assert.Equal(TargetRangeScene.HitTargetMesh, after.Mesh);
		Assert.NotEqual(before.Model, after.Model);

		// The completed target is drawn at 55% of its authored size.
		float beforeRadius = MathF.Sqrt(
			before.Model.M11 * before.Model.M11 +
			before.Model.M12 * before.Model.M12 +
			before.Model.M13 * before.Model.M13);

		float afterRadius = MathF.Sqrt(
			after.Model.M11 * after.Model.M11 +
			after.Model.M12 * after.Model.M12 +
			after.Model.M13 * after.Model.M13);

		Assert.Equal(
			beforeRadius * TargetRangeScene.HitScaleFactor,
			afterRadius,
			5);
	}

	[Fact]
	public void AimAt_PointsTheCameraAtEveryTarget()
	{
		using RangeFixture range = new();

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			Vector3 target = range.Scene.GetTargetPosition(index);

			TargetRangeController.AimAt(range.Camera, target);

			Vector3 expected = Vector3.Normalize(
				target - range.Camera.Position);

			Assert.True(
				Vector3.Distance(expected, range.Camera.Forward) < 1.0e-5f,
				$"aim at target {index} was off by " +
				$"{Vector3.Distance(expected, range.Camera.Forward)}.");
		}
	}

	[Fact]
	public void AimAt_ClampsPitchForAPointDirectlyAbove()
	{
		using RangeFixture range = new();

		TargetRangeController.AimAt(
			range.Camera,
			range.Camera.Position + new Vector3(0.0f, 10.0f, 0.0f));

		Assert.Equal(
			CameraController.MaxPitchRadians,
			range.Camera.Pitch,
			5);

		Assert.True(float.IsFinite(range.Camera.Yaw));
	}

	[Fact]
	public void Update_WithoutActions_ReportsTheNoTargetSentinel()
	{
		using RangeFixture range = new();

		TargetRangeFrameResult result = range.Controller.Update(
			TargetRangeCommand.None,
			range.Camera);

		Assert.False(result.Fired);
		Assert.False(result.Hit);
		Assert.False(result.Restarted);
		Assert.False(result.RoundCompleted);
		Assert.Equal(-1, result.TargetIndex);
		Assert.Equal(0, result.Score);
		Assert.Equal(
			TargetRangeScene.TargetCount,
			result.RemainingTargets);
		Assert.Equal(0.0f, result.Distance);

		// The sentinel itself carries the documented index as well, so a
		// caller that merges live fields with `with` keeps it.
		Assert.Equal(-1, TargetRangeFrameResult.None.TargetIndex);
	}

	[Fact]
	public void Update_WithoutActions_DoesNotAllocate()
	{
		using RangeFixture range = new();

		for (int index = 0; index < 100; index++)
		{
			range.Controller.Update(
				TargetRangeCommand.None,
				range.Camera);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1000; index++)
		{
			range.Controller.Update(
				TargetRangeCommand.None,
				range.Camera);
		}

		long allocated =
			GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
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

	private sealed class RangeFixture : IDisposable
	{
		public RangeFixture()
		{
			Scene = new TargetRangeScene(
				EcsMeshResolver.CreateTargetRange(
					MeshGeometry.CreateTargetRangeMeshes()));

			Controller = new TargetRangeController(Scene);
			Camera = new CameraController(
				TargetRangeScene.InitialCameraPosition);
		}

		public TargetRangeScene Scene { get; }

		public TargetRangeController Controller { get; }

		public CameraController Camera { get; }

		public void Dispose()
		{
			Scene.Dispose();
		}
	}
}
