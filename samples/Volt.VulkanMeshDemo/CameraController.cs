using System.Numerics;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Describes the movement axes requested for one camera update.
/// </summary>
/// <remarks>
/// Sampling input and applying it are deliberately separate: this
/// struct carries only requested directions, so the movement
/// mathematics can be tested without SDL.
/// </remarks>
internal readonly record struct CameraMovementInput(
	bool Forward,
	bool Backward,
	bool StrafeLeft,
	bool StrafeRight,
	bool Up,
	bool Down,
	bool Sprint)
{
	/// <summary>
	/// Gets an input with no movement requested.
	/// </summary>
	public static CameraMovementInput None => default;

	/// <summary>
	/// Gets an input that moves forward only.
	/// </summary>
	public static CameraMovementInput ForwardOnly =>
		new(
			Forward: true,
			Backward: false,
			StrafeLeft: false,
			StrafeRight: false,
			Up: false,
			Down: false,
			Sprint: false);

	/// <summary>
	/// Gets whether any movement axis is requested.
	/// </summary>
	public bool HasMovement =>
		Forward ||
		Backward ||
		StrafeLeft ||
		StrafeRight ||
		Up ||
		Down;
}

/// <summary>
/// Holds the sample's movable first-person camera state.
/// </summary>
/// <remarks>
/// <para>
/// The camera uses a right-handed convention that matches the
/// sample's perspective projection: at yaw and pitch zero it looks
/// down the negative Z axis, so the default pose is the fixed pose
/// previously built by <see cref="CameraTransforms.CreateView"/>.
/// Forward includes pitch, which makes movement free-fly rather than
/// ground-plane walking; up and down move along world +Y and -Y.
/// </para>
/// <para>
/// Yaw and pitch are stored as radians. Yaw is continuous and is not
/// wrapped, so multiple turns preserve their accumulated value.
/// Pitch is clamped just short of straight up and straight down,
/// which keeps the view basis well defined for
/// <see cref="Matrix4x4.CreateLookAt"/> and avoids orientation
/// instability at the poles.
/// </para>
/// <para>
/// Every update is allocation-free. <see cref="CreateView"/> returns
/// a <see cref="Matrix4x4"/> by value, so the camera can be queried
/// once per frame without heap traffic.
/// </para>
/// </remarks>
internal sealed class CameraController
{
	/// <summary>
	/// Default movement speed in world units per second.
	/// </summary>
	public const float DefaultMoveSpeed = 2.5f;

	/// <summary>
	/// Default multiplier applied while sprinting.
	/// </summary>
	public const float DefaultSprintMultiplier = 3.0f;

	/// <summary>
	/// Default look sensitivity in radians per pixel of mouse motion.
	/// </summary>
	public const float DefaultMouseSensitivity = 0.0022f;

	/// <summary>
	/// Largest elapsed time integrated into one movement update.
	/// </summary>
	/// <remarks>
	/// Returning from a paused or unfocused window can produce an
	/// arbitrarily large elapsed time. Clamping it keeps a single
	/// frame from teleporting the camera across the scene. The value
	/// is deliberately larger than one frame at 10 FPS so ordinary
	/// slow frames still move at their true speed.
	/// </remarks>
	public const float MaxIntegrationStepSeconds = 0.1f;

	/// <summary>
	/// Largest permitted absolute pitch, in radians.
	/// </summary>
	public const float MaxPitchRadians = (MathF.PI / 2.0f) - 0.01f;

	/// <summary>
	/// Gets or sets the world-space camera position.
	/// </summary>
	public Vector3 Position { get; private set; } =
		CameraTransforms.CameraPosition;

	/// <summary>
	/// Gets the accumulated yaw in radians. Positive yaw turns left.
	/// </summary>
	public float Yaw { get; private set; }

	/// <summary>
	/// Gets the pitch in radians. Positive pitch looks up.
	/// </summary>
	public float Pitch { get; private set; }

	/// <summary>
	/// Gets or sets the movement speed in world units per second.
	/// </summary>
	public float MoveSpeed { get; set; } = DefaultMoveSpeed;

	/// <summary>
	/// Gets or sets the multiplier applied while sprinting.
	/// </summary>
	public float SprintMultiplier { get; set; } = DefaultSprintMultiplier;

	/// <summary>
	/// Gets or sets the look sensitivity in radians per pixel.
	/// </summary>
	public float MouseSensitivity { get; set; } = DefaultMouseSensitivity;

	/// <summary>
	/// Gets the yaw in degrees.
	/// </summary>
	public float YawDegrees => Yaw * (180.0f / MathF.PI);

	/// <summary>
	/// Gets the pitch in degrees.
	/// </summary>
	public float PitchDegrees => Pitch * (180.0f / MathF.PI);

	/// <summary>
	/// Gets the unit forward vector for the current orientation.
	/// </summary>
	public Vector3 Forward
	{
		get
		{
			float cosPitch = MathF.Cos(Pitch);

			return new Vector3(
				-MathF.Sin(Yaw) * cosPitch,
				MathF.Sin(Pitch),
				-MathF.Cos(Yaw) * cosPitch);
		}
	}

	/// <summary>
	/// Gets the unit right vector for the current orientation.
	/// </summary>
	/// <remarks>
	/// Right is derived from yaw only, so strafing stays horizontal
	/// when the camera looks up or down.
	/// </remarks>
	public Vector3 Right =>
		new(MathF.Cos(Yaw), 0.0f, -MathF.Sin(Yaw));

	/// <summary>
	/// Creates the camera's current view matrix.
	/// </summary>
	/// <remarks>
	/// At the initial pose this is numerically identical to
	/// <see cref="CameraTransforms.CreateView"/> for the camera scene:
	/// both call <see cref="Matrix4x4.CreateLookAt"/> with a position
	/// three units in front of the origin along +Z and world up.
	/// </remarks>
	public Matrix4x4 CreateView()
	{
		return Matrix4x4.CreateLookAt(
			Position,
			Position + Forward,
			Vector3.UnitY);
	}

	/// <summary>
	/// Applies a relative mouse motion in pixels.
	/// </summary>
	/// <remarks>
	/// SDL reports relative motion with positive X to the right and
	/// positive Y downwards, so the right turn and downward look both
	/// subtract from their angles. Non-finite deltas are ignored
	/// rather than corrupting the orientation.
	/// </remarks>
	public void ApplyLook(float deltaX, float deltaY)
	{
		if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY))
		{
			return;
		}

		Yaw -= deltaX * MouseSensitivity;

		Pitch = Math.Clamp(
			Pitch - (deltaY * MouseSensitivity),
			-MaxPitchRadians,
			MaxPitchRadians);
	}

	/// <summary>
	/// Integrates one movement update.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Simultaneous axis requests are normalized, so moving
	/// diagonally never travels faster than moving along one axis.
	/// Sprinting multiplies the speed after normalization.
	/// </para>
	/// <para>
	/// Zero, negative, and non-finite elapsed times move nothing.
	/// Larger elapsed times are clamped to
	/// <see cref="MaxIntegrationStepSeconds"/>.
	/// </para>
	/// </remarks>
	public void Move(CameraMovementInput input, float deltaSeconds)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
		{
			return;
		}

		if (!input.HasMovement)
		{
			return;
		}

		float step = MathF.Min(deltaSeconds, MaxIntegrationStepSeconds);

		Vector3 direction = Vector3.Zero;
		Vector3 forward = Forward;
		Vector3 right = Right;

		if (input.Forward)
		{
			direction += forward;
		}

		if (input.Backward)
		{
			direction -= forward;
		}

		if (input.StrafeRight)
		{
			direction += right;
		}

		if (input.StrafeLeft)
		{
			direction -= right;
		}

		if (input.Up)
		{
			direction += Vector3.UnitY;
		}

		if (input.Down)
		{
			direction -= Vector3.UnitY;
		}

		if (direction.LengthSquared() > 1.0f)
		{
			direction = Vector3.Normalize(direction);
		}

		float speed = MoveSpeed;

		if (input.Sprint)
		{
			speed *= SprintMultiplier;
		}

		Position += direction * (speed * step);
	}
}
