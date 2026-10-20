using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class CameraScriptTests
{
	[Fact]
	public void Constants_DescribeABoundedSequence()
	{
		Assert.Equal(100, CameraScript.InitialHoldFrames);
		Assert.Equal(12, CameraScript.MovementFrames);
		Assert.Equal(10, CameraScript.FinalPoseSettleFrames);
		Assert.Equal(
			CameraScript.InitialHoldFrames +
				CameraScript.MovementFrames +
				CameraScript.FinalPoseSettleFrames,
			CameraScript.MinimumFrameLimit);
		Assert.Equal(1.0f / 60.0f, CameraScript.SimulationStepSeconds);
	}

	[Fact]
	public void Advance_HoldsTheInitialPoseForTheHoldFrames()
	{
		CameraController camera = new();
		CameraScript script = new();

		Vector3 initialPosition = camera.Position;

		for (int frame = 1; frame <= CameraScript.InitialHoldFrames; frame++)
		{
			CameraScriptCheckpoint checkpoint = script.Advance(camera);

			Assert.Equal(
				frame == 1
					? CameraScriptCheckpoint.InitialPose
					: CameraScriptCheckpoint.None,
				checkpoint);

			Assert.Equal(initialPosition, camera.Position);
			Assert.Equal(0.0f, camera.Yaw);
			Assert.Equal(0.0f, camera.Pitch);
		}

		Assert.Equal(CameraScript.InitialHoldFrames, script.AcceptedFrames);
		Assert.False(script.HasReachedFinalPose);
	}

	[Fact]
	public void Advance_StartsMovingAfterTheHoldPhase()
	{
		CameraController camera = new();
		CameraScript script = new();

		for (int frame = 0; frame < CameraScript.InitialHoldFrames; frame++)
		{
			script.Advance(camera);
		}

		CameraScriptCheckpoint checkpoint = script.Advance(camera);

		Assert.Equal(CameraScriptCheckpoint.MovementStarted, checkpoint);
		Assert.NotEqual(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
		Assert.True(camera.Yaw < 0.0f);
	}

	[Fact]
	public void Advance_ReachesTheFinalPoseAfterTheSettleFrames()
	{
		CameraController camera = new();
		CameraScript script = new();

		for (int frame = 1; frame < CameraScript.MinimumFrameLimit; frame++)
		{
			Assert.NotEqual(
				CameraScriptCheckpoint.FinalPoseReached,
				script.Advance(camera));
		}

		CameraScriptCheckpoint checkpoint = script.Advance(camera);

		Assert.Equal(CameraScriptCheckpoint.FinalPoseReached, checkpoint);
		Assert.True(script.HasReachedFinalPose);
		Assert.Equal(CameraScript.MinimumFrameLimit, script.FinalPoseFrame);
		Assert.Equal(1, script.FramesHoldingFinalPose);
	}

	[Fact]
	public void Advance_HoldsTheFinalPoseForTheRestOfTheRun()
	{
		CameraController camera = new();
		CameraScript script = new();

		for (int frame = 0; frame < CameraScript.MinimumFrameLimit; frame++)
		{
			script.Advance(camera);
		}

		Vector3 finalPosition = camera.Position;
		float finalYaw = camera.Yaw;
		float finalPitch = camera.Pitch;

		for (int frame = CameraScript.MinimumFrameLimit; frame < 600; frame++)
		{
			Assert.Equal(
				CameraScriptCheckpoint.None,
				script.Advance(camera));
		}

		Assert.Equal(finalPosition, camera.Position);
		Assert.Equal(finalYaw, camera.Yaw);
		Assert.Equal(finalPitch, camera.Pitch);
		Assert.Equal(600, script.AcceptedFrames);
		Assert.Equal(
			600 - CameraScript.MinimumFrameLimit + 1,
			script.FramesHoldingFinalPose);
	}

	[Fact]
	public void Advance_AppliesTheDocumentedYawAndTravel()
	{
		CameraController camera = new();
		CameraScript script = new();

		Vector3 previous = camera.Position;
		float pathLength = 0.0f;

		for (int frame = 0; frame < 600; frame++)
		{
			script.Advance(camera);
			pathLength += Vector3.Distance(previous, camera.Position);
			previous = camera.Position;
		}

		// The script flies forward for exactly 12 fixed 1/60 second
		// steps at the default 2.5 units per second.
		Assert.Equal(
			CameraController.DefaultMoveSpeed *
				CameraScript.MovementFrames *
				CameraScript.SimulationStepSeconds,
			pathLength,
			4);

		Assert.Equal(
			CameraController.DefaultMoveSpeed *
				CameraScript.MovementFrames *
				CameraScript.SimulationStepSeconds,
			0.5f,
			5);

		Assert.Equal(0.0f, camera.YawDegrees + 8.0f, 3);
		Assert.Equal(0.0f, camera.Pitch, 6);

		// Turning right while flying forward drifts toward +X and
		// approaches the origin. Both bounds follow from the travelled
		// arc around the starting point.
		Assert.InRange(camera.Position.X, 0.03f, 0.05f);
		Assert.InRange(camera.Position.Z, 2.49f, 2.51f);
		Assert.Equal(0.0f, camera.Position.Y, 5);

		Assert.True(
			Vector3.Distance(camera.Position, new Vector3(0.0f, 0.0f, 3.0f)) <
				pathLength,
			"The final pose must be a chord of the travelled path.");
	}

	[Fact]
	public void Advance_IsDeterministic()
	{
		CameraController firstCamera = new();
		CameraScript firstScript = new();

		CameraController secondCamera = new();
		CameraScript secondScript = new();

		for (int frame = 0; frame < 600; frame++)
		{
			Assert.Equal(
				firstScript.Advance(firstCamera),
				secondScript.Advance(secondCamera));

			Assert.Equal(firstCamera.Position, secondCamera.Position);
			Assert.Equal(firstCamera.Yaw, secondCamera.Yaw);
			Assert.Equal(firstCamera.Pitch, secondCamera.Pitch);
		}
	}

	[Fact]
	public void Advance_IsIndependentOfObservedRenderingSpeed()
	{
		CameraController fastCamera = new();
		CameraScript fastScript = new();

		CameraController slowCamera = new();
		CameraScript slowScript = new();

		for (int frame = 0; frame < 600; frame++)
		{
			fastScript.Advance(fastCamera);

			if (frame % 100 == 0)
			{
				Thread.Sleep(2);
			}

			slowScript.Advance(slowCamera);
		}

		Assert.Equal(fastCamera.Position, slowCamera.Position);
		Assert.Equal(fastCamera.Yaw, slowCamera.Yaw);
		Assert.Equal(fastCamera.Pitch, slowCamera.Pitch);
	}

	[Fact]
	public void Advance_DoesNotCountUnacceptedFrames()
	{
		CameraController camera = new();
		CameraScript script = new();

		// Attempted frames that never reached the script (for example
		// presentation retries) cannot advance it.
		for (int frame = 0; frame < CameraScript.InitialHoldFrames; frame++)
		{
			script.Advance(camera);
		}

		Assert.Equal(CameraScript.InitialHoldFrames, script.AcceptedFrames);
		Assert.Equal(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
		Assert.False(script.HasReachedFinalPose);
	}

	[Fact]
	public void Advance_RejectsNullCamera()
	{
		CameraScript script = new();

		Assert.Throws<ArgumentNullException>(() => script.Advance(null!));
	}
}
