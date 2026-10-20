using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class EcsScriptTests
{
	private const int RunFrames = 600;

	[Fact]
	public void Constants_KeepTheDocumentedSchedule()
	{
		Assert.Equal(200, EcsScript.InitialHoldFrames);
		Assert.Equal(10, EcsScript.ChangedStateSettleFrames);
		Assert.Equal(200, EcsScript.ChangeFrame);
		Assert.Equal(210, EcsScript.ChangedStateFrame);
		Assert.Equal(210, EcsScript.MinimumFrameLimit);
	}

	[Fact]
	public void Advance_FirstFrameReportsInitialState()
	{
		using EcsDemoScene scene = CreateScene();
		EcsScript script = new();

		Assert.Equal(
			EcsScriptCheckpoint.InitialState,
			script.Advance(scene));

		Assert.Equal(1, script.AcceptedFrames);
		Assert.False(script.HasAppliedChange);
		Assert.False(script.HasReachedChangedState);
		Assert.Equal(0, script.FramesHoldingChangedState);

		Assert.Equal(
			CameraTransforms.TriangleATransform.Position.Value,
			scene.TriangleAPosition);

		Assert.Equal(
			CameraTransforms.CameraPosition,
			scene.CameraPosition);
	}

	[Fact]
	public void Advance_HoldsTheInitialSceneThroughTheChangeFrame()
	{
		using EcsDemoScene scene = CreateScene();
		EcsScript script = new();

		for (int frame = 1; frame < EcsScript.ChangeFrame; frame++)
		{
			EcsScriptCheckpoint checkpoint = script.Advance(scene);

			Assert.Equal(
				frame == 1
					? EcsScriptCheckpoint.InitialState
					: EcsScriptCheckpoint.None,
				checkpoint);

			Assert.Equal(
				CameraTransforms.TriangleATransform.Position.Value,
				scene.TriangleAPosition);

			Assert.Equal(
				CameraTransforms.CameraPosition,
				scene.CameraPosition);
		}

		Assert.False(script.HasAppliedChange);
	}

	[Fact]
	public void Advance_AppliesTheChangeAfterTheInitialHold()
	{
		using EcsDemoScene scene = CreateScene();
		EcsScript script = new();

		AdvanceFrames(script, scene, EcsScript.ChangeFrame - 1);

		Assert.Equal(
			EcsScriptCheckpoint.ChangeApplied,
			script.Advance(scene));

		Assert.True(script.HasAppliedChange);

		Assert.Equal(
			CameraTransforms.TriangleATransform.Position.Value +
				new Vector3(EcsDemoScene.ScriptedTriangleShiftX, 0, 0),
			scene.TriangleAPosition);

		Assert.Equal(
			CameraTransforms.CameraPosition +
				new Vector3(EcsDemoScene.ScriptedCameraShiftX, 0, 0),
			scene.CameraPosition);
	}

	[Fact]
	public void Advance_ReportsTheChangedStateAfterItSettles()
	{
		using EcsDemoScene scene = CreateScene();
		EcsScript script = new();

		AdvanceFrames(script, scene, EcsScript.ChangeFrame);

		for (int frame = EcsScript.ChangeFrame + 1;
			frame < EcsScript.ChangedStateFrame;
			frame++)
		{
			Assert.Equal(
				EcsScriptCheckpoint.None,
				script.Advance(scene));
		}

		Assert.Equal(
			EcsScriptCheckpoint.ChangedStateReached,
			script.Advance(scene));

		Assert.Equal(
			EcsScript.ChangedStateFrame,
			script.ChangedStateFrameNumber);

		Assert.Equal(1, script.FramesHoldingChangedState);
	}

	[Fact]
	public void Advance_HoldsTheChangedStateForTheRestOfTheRun()
	{
		using EcsDemoScene scene = CreateScene();
		EcsScript script = new();

		Vector3 changedTriangle = Vector3.Zero;
		Vector3 changedCamera = Vector3.Zero;

		for (int frame = 1; frame <= RunFrames; frame++)
		{
			EcsScriptCheckpoint checkpoint = script.Advance(scene);

			if (frame < EcsScript.ChangeFrame)
			{
				Assert.NotEqual(
					EcsScriptCheckpoint.ChangeApplied,
					checkpoint);

				continue;
			}

			if (frame == EcsScript.ChangeFrame)
			{
				Assert.Equal(
					EcsScriptCheckpoint.ChangeApplied,
					checkpoint);

				changedTriangle = scene.TriangleAPosition;
				changedCamera = scene.CameraPosition;
				continue;
			}

			Assert.Equal(
				frame == EcsScript.ChangedStateFrame
					? EcsScriptCheckpoint.ChangedStateReached
					: EcsScriptCheckpoint.None,
				checkpoint);

			Assert.Equal(changedTriangle, scene.TriangleAPosition);
			Assert.Equal(changedCamera, scene.CameraPosition);
		}

		Assert.Equal(RunFrames, script.AcceptedFrames);
		Assert.True(script.HasReachedChangedState);

		Assert.Equal(
			RunFrames - EcsScript.ChangedStateFrame + 1,
			script.FramesHoldingChangedState);
	}

	[Fact]
	public void Advance_TwoScriptsProduceIdenticalSequences()
	{
		using EcsDemoScene first = CreateScene();
		using EcsDemoScene second = CreateScene();

		EcsScript firstScript = new();
		EcsScript secondScript = new();

		for (int frame = 1; frame <= RunFrames; frame++)
		{
			Assert.Equal(
				firstScript.Advance(first),
				secondScript.Advance(second));

			Assert.Equal(first.TriangleAPosition, second.TriangleAPosition);
			Assert.Equal(first.CameraPosition, second.CameraPosition);
		}
	}

	[Fact]
	public void Advance_NullScene_Throws()
	{
		EcsScript script = new();

		Assert.Throws<ArgumentNullException>(
			() => script.Advance(null!));
	}

	private static void AdvanceFrames(
		EcsScript script,
		EcsDemoScene scene,
		int frames)
	{
		for (int frame = 0; frame < frames; frame++)
		{
			script.Advance(scene);
		}
	}

	private static EcsDemoScene CreateScene()
	{
		return new EcsDemoScene(
			EcsMeshResolver.CreateDefault(
				MeshGeometry.CreateCameraTransformMeshes()));
	}
}
