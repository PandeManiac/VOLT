using System.Numerics;
using Volt.Rendering.Cameras;

namespace Volt.Editor.Viewport;

/// <summary>
/// The free-flying camera the editor's scene view navigates with.
/// </summary>
/// <remarks>
/// <para>
/// Look and movement come from <see cref="CameraController"/>, the
/// engine's own first-person camera, so the editor's authoring camera
/// and a game's camera share one convention: right-handed, +Y up, -Z
/// forward, with yaw turning left about +Y and pitch looking up about
/// +X.
/// </para>
/// <para>
/// This type adds what authoring needs: a focus that keeps the current
/// orientation, middle-drag panning, wheel dollying, and a projection
/// built for the target's Y direction.
/// </para>
/// <para>
/// Every member is allocation-free.
/// </para>
/// </remarks>
internal sealed class EditorCamera
{
	/// <summary>
	/// Initial world position: above, to one side of, and in front of
	/// the scene origin.
	/// </summary>
	/// <remarks>
	/// The view opens aimed at the origin, which is where a new
	/// project's content sits. See <see cref="CreateDefaultCamera"/>.
	/// </remarks>
	public static readonly Vector3 DefaultPosition = new(6f, 5f, -10f);

	private readonly CameraController _controller = CreateDefaultCamera();

	/// <summary>
	/// Gets the camera's world position.
	/// </summary>
	public Vector3 Position => _controller.Position;

	/// <summary>
	/// Gets the unit forward direction, including pitch.
	/// </summary>
	public Vector3 Forward => _controller.Forward;

	/// <summary>
	/// Gets the horizontal unit right direction.
	/// </summary>
	public Vector3 Right => _controller.Right;

	/// <summary>
	/// Gets the unit up direction for the current orientation.
	/// </summary>
	public Vector3 Up => Vector3.Normalize(
		Vector3.Cross(_controller.Right, _controller.Forward));

	/// <summary>
	/// Gets the yaw in radians. Positive yaw turns left.
	/// </summary>
	public float Yaw => _controller.Yaw;

	/// <summary>
	/// Gets the pitch in radians. Positive pitch looks up.
	/// </summary>
	public float Pitch => _controller.Pitch;

	/// <summary>
	/// Gets or sets the vertical field of view in radians.
	/// </summary>
	public float FieldOfView { get; set; } = MathF.PI / 3f;

	/// <summary>
	/// Gets or sets the near plane.
	/// </summary>
	public float NearPlane { get; set; } = 0.05f;

	/// <summary>
	/// Gets or sets the far plane.
	/// </summary>
	public float FarPlane { get; set; } = 2000f;

	/// <summary>
	/// Gets or sets the free-fly speed in world units per second.
	/// </summary>
	public float MoveSpeed
	{
		get => _controller.MoveSpeed;
		set => _controller.MoveSpeed = value;
	}

	/// <summary>
	/// Applies relative pointer motion in pixels as a look.
	/// </summary>
	public void Look(float deltaX, float deltaY)
	{
		_controller.ApplyLook(deltaX, deltaY);
	}

	/// <summary>
	/// Integrates one movement step from held movement keys.
	/// </summary>
	/// <param name="forward">True while the forward key is held.</param>
	/// <param name="backward">True while the backward key is held.</param>
	/// <param name="strafeLeft">True while the left key is held.</param>
	/// <param name="strafeRight">True while the right key is held.</param>
	/// <param name="up">True while the up key is held.</param>
	/// <param name="down">True while the down key is held.</param>
	/// <param name="sprint">True while the sprint modifier is held.</param>
	/// <param name="deltaSeconds">The frame interval.</param>
	/// <remarks>
	/// Opposite keys cancel, and diagonal movement is normalized, so no
	/// combination travels faster than one axis. The frame interval is
	/// clamped so returning from a paused window cannot teleport the
	/// camera across the scene.
	/// </remarks>
	public void Move(
		bool forward,
		bool backward,
		bool strafeLeft,
		bool strafeRight,
		bool up,
		bool down,
		bool sprint,
		float deltaSeconds)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0f)
		{
			return;
		}

		Vector3 direction = Vector3.Zero;

		if (forward)
		{
			direction += Forward;
		}

		if (backward)
		{
			direction -= Forward;
		}

		if (strafeRight)
		{
			direction += Right;
		}

		if (strafeLeft)
		{
			direction -= Right;
		}

		if (up)
		{
			direction += Vector3.UnitY;
		}

		if (down)
		{
			direction -= Vector3.UnitY;
		}

		if (direction.LengthSquared() < 1e-6f)
		{
			return;
		}

		if (direction.LengthSquared() > 1f)
		{
			direction = Vector3.Normalize(direction);
		}

		const float SprintMultiplier = 3f;
		const float MaximumStepSeconds = 0.1f;

		float speed = sprint ? MoveSpeed * SprintMultiplier : MoveSpeed;
		float step = MathF.Min(deltaSeconds, MaximumStepSeconds);

		PanBy(direction * (speed * step));
	}

	/// <summary>
	/// Pans the camera in its own screen plane.
	/// </summary>
	/// <param name="deltaX">
	/// Horizontal pointer motion in pixels, positive to the right.
	/// </param>
	/// <param name="deltaY">
	/// Vertical pointer motion in pixels, positive downwards, which is
	/// how both SDL and the UI report motion.
	/// </param>
	/// <remarks>
	/// The camera moves opposite to the drag, so the content stays under
	/// the pointer: dragging right moves the camera left, and dragging
	/// down moves the camera up along world +Y. Both axes follow the
	/// same rule, which is what keeps a middle-drag pan feeling attached
	/// to the scene. The rate scales with <see cref="MoveSpeed"/>, so
	/// travelling faster also pans faster.
	/// </remarks>
	public void Pan(float deltaX, float deltaY)
	{
		if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY))
		{
			return;
		}

		float unitsPerPixel = MoveSpeed * 0.0025f;

		Vector3 offset =
			(Up * deltaY) -
			(Right * deltaX);

		PanBy(offset * unitsPerPixel);
	}

	/// <summary>
	/// Dollies the camera along its forward axis.
	/// </summary>
	/// <param name="wheel">Wheel ticks; positive ticks move forward.</param>
	public void Zoom(float wheel)
	{
		if (!float.IsFinite(wheel) || wheel == 0f)
		{
			return;
		}

		float step = MathF.Max(0.25f, MoveSpeed * 0.25f);
		float distance = wheel * step;

		PanBy(Forward * distance);
	}

	/// <summary>
	/// Frames a point while keeping the current orientation.
	/// </summary>
	/// <param name="point">The world-space point to look at.</param>
	/// <param name="radius">
	/// The radius of the content being framed, which sets the
	/// distance.
	/// </param>
	public void Focus(Vector3 point, float radius)
	{
		float distance = MathF.Max(1.5f, radius * 3f);

		_controller.SetPose(
			point - (Forward * distance),
			Yaw,
			Pitch);
	}

	/// <summary>
	/// Restores a documented pose.
	/// </summary>
	public void SetPose(Vector3 position, float yaw, float pitch)
	{
		_controller.SetPose(position, yaw, pitch);
	}

	/// <summary>
	/// The steepest pitch the camera takes, in radians.
	/// </summary>
	/// <remarks>
	/// Looking exactly along +Y or -Y leaves the yaw undefined, so an
	/// axis view stops a fraction of a degree short of the pole
	/// instead of flipping.
	/// </remarks>
	private const float MaximumPitch = 1.5533f;

	/// <summary>
	/// Looks at a pivot from one side of it.
	/// </summary>
	/// <param name="side">
	/// The side to stand on, as a direction: the camera is placed at
	/// the pivot plus this direction and turned to face the pivot.
	/// </param>
	/// <param name="pivot">The point to look at.</param>
	/// <remarks>
	/// The distance to the pivot is kept, so snapping to an axis view
	/// changes the angle and not the zoom, which is how the
	/// orientation compass stays predictable. A camera already sitting
	/// on the pivot has no distance to keep and falls back to a
	/// readable one.
	/// </remarks>
	public void FrameSide(Vector3 side, Vector3 pivot)
	{
		if (side.LengthSquared() < 1e-8f)
		{
			return;
		}

		side = Vector3.Normalize(side);

		float distance = Vector3.Distance(Position, pivot);

		if (!float.IsFinite(distance) || distance < 0.5f)
		{
			distance = MathF.Max(8f, MoveSpeed * 2f);
		}

		Vector3 forward = -side;

		float pitch = Math.Clamp(
			MathF.Asin(Math.Clamp(forward.Y, -1f, 1f)),
			-MaximumPitch,
			MaximumPitch);

		SetPose(
			pivot + (side * distance),
			MathF.Atan2(-forward.X, -forward.Z),
			pitch);
	}

	/// <summary>
	/// Builds the world-to-view matrix.
	/// </summary>
	public Matrix4x4 CreateView()
	{
		return _controller.CreateView();
	}

	/// <summary>
	/// Builds the view-to-clip matrix for a viewport.
	/// </summary>
	/// <param name="aspectRatio">Width divided by height.</param>
	/// <param name="yDown">
	/// True for a Vulkan render target, false for UI overlay geometry.
	/// </param>
	public Matrix4x4 CreateProjection(float aspectRatio, bool yDown)
	{
		var camera = new Volt.Ecs.Components.Rendering.CameraComponent(
			FieldOfView,
			NearPlane,
			FarPlane);

		return CameraProjection.CreatePerspective(
			camera,
			aspectRatio,
			yDown
				? ViewportYOrientation.YDown
				: ViewportYOrientation.YUp);
	}

	/// <summary>
	/// Creates the camera the editor opens with: aimed at the origin.
	/// </summary>
	/// <remarks>
	/// A camera that faced away from the origin would open on an empty
	/// viewport with every authoring tool pointing at nothing, so the
	/// default pose is the position plus a look at the scene origin.
	/// </remarks>
	private static CameraController CreateDefaultCamera()
	{
		CameraController camera = new(DefaultPosition);

		Vector3 direction = Vector3.Normalize(
			Vector3.Zero - DefaultPosition);

		camera.SetPose(
			DefaultPosition,
			MathF.Atan2(-direction.X, -direction.Z),
			MathF.Asin(direction.Y));

		return camera;
	}

	private void PanBy(Vector3 offset)
	{
		if (!float.IsFinite(offset.X) ||
			!float.IsFinite(offset.Y) ||
			!float.IsFinite(offset.Z))
		{
			return;
		}

		_controller.SetPose(Position + offset, Yaw, Pitch);
	}
}
