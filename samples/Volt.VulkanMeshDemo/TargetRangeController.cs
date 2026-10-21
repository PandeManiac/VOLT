using System.Numerics;
using Volt.Ecs;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Describes what one gameplay update did.
/// </summary>
/// <remarks>
/// The value is returned rather than raised as an event so the caller
/// can log, update the window title, or assert without any subscription
/// or allocation. <see cref="TargetIndex"/> is -1 when no target was
/// involved.
/// </remarks>
internal readonly record struct TargetRangeFrameResult(
	bool Restarted,
	bool Fired,
	bool Hit,
	int TargetIndex,
	float Distance,
	int Score,
	int RemainingTargets,
	bool RoundCompleted)
{
	/// <summary>
	/// Gets a result for a frame that only moved or aimed.
	/// </summary>
	/// <remarks>
	/// The value is spelled out rather than using <c>default</c> so
	/// <see cref="TargetIndex"/> reports the documented no-target value
	/// of -1 instead of the default entity index of 0. A caller may
	/// merge live score fields into the result with <c>with</c>; the
	/// action fields and the sentinel index stay exactly as declared
	/// here.
	/// </remarks>
	public static TargetRangeFrameResult None => new(
		Restarted: false,
		Fired: false,
		Hit: false,
		TargetIndex: -1,
		Distance: 0.0f,
		Score: 0,
		RemainingTargets: 0,
		RoundCompleted: false);
}

/// <summary>
/// Owns the target range's gameplay rules and its camera aiming helper.
/// </summary>
/// <remarks>
/// <para>
/// This type decides what a shot means: it builds the candidate spheres
/// from the ECS scene, casts the camera's forward ray, applies the hit
/// through the scene, and keeps the score and round state. Rendering
/// reads only the scene, so a hit is visible on the next recorded frame
/// without this type touching a GPU resource.
/// </para>
/// <para>
/// The camera is synced into its entity before every decision, so the
/// ray used for hit detection is derived from the same components the
/// renderer will use for that frame. Player input never mutates the
/// score directly; the scripted path goes through the same
/// <see cref="Update"/> method.
/// </para>
/// <para>
/// Steady-state updates allocate nothing: the candidate buffer is
/// allocated once and every other value is a struct. Console messages
/// allocate, but only on the discrete events they describe.
/// </para>
/// </remarks>
internal sealed class TargetRangeController
{
	private readonly TargetRangeScene _scene;
	private readonly TargetSphere[] _candidates =
		new TargetSphere[TargetRangeScene.TargetCount];

	/// <summary>
	/// Creates a controller for one scene.
	/// </summary>
	public TargetRangeController(TargetRangeScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;
	}

	public TargetRangeScene Scene => _scene;

	/// <summary>
	/// Gets the current score.
	/// </summary>
	public int Score { get; private set; }

	/// <summary>
	/// Gets the number of targets completed this round.
	/// </summary>
	public int CompletedTargets { get; private set; }

	/// <summary>
	/// Gets the number of targets still live.
	/// </summary>
	public int RemainingTargets =>
		TargetRangeScene.TargetCount - CompletedTargets;

	/// <summary>
	/// Gets whether every target has been completed.
	/// </summary>
	public bool IsRoundComplete { get; private set; }

	/// <summary>
	/// Gets the number of shots taken this round, hit or miss.
	/// </summary>
	public int ShotsFired { get; private set; }

	/// <summary>
	/// Applies one frame of gameplay.
	/// </summary>
	/// <remarks>
	/// The camera pose is written to the ECS camera entity first, so
	/// movement and look input from this frame are what the shot ray and
	/// the renderer both see.
	/// </remarks>
	public TargetRangeFrameResult Update(
		TargetRangeCommand command,
		CameraController camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		_scene.SyncCamera(camera);

		if (command.Restart)
		{
			return Restart(camera);
		}

		return command.Fire
			? Fire()
			: TargetRangeFrameResult.None with
			{
				Score = Score,
				RemainingTargets = RemainingTargets,
				RoundCompleted = IsRoundComplete
			};
	}

	/// <summary>
	/// Casts one shot along the camera's forward direction.
	/// </summary>
	/// <remarks>
	/// The ray origin and direction come from the camera entity's own
	/// components. Candidate spheres come from the target entities'
	/// position and scale, so the collision volume cannot drift from the
	/// drawn geometry.
	/// </remarks>
	public TargetRangeFrameResult Fire()
	{
		ShotsFired++;

		Vector3 origin = _scene.CameraPosition;
		Vector3 direction = _scene.CameraForward;

		for (int index = 0; index < _candidates.Length; index++)
		{
			_candidates[index] = _scene.GetTargetSphere(index);
		}

		TargetRaycastResult cast = TargetRangeRaycast.Cast(
			origin,
			direction,
			TargetRangeRaycast.MaximumDistance,
			_candidates);

		if (!cast.Hit)
		{
			Console.WriteLine(
				$"Target range: miss; score {Score}/" +
				$"{TargetRangeScene.MaximumScore}, " +
				$"{RemainingTargets} " +
				$"{(RemainingTargets == 1 ? "target" : "targets")} " +
				$"remaining.");

			return new TargetRangeFrameResult(
				Restarted: false,
				Fired: true,
				Hit: false,
				TargetIndex: -1,
				Distance: 0.0f,
				Score: Score,
				RemainingTargets: RemainingTargets,
				RoundCompleted: IsRoundComplete);
		}

		int targetIndex = FindTargetIndex(cast.Entity);

		if (!_scene.TryMarkTargetHit(targetIndex))
		{
			// The scene is the authority for completion: a target that
			// was already complete never scores again, even if a caller
			// somehow hands it the same entity twice.
			return new TargetRangeFrameResult(
				Restarted: false,
				Fired: true,
				Hit: false,
				TargetIndex: targetIndex,
				Distance: cast.Distance,
				Score: Score,
				RemainingTargets: RemainingTargets,
				RoundCompleted: IsRoundComplete);
		}

		Score += TargetRangeScene.PointsPerTarget;
		CompletedTargets++;

		Console.WriteLine(
			$"Target range: hit target {targetIndex + 1} at " +
			$"{cast.Distance:F2} units; score {Score}/" +
			$"{TargetRangeScene.MaximumScore}, " +
			$"{RemainingTargets} " +
			$"{(RemainingTargets == 1 ? "target" : "targets")} " +
			$"remaining.");

		if (CompletedTargets == TargetRangeScene.TargetCount)
		{
			IsRoundComplete = true;

			Console.WriteLine(
				$"Target range: round complete; score {Score}/" +
				$"{TargetRangeScene.MaximumScore}. Press R to restart.");
		}

		return new TargetRangeFrameResult(
			Restarted: false,
			Fired: true,
			Hit: true,
			TargetIndex: targetIndex,
			Distance: cast.Distance,
			Score: Score,
			RemainingTargets: RemainingTargets,
			RoundCompleted: IsRoundComplete);
	}

	/// <summary>
	/// Resets the round and returns the camera to its documented pose.
	/// </summary>
	/// <remarks>
	/// Target positions, rotation, scale, mesh handle, and completion
	/// state are all restored, so the next extraction draws the initial
	/// range. The camera pose is written into the controller and then
	/// into the camera entity, so look and movement continue from the
	/// documented pose rather than from wherever the previous round
	/// ended.
	/// </remarks>
	public TargetRangeFrameResult Restart(CameraController camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		Score = 0;
		CompletedTargets = 0;
		IsRoundComplete = false;
		ShotsFired = 0;

		camera.SetPose(
			TargetRangeScene.InitialCameraPosition,
			TargetRangeScene.InitialCameraYaw,
			TargetRangeScene.InitialCameraPitch);

		_scene.RestoreTargets();
		_scene.SyncCamera(camera);

		Console.WriteLine(
			$"Target range: round restarted; score 0/" +
			$"{TargetRangeScene.MaximumScore}, " +
			$"{TargetRangeScene.TargetCount} targets remaining.");

		return new TargetRangeFrameResult(
			Restarted: true,
			Fired: false,
			Hit: false,
			TargetIndex: -1,
			Distance: 0.0f,
			Score: Score,
			RemainingTargets: RemainingTargets,
			RoundCompleted: IsRoundComplete);
	}

	/// <summary>
	/// Points the camera at a world-space position.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the same yaw/pitch mathematics as manual mouse look: the
	/// required delta is converted into pixels at the camera's current
	/// sensitivity and applied through <see cref="CameraController.ApplyLook"/>,
	/// so the script exercises the real look path, including pitch
	/// clamping, instead of setting an orientation directly.
	/// </para>
	/// <para>
	/// The yaw is derived from the horizontal direction only, so looking
	/// at a point directly above the camera still has a well-defined
	/// heading; the pitch is then clamped by the controller.
	/// </para>
	/// </remarks>
	public static void AimAt(CameraController camera, Vector3 worldPoint)
	{
		ArgumentNullException.ThrowIfNull(camera);

		Vector3 offset = worldPoint - camera.Position;

		float horizontalDistance =
			MathF.Sqrt((offset.X * offset.X) + (offset.Z * offset.Z));

		float desiredYaw = MathF.Atan2(-offset.X, -offset.Z);

		float desiredPitch = MathF.Atan2(
			offset.Y,
			horizontalDistance);

		float sensitivity = camera.MouseSensitivity;

		camera.ApplyLook(
			(camera.Yaw - desiredYaw) / sensitivity,
			(camera.Pitch - desiredPitch) / sensitivity);
	}

	private int FindTargetIndex(Entity entity)
	{
		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			if (_scene.GetTargetEntity(index) == entity)
			{
				return index;
			}
		}

		throw new InvalidOperationException(
			$"The aiming ray hit entity {entity}, which is not a " +
			$"target of this scene.");
	}
}
