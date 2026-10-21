using System.Numerics;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Identifies a scripted gameplay checkpoint emitted for one accepted
/// frame.
/// </summary>
/// <remarks>
/// Every checkpoint is produced from the accepted-presentation path, so
/// a reported state always describes components that the next recorded
/// frame will draw. The names separate what the script asked for, what
/// the gameplay state became, and which state has been held long enough
/// to be proven presented.
/// </remarks>
internal enum TargetRangeScriptCheckpoint
{
	None,

	/// <summary>
	/// The initial range was presented with the initial camera pose.
	/// </summary>
	InitialState,

	/// <summary>
	/// A deliberate miss shot was requested.
	/// </summary>
	MissRequested,

	/// <summary>
	/// The miss shot resolved without changing the score.
	/// </summary>
	MissVerified,

	/// <summary>
	/// A shot at a live target was requested.
	/// </summary>
	HitRequested,

	/// <summary>
	/// The shot hit and the score increased exactly once.
	/// </summary>
	HitVerified,

	/// <summary>
	/// A second shot at the completed target was requested.
	/// </summary>
	DuplicateRequested,

	/// <summary>
	/// The duplicate shot scored nothing.
	/// </summary>
	DuplicateVerified,

	/// <summary>
	/// Forward movement started.
	/// </summary>
	MovementStarted,

	/// <summary>
	/// Forward movement stopped and the ECS camera moved.
	/// </summary>
	MovementVerified,

	/// <summary>
	/// The round was restarted.
	/// </summary>
	RestartRequested,

	/// <summary>
	/// The restarted round reports a zero score, live targets, and the
	/// documented camera pose.
	/// </summary>
	RestartVerified,

	/// <summary>
	/// Every target is complete and the score is maximal.
	/// </summary>
	RoundCompleted,

	/// <summary>
	/// The completed state has been presented for a stable number of
	/// accepted frames.
	/// </summary>
	FinalStateHeld
}

/// <summary>
/// Drives a deterministic scripted round through the real gameplay
/// code for headless verification.
/// </summary>
/// <remarks>
/// <para>
/// The script advances once per accepted presented frame, never per
/// attempted frame, so presentation retries and swapchain recreation
/// cannot make it act twice. It supplies value-type commands and camera
/// input to the same controller a player uses; it never injects SDL
/// events, never writes a score, and never marks a target complete.
/// </para>
/// <para>
/// Each verification compares observable state before and after a real
/// action and throws when the outcome is wrong. A scripted run
/// therefore fails loudly if hit detection, scoring, completion, or
/// restart regress, instead of only logging numbers.
/// </para>
/// </remarks>
internal sealed class TargetRangeScript
{
	/// <summary>
	/// Elapsed time handed to the camera for each scripted frame.
	/// </summary>
	/// <remarks>
	/// A fixed value keeps movement distances identical across runs and
	/// frame rates. It is below the controller's maximum integration
	/// step, so it is never clamped.
	/// </remarks>
	public const float ScriptedDeltaSeconds = 1.0f / 60.0f;

	private const int IdleFrames = 8;
	private const int MovementFrames = 16;
	private const int CompletionHoldFrames = 120;

	/// <summary>
	/// Accepted frames the completed round is presented before the
	/// final checkpoint is reported.
	/// </summary>
	public const int FinalHoldFrames = CompletionHoldFrames;

	/// <summary>
	/// Smallest frame limit that reaches the final checkpoint.
	/// </summary>
	/// <remarks>
	/// The run needs the idle frames, the miss, the first hit, the
	/// duplicate shot, the movement phase, the restart, one aim and one
	/// shot per target, and the completion hold.
	/// </remarks>
	public const int MinimumFrameLimit = 190;

	private enum Step
	{
		Initial,
		Idle,
		AimMiss,
		FireMiss,
		VerifyMiss,
		AimFirst,
		FireFirst,
		VerifyFirst,
		FireDuplicate,
		VerifyDuplicate,
		MoveStart,
		MoveContinue,
		MoveStop,
		VerifyMove,
		Restart,
		VerifyRestart,
		CompleteRound,
		VerifyComplete,
		Hold,
		Done
	}

	private Step _step = Step.Initial;
	private int _stepFrames;
	private int _acceptedFrames;
	private int _targetIndex;
	private bool _aiming;
	private int _scoreBeforeAction;
	private Vector3 _cameraPositionBeforeMovement;
	private bool _finalStateReported;

	/// <summary>
	/// Gets the number of accepted frames advanced so far.
	/// </summary>
	public int AcceptedFrames => _acceptedFrames;

	/// <summary>
	/// Gets the score the round had immediately before the scripted
	/// restart, or zero before the restart.
	/// </summary>
	public int ScoreBeforeRestart { get; private set; }

	/// <summary>
	/// Gets whether the completed round has been reported as held.
	/// </summary>
	public bool HasReachedFinalState => _finalStateReported;

	/// <summary>
	/// Gets the number of accepted frames that have presented the
	/// completed round, including the frame that reported it.
	/// </summary>
	public int FramesHoldingFinalState { get; private set; }

	/// <summary>
	/// Advances the script by one accepted frame.
	/// </summary>
	/// <param name="scene">The scene the round runs in.</param>
	/// <param name="controller">The gameplay controller.</param>
	/// <param name="camera">The camera the player would drive.</param>
	/// <returns>The checkpoint this frame produced, if any.</returns>
	public TargetRangeScriptCheckpoint Advance(
		TargetRangeScene scene,
		TargetRangeController controller,
		CameraController camera)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(controller);
		ArgumentNullException.ThrowIfNull(camera);

		_acceptedFrames++;
		_stepFrames++;

		TargetRangeScriptCheckpoint checkpoint = RunStep();

		if (_step >= Step.VerifyComplete && controller.IsRoundComplete)
		{
			FramesHoldingFinalState++;

			if (!_finalStateReported &&
				FramesHoldingFinalState >= CompletionHoldFrames)
			{
				_finalStateReported = true;
				return TargetRangeScriptCheckpoint.FinalStateHeld;
			}
		}

		return checkpoint;

		TargetRangeScriptCheckpoint RunStep()
		{
			switch (_step)
			{
				case Step.Initial:
					Expect(
						controller.Score == 0 &&
						controller.RemainingTargets ==
							TargetRangeScene.TargetCount,
						"the round must start with a zero score and " +
						"every target live.");

					GoTo(Step.Idle);
					return TargetRangeScriptCheckpoint.InitialState;

				case Step.Idle:
					controller.Update(TargetRangeCommand.None, camera);

					if (_stepFrames >= IdleFrames)
					{
						GoTo(Step.AimMiss);
					}

					return TargetRangeScriptCheckpoint.None;

				case Step.AimMiss:
					// Straight up and forward: no target is anywhere
					// near this direction, so the shot must miss.
					TargetRangeController.AimAt(
						camera,
						camera.Position + new Vector3(0.0f, 10.0f, -10.0f));

					controller.Update(TargetRangeCommand.None, camera);

					_scoreBeforeAction = controller.Score;

					GoTo(Step.FireMiss);
					return TargetRangeScriptCheckpoint.MissRequested;

				case Step.FireMiss:
					{
						TargetRangeFrameResult result = controller.Update(
							TargetRangeCommand.FireOnly,
							camera);

						Expect(
							result.Fired && !result.Hit,
							"the deliberate miss must not hit a target.");
						Expect(
							controller.Score == _scoreBeforeAction,
							"a miss must not change the score.");

						GoTo(Step.VerifyMiss);
						return TargetRangeScriptCheckpoint.MissVerified;
					}

				case Step.VerifyMiss:
					{
						Expect(
							controller.Score == 0 &&
							controller.CompletedTargets == 0,
							"the miss must leave the round untouched.");

						GoTo(Step.AimFirst);
						return TargetRangeScriptCheckpoint.None;
					}

				case Step.AimFirst:
					TargetRangeController.AimAt(
						camera,
						scene.GetTargetPosition(_targetIndex));

					controller.Update(TargetRangeCommand.None, camera);

					_scoreBeforeAction = controller.Score;

					GoTo(Step.FireFirst);
					return TargetRangeScriptCheckpoint.HitRequested;

				case Step.FireFirst:
					{
						TargetRangeFrameResult result = controller.Update(
							TargetRangeCommand.FireOnly,
							camera);

						Expect(
							result.Fired && result.Hit,
							"a shot aimed at a live target must hit it.");
						Expect(
							result.TargetIndex == _targetIndex,
							"the shot must hit the target it was aimed at.");
						Expect(
							controller.Score ==
								_scoreBeforeAction +
								TargetRangeScene.PointsPerTarget,
							"a hit must add exactly one target's points.");
						Expect(
							controller.CompletedTargets == 1,
							"one hit must complete exactly one target.");

						GoTo(Step.VerifyFirst);
						return TargetRangeScriptCheckpoint.HitVerified;
					}

				case Step.VerifyFirst:
					Expect(
						scene.IsTargetCompleted(_targetIndex),
						"the hit target's component must be completed.");
					Expect(
						scene.GetTargetMesh(_targetIndex) ==
							TargetRangeScene.HitTargetMesh,
						"the hit target must reference the hit mesh.");

					GoTo(Step.FireDuplicate);
					return TargetRangeScriptCheckpoint.None;

				case Step.FireDuplicate:
					{
						// Still aimed at the completed target. The ray
						// intersects its sphere, but the scene reports it as
						// completed, so the shot must score nothing.
						_scoreBeforeAction = controller.Score;

						TargetRangeFrameResult result = controller.Update(
							TargetRangeCommand.FireOnly,
							camera);

						Expect(
							result.Fired && !result.Hit,
							"a completed target must not be hit again.");
						Expect(
							controller.Score == _scoreBeforeAction,
							"a duplicate shot must not change the score.");

						GoTo(Step.VerifyDuplicate);
						return TargetRangeScriptCheckpoint.DuplicateVerified;
					}

				case Step.VerifyDuplicate:
					Expect(
						controller.CompletedTargets == 1,
						"a duplicate shot must not complete another " +
						"target.");

					GoTo(Step.MoveStart);
					return TargetRangeScriptCheckpoint.None;

				case Step.MoveStart:
					_cameraPositionBeforeMovement =
						scene.CameraPosition;

					GoTo(Step.MoveContinue);
					return TargetRangeScriptCheckpoint.MovementStarted;

				case Step.MoveContinue:
					camera.Move(
						CameraMovementInput.ForwardOnly,
						ScriptedDeltaSeconds);

					controller.Update(TargetRangeCommand.None, camera);

					if (_stepFrames >= MovementFrames)
					{
						GoTo(Step.MoveStop);
					}

					return TargetRangeScriptCheckpoint.None;

				case Step.MoveStop:
					{
						Vector3 moved = scene.CameraPosition;

						Expect(
							Vector3.Distance(
								moved,
								_cameraPositionBeforeMovement) > 0.01f,
							"forward movement must move the ECS camera " +
							"entity.");

						GoTo(Step.VerifyMove);
						return TargetRangeScriptCheckpoint.MovementVerified;
					}

				case Step.VerifyMove:
					GoTo(Step.Restart);
					return TargetRangeScriptCheckpoint.None;

				case Step.Restart:
					{
						Expect(
							controller.Score > 0 &&
							controller.CompletedTargets > 0,
							"the restart must reset a round in progress.");

						ScoreBeforeRestart = controller.Score;

						TargetRangeFrameResult result = controller.Update(
							TargetRangeCommand.RestartOnly,
							camera);

						Expect(result.Restarted, "the restart must be applied.");
						Expect(
							controller.Score == 0 &&
							controller.CompletedTargets == 0 &&
							!controller.IsRoundComplete,
							"the restart must reset score and round state.");

						GoTo(Step.VerifyRestart);
						return TargetRangeScriptCheckpoint.RestartRequested;
					}

				case Step.VerifyRestart:
					{
						for (int index = 0;
							index < TargetRangeScene.TargetCount;
							index++)
						{
							Expect(
								!scene.IsTargetCompleted(index),
								"every target must be live after a restart.");

							Expect(
								scene.GetTargetMesh(index) ==
									TargetRangeScene.UnhitTargetMesh,
								"every target must reference the unhit mesh " +
								"after a restart.");

							TargetDefinition definition =
								TargetRangeScene.GetDefinition(index);

							Expect(
								Vector3.Distance(
									scene.GetTargetPosition(index),
									definition.Position) < 0.0001f,
								"every target must be back at its authored " +
								"position after a restart.");

							Expect(
								MathF.Abs(
									scene.GetTargetScale(index) -
									definition.Radius) < 0.0001f,
								"every target must be back at its authored " +
								"size after a restart.");
						}

						Expect(
							Vector3.Distance(
								scene.CameraPosition,
								TargetRangeScene.InitialCameraPosition) <
								0.0001f,
							"the camera must return to its documented pose " +
							"after a restart.");

						_targetIndex = 0;
						_aiming = false;

						GoTo(Step.CompleteRound);
						return TargetRangeScriptCheckpoint.RestartVerified;
					}

				case Step.CompleteRound:
					if (!_aiming)
					{
						TargetRangeController.AimAt(
							camera,
							scene.GetTargetPosition(_targetIndex));

						controller.Update(
							TargetRangeCommand.None,
							camera);

						_aiming = true;
						_stepFrames = 0;

						return TargetRangeScriptCheckpoint.None;
					}

					controller.Update(
						TargetRangeCommand.FireOnly,
						camera);

					Expect(
						controller.IsRoundComplete ||
							_targetIndex + 1 <
							TargetRangeScene.TargetCount,
						"the round must not complete before every target " +
						"is hit.");

					_targetIndex++;
					_aiming = false;
					_stepFrames = 0;

					if (_targetIndex >= TargetRangeScene.TargetCount)
					{
						GoTo(Step.VerifyComplete);
					}

					return TargetRangeScriptCheckpoint.None;

				case Step.VerifyComplete:
					Expect(
						controller.IsRoundComplete,
						"hitting every target must complete the round.");
					Expect(
						controller.Score ==
							TargetRangeScene.MaximumScore,
						"a completed round must score the maximum.");
					Expect(
						controller.RemainingTargets == 0,
						"a completed round must have no targets left.");

					GoTo(Step.Hold);

					return TargetRangeScriptCheckpoint.RoundCompleted;

				case Step.Hold:
				case Step.Done:
				default:
					return TargetRangeScriptCheckpoint.None;
			}
		}

		void GoTo(Step next)
		{
			// A local function, not the public Advance method. Every
			// transition starts the next step with a fresh frame count.
			_step = next;
			_stepFrames = 0;
		}
	}

	private static void Expect(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(
				$"Target range script: {message}");
		}
	}
}
