using System.Numerics;
using Volt.Rendering.Cameras;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeScriptTests
{
	[Fact]
	public void Script_ReachesTheCompletedRound()
	{
		using ScriptRun run = ScriptRun.Execute(
			TargetRangeScript.MinimumFrameLimit);

		Assert.True(run.Script.HasReachedFinalState);
		Assert.True(run.Controller.IsRoundComplete);
		Assert.Equal(
			TargetRangeScene.MaximumScore,
			run.Controller.Score);
		Assert.Equal(
			TargetRangeScene.TargetCount,
			run.Controller.CompletedTargets);
		Assert.Equal(0, run.Controller.RemainingTargets);
	}

	[Fact]
	public void Script_ReportsTheRequiredCheckpointsInOrder()
	{
		using ScriptRun run = ScriptRun.Execute(190);

		TargetRangeScriptCheckpoint[] expected =
		[
			TargetRangeScriptCheckpoint.InitialState,
			TargetRangeScriptCheckpoint.MissRequested,
			TargetRangeScriptCheckpoint.MissVerified,
			TargetRangeScriptCheckpoint.HitRequested,
			TargetRangeScriptCheckpoint.HitVerified,
			TargetRangeScriptCheckpoint.DuplicateVerified,
			TargetRangeScriptCheckpoint.MovementStarted,
			TargetRangeScriptCheckpoint.MovementVerified,
			TargetRangeScriptCheckpoint.RestartRequested,
			TargetRangeScriptCheckpoint.RestartVerified,
			TargetRangeScriptCheckpoint.RoundCompleted,
			TargetRangeScriptCheckpoint.FinalStateHeld
		];

		int searchFrom = 0;

		foreach (TargetRangeScriptCheckpoint checkpoint in expected)
		{
			int found = run.Checkpoints.FindIndex(
				searchFrom,
				candidate => candidate == checkpoint);

			Assert.True(
				found >= searchFrom,
				$"checkpoint {checkpoint} was not reported in order. " +
				$"Reported: {string.Join(", ", run.Checkpoints)}.");

			searchFrom = found + 1;
		}
	}

	[Fact]
	public void Script_MissPrecedesTheFirstHitAndKeepsTheScoreAtZero()
	{
		using ScriptRun run = new();

		int frames = 0;

		while (run.Script.AcceptedFrames < 30)
		{
			TargetRangeScriptCheckpoint checkpoint = run.Advance();

			frames++;

			if (checkpoint == TargetRangeScriptCheckpoint.MissVerified)
			{
				Assert.Equal(0, run.Controller.Score);
				Assert.Equal(0, run.Controller.CompletedTargets);
			}

			if (checkpoint == TargetRangeScriptCheckpoint.HitVerified)
			{
				Assert.Equal(100, run.Controller.Score);
				Assert.Equal(1, run.Controller.CompletedTargets);
			}
		}

		Assert.Equal(30, frames);
	}

	[Fact]
	public void Script_RestartVerificationLeavesAZeroScoreAndAuthoredTargets()
	{
		using ScriptRun run = new();

		TargetRangeScriptCheckpoint checkpoint =
			TargetRangeScriptCheckpoint.None;

		while (checkpoint != TargetRangeScriptCheckpoint.RestartVerified)
		{
			checkpoint = run.Advance();
		}

		Assert.Equal(0, run.Controller.Score);
		Assert.Equal(0, run.Controller.CompletedTargets);
		Assert.Equal(
			TargetRangeScene.InitialCameraPosition,
			run.Scene.CameraPosition);

		while (run.Script.AcceptedFrames < 60)
		{
			run.Advance();
		}

		Assert.True(run.Controller.Score > 0);
	}

	[Fact]
	public void Script_MovementMovesTheEcsCamera()
	{
		using ScriptRun run = new();

		Vector3 positionAtMovementStart = default;
		Vector3 positionAfterMovement = default;
		bool started = false;

		while (!run.Script.HasReachedFinalState)
		{
			TargetRangeScriptCheckpoint checkpoint = run.Advance();

			if (checkpoint == TargetRangeScriptCheckpoint.MovementStarted)
			{
				positionAtMovementStart = run.Scene.CameraPosition;
				started = true;
			}

			if (checkpoint == TargetRangeScriptCheckpoint.MovementVerified)
			{
				positionAfterMovement = run.Scene.CameraPosition;
			}
		}

		Assert.True(started);

		float moved = Vector3.Distance(
			positionAtMovementStart,
			positionAfterMovement);

		// Sixteen fixed 1/60 second steps at 2.5 units per second.
		Assert.InRange(moved, 0.6f, 0.7f);
		Assert.True(positionAfterMovement.Z < positionAtMovementStart.Z);
	}

	[Fact]
	public void Script_IsRepeatable()
	{
		using ScriptRun first = ScriptRun.Execute(200);
		using ScriptRun second = ScriptRun.Execute(200);

		Assert.Equal(first.Controller.Score, second.Controller.Score);
		Assert.Equal(
			first.Controller.CompletedTargets,
			second.Controller.CompletedTargets);
		Assert.Equal(
			first.Controller.IsRoundComplete,
			second.Controller.IsRoundComplete);

		Assert.Equal(first.Scene.CameraPosition, second.Scene.CameraPosition);
		Assert.Equal(
			first.Scene.CameraForward,
			second.Scene.CameraForward);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			Assert.Equal(
				first.Scene.GetTargetPosition(index),
				second.Scene.GetTargetPosition(index));

			Assert.Equal(
				first.Scene.GetTargetScale(index),
				second.Scene.GetTargetScale(index));
		}

		Assert.Equal(first.Checkpoints, second.Checkpoints);
		Assert.Equal(
			first.CheckpointFrames,
			second.CheckpointFrames);
	}

	[Fact]
	public void Script_KeepsTheCompletedStateStableOnceHeld()
	{
		using ScriptRun run = ScriptRun.Execute(200);

		int held = run.Script.FramesHoldingFinalState;
		Vector3 camera = run.Scene.CameraPosition;

		for (int index = 0; index < 10; index++)
		{
			run.Advance();
		}

		Assert.Equal(held + 10, run.Script.FramesHoldingFinalState);
		Assert.Equal(
			TargetRangeScene.MaximumScore,
			run.Controller.Score);

		Assert.Equal(camera, run.Scene.CameraPosition);
	}

	[Fact]
	public void Script_DoesNothingBeforeItsFirstAdvance()
	{
		using ScriptRun run = new();

		Assert.Equal(0, run.Script.AcceptedFrames);
		Assert.Equal(0, run.Controller.Score);
		Assert.False(run.Script.HasReachedFinalState);
	}

	private sealed class ScriptRun : IDisposable
	{
		private readonly TargetRangeScene _scene;
		private readonly TargetRangeController _controller;

		public ScriptRun()
		{
			_scene = new TargetRangeScene(
				EcsMeshResolver.CreateTargetRange(
					MeshGeometry.CreateTargetRangeMeshes()));

			_controller = new TargetRangeController(_scene);

			Camera = new CameraController(
				TargetRangeScene.InitialCameraPosition);
		}

		public TargetRangeScene Scene => _scene;

		public TargetRangeController Controller => _controller;

		public CameraController Camera { get; }

		public TargetRangeScript Script { get; } = new();

		public List<TargetRangeScriptCheckpoint> Checkpoints { get; } = [];

		public List<int> CheckpointFrames { get; } = [];

		public static ScriptRun Execute(int frames)
		{
			ScriptRun run = new();

			for (int index = 0; index < frames; index++)
			{
				run.Advance();
			}

			return run;
		}

		public TargetRangeScriptCheckpoint Advance()
		{
			TargetRangeScriptCheckpoint checkpoint = Script.Advance(
				_scene,
				_controller,
				Camera);

			if (checkpoint != TargetRangeScriptCheckpoint.None)
			{
				Checkpoints.Add(checkpoint);
				CheckpointFrames.Add(Script.AcceptedFrames);
			}

			return checkpoint;
		}

		public void Dispose()
		{
			_scene.Dispose();
		}
	}
}
