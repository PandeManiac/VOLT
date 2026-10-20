using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Identifies a scripted camera checkpoint emitted for one accepted
/// frame.
/// </summary>
internal enum CameraScriptCheckpoint
{
	/// <summary>
	/// No checkpoint occurred on this frame.
	/// </summary>
	None,

	/// <summary>
	/// The first accepted frame held the initial camera pose.
	/// </summary>
	InitialPose,

	/// <summary>
	/// The deterministic movement phase started.
	/// </summary>
	MovementStarted,

	/// <summary>
	/// The final pose has been presented for a stable number of
	/// accepted frames.
	/// </summary>
	FinalPoseReached
}

/// <summary>
/// Drives a deterministic camera flight for headless verification.
/// </summary>
/// <remarks>
/// <para>
/// The script advances exactly once per accepted presented frame, not
/// per attempted frame, so presentation retries and swapchain
/// recreation cannot move the camera extra times. Movement uses the
/// same <see cref="CameraController"/> update methods as interactive
/// input and a fixed <c>1/60</c> second simulation step, so the result
/// does not depend on the observed frame rate or wall-clock time.
/// </para>
/// <para>
/// The sequence holds the initial pose for
/// <see cref="InitialHoldFrames"/> accepted frames, flies forward
/// while yawing right for <see cref="MovementFrames"/> frames, then
/// holds the final pose indefinitely. At the default speed the
/// movement phase covers 0.5 world units and turns 8 degrees, which
/// keeps every mesh inside the 16:9 view at both the initial and the
/// final pose. The final checkpoint is only
/// emitted after the pose has been presented for
/// <see cref="FinalPoseSettleFrames"/> frames, so it cannot report a
/// pose that has not reached the resulting images.
/// </para>
/// </remarks>
internal sealed class CameraScript
{
	/// <summary>
	/// Accepted frames rendered at the initial pose before movement.
	/// </summary>
	public const int InitialHoldFrames = 100;

	/// <summary>
	/// Accepted frames that integrate scripted movement.
	/// </summary>
	public const int MovementFrames = 12;

	/// <summary>
	/// Accepted frames the final pose is presented before its
	/// checkpoint is reported.
	/// </summary>
	public const int FinalPoseSettleFrames = 10;

	/// <summary>
	/// Fixed simulation step applied once per accepted frame.
	/// </summary>
	public const float SimulationStepSeconds = 1.0f / 60.0f;

	/// <summary>
	/// Total yaw applied over the movement phase, in degrees.
	/// Negative values turn right.
	/// </summary>
	public const float TotalYawDegrees = -8.0f;

	/// <summary>
	/// Smallest frame limit that reaches the final-pose checkpoint.
	/// </summary>
	public const int MinimumFrameLimit =
		InitialHoldFrames + MovementFrames + FinalPoseSettleFrames;

	private int _acceptedFrames;
	private bool _initialPoseReported;
	private bool _finalPoseReported;

	/// <summary>
	/// Gets the number of accepted frames advanced so far.
	/// </summary>
	public int AcceptedFrames => _acceptedFrames;

	/// <summary>
	/// Gets the frame number on which the final-pose checkpoint was
	/// emitted, or zero before it is reached.
	/// </summary>
	public int FinalPoseFrame { get; private set; }

	/// <summary>
	/// Gets whether the final pose has been presented and reported.
	/// </summary>
	public bool HasReachedFinalPose => _finalPoseReported;

	/// <summary>
	/// Gets the number of accepted frames that have used the final
	/// pose, including the frame that reported it.
	/// </summary>
	public int FramesHoldingFinalPose =>
		_finalPoseReported
			? _acceptedFrames - FinalPoseFrame + 1
			: 0;

	/// <summary>
	/// Advances the script by one accepted frame.
	/// </summary>
	/// <param name="camera">The camera to drive.</param>
	/// <returns>The checkpoint this frame produced, if any.</returns>
	public CameraScriptCheckpoint Advance(CameraController camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		_acceptedFrames++;

		if (!_initialPoseReported)
		{
			_initialPoseReported = true;
			return CameraScriptCheckpoint.InitialPose;
		}

		int movementFrame = _acceptedFrames - InitialHoldFrames;

		if (movementFrame >= 1 && movementFrame <= MovementFrames)
		{
			float yawStep =
				(TotalYawDegrees * (MathF.PI / 180.0f)) /
				MovementFrames;

			// The look is expressed as mouse pixels, so the script
			// goes through the same sensitivity and clamping path
			// as an interactive mouse delta. A negative yaw step
			// turns right, which needs a positive horizontal
			// mouse delta.
			camera.ApplyLook(-yawStep / camera.MouseSensitivity, 0.0f);

			camera.Move(
				CameraMovementInput.ForwardOnly,
				SimulationStepSeconds);

			return movementFrame == 1
				? CameraScriptCheckpoint.MovementStarted
				: CameraScriptCheckpoint.None;
		}

		if (!_finalPoseReported &&
			_acceptedFrames >= MinimumFrameLimit)
		{
			_finalPoseReported = true;
			FinalPoseFrame = _acceptedFrames;

			return CameraScriptCheckpoint.FinalPoseReached;
		}

		return CameraScriptCheckpoint.None;
	}
}
