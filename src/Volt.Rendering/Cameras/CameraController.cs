using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;

namespace Volt.Rendering.Cameras;

/// <summary>
/// Holds a movable first-person camera's state and mathematics.
/// </summary>
/// <remarks>
/// <para>
/// The camera uses VOLT's right-handed convention: at yaw and pitch
/// zero it looks down the negative Z axis with +Y up. Forward
/// includes pitch, which makes movement free-fly rather than
/// ground-plane walking; up and down move along world +Y and -Y.
/// </para>
/// <para>
/// Yaw and pitch are stored as radians. Yaw is continuous and is not
/// wrapped, so multiple turns preserve their accumulated value.
/// Pitch is clamped just short of straight up and straight down,
/// which keeps the view basis well defined and avoids orientation
/// instability at the poles. Positive yaw turns left, and positive
/// pitch looks up.
/// </para>
/// <para>
/// The controller is an application-level object: it owns no ECS
/// components, and position and rotation can be copied into
/// <see cref="PositionComponent"/> and <see cref="RotationComponent"/>
/// when an entity representation is needed. See
/// <see cref="CreateRotation"/> for that conversion.
/// </para>
/// <para>
/// The controller contains no input sampling and has no dependency
/// on SDL, a window, or a graphics backend. Every update and every
/// matrix query is allocation-free and returns value types, so it can
/// be queried once per frame without heap traffic.
/// </para>
/// </remarks>
public sealed class CameraController
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
	public const float MaxPitchRadians =
		(MathF.PI / 2.0f) - 0.01f;

	/// <summary>
	/// Initializes a camera at a caller-supplied world position,
	/// looking down -Z.
	/// </summary>
	/// <param name="position">The initial world-space position.</param>
	public CameraController(Vector3 position)
	{
		Position = position;
	}

	/// <summary>
	/// Gets the world-space camera position.
	/// </summary>
	public Vector3 Position { get; private set; }

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
	public float SprintMultiplier { get; set; } =
		DefaultSprintMultiplier;

	/// <summary>
	/// Gets or sets the look sensitivity in radians per pixel.
	/// </summary>
	public float MouseSensitivity { get; set; } =
		DefaultMouseSensitivity;

	/// <summary>
	/// Gets the yaw in degrees.
	/// </summary>
	public float YawDegrees => Yaw * (180.0f / MathF.PI);

	/// <summary>
	/// Gets the pitch in degrees.
	/// </summary>
	public float PitchDegrees => Pitch * (180.0f / MathF.PI);

	/// <summary>
	/// Restores a documented camera pose.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A round that restarts must return the camera to its initial
	/// pose, and the controller deliberately exposes no mutable pose
	/// properties. This method is the documented way to do that: it
	/// sets position and orientation together so a reset can never
	/// leave half of the old pose behind.
	/// </para>
	/// <para>
	/// Pitch is clamped to <see cref="MaxPitchRadians"/>, exactly as
	/// look input is. Yaw is stored as supplied, so callers that want
	/// a wrapped heading must wrap it themselves; the controller never
	/// wraps yaw on its own.
	/// </para>
	/// <para>
	/// Non-finite values throw instead of silently corrupting the
	/// view, because a reset is authored data rather than sampled
	/// input.
	/// </para>
	/// </remarks>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The position, yaw, or pitch is not finite.
	/// </exception>
	public void SetPose(Vector3 position, float yaw, float pitch)
	{
		if (!float.IsFinite(position.X) ||
			!float.IsFinite(position.Y) ||
			!float.IsFinite(position.Z))
		{
			throw new ArgumentOutOfRangeException(
				nameof(position),
				"The camera position must be finite.");
		}

		if (!float.IsFinite(yaw))
		{
			throw new ArgumentOutOfRangeException(
				nameof(yaw),
				"The camera yaw must be finite.");
		}

		if (!float.IsFinite(pitch))
		{
			throw new ArgumentOutOfRangeException(
				nameof(pitch),
				"The camera pitch must be finite.");
		}

		Position = position;
		Yaw = yaw;
		Pitch = Math.Clamp(pitch, -MaxPitchRadians, MaxPitchRadians);
	}

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
	/// Gets the current orientation as a normalized quaternion.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Zero yaw and pitch produce <see cref="Quaternion.Identity"/>.
	/// The result follows the same conventions as the yaw and pitch
	/// properties: positive yaw turns left about +Y, and positive
	/// pitch looks up about +X.
	/// </para>
	/// <para>
	/// <see cref="Matrix4x4"/> composes rotations from left to right,
	/// but <see cref="Quaternion"/> multiplication reverses that
	/// order: <c>M(a * b)</c> equals <c>M(b) * M(a)</c>. The yaw is
	/// therefore written as the first factor so that the resulting
	/// matrix applies pitch and then yaw in world space, which keeps
	/// the horizon level and matches <see cref="Forward"/>.
	/// </para>
	/// <para>
	/// The quaternion is normalized because
	/// <see cref="RotationComponent"/> documents that expectation.
	/// The controller keeps yaw and pitch as the single mutable
	/// source of truth and derives this value on demand.
	/// </para>
	/// </remarks>
	public Quaternion CreateRotation()
	{
		Quaternion yawRotation = Quaternion.CreateFromAxisAngle(
			Vector3.UnitY,
			Yaw);

		Quaternion pitchRotation = Quaternion.CreateFromAxisAngle(
			Vector3.UnitX,
			Pitch);

		return Quaternion.Normalize(yawRotation * pitchRotation);
	}

	/// <summary>
	/// Creates the camera's current view matrix.
	/// </summary>
	/// <remarks>
	/// The view is built from the ECS components through
	/// <see cref="CameraComponent.CreateViewMatrix"/>, so a free
	/// camera and an ECS camera with the same position and rotation
	/// produce the same view. Camera scale is not part of a view
	/// matrix.
	/// </remarks>
	public Matrix4x4 CreateView()
	{
		PositionComponent position = new(Position);
		RotationComponent rotation = new(CreateRotation());

		return CameraComponent.CreateViewMatrix(
			in position,
			in rotation);
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

		float step = MathF.Min(
			deltaSeconds,
			MaxIntegrationStepSeconds);

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
