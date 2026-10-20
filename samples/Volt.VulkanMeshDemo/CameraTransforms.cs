using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Transforms;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Builds the sample's model, view, and projection matrices.
/// </summary>
/// <remarks>
/// <para>
/// Model transforms, view composition, and the perspective
/// projection come from <see cref="TransformMatrices"/>,
/// <see cref="CameraComponent"/>, and <see cref="CameraProjection"/>.
/// What remains sample-local is the scene selection: the legacy
/// gallery and depth-occlusion scenes author clip-space geometry and
/// keep their own projection and identity view, and the camera scene
/// keeps its documented initial pose and model transforms.
/// </para>
/// <para>
/// <see cref="System.Numerics.Matrix4x4"/> uses row-vector
/// transforms: a position is multiplied on the left of a matrix, so
/// transformations compose from left to right. The sample therefore
/// composes <c>model * view * projection</c>.
/// </para>
/// <para>
/// The sixteen floats of a <see cref="Matrix4x4"/> are pushed in
/// their natural memory order and read by GLSL as a column-major
/// <c>mat4</c>. That reinterpretation transposes the matrix
/// implicitly, so the shader multiplies <c>mvp * vec4(position, 1.0)</c>
/// and the CPU must not transpose again. This is an upload
/// convention of the sample's shader, not of the shared math.
/// </para>
/// </remarks>
internal static class CameraTransforms
{
	/// <summary>
	/// Vertical field of view for the perspective camera, in radians.
	/// </summary>
	public const float FieldOfViewRadians = MathF.PI / 3.0f;

	public const float NearPlane = 0.1f;

	/// <summary>
	/// Far clip distance for the camera scene.
	/// </summary>
	/// <remarks>
	/// The sample keeps its own far plane instead of using
	/// <see cref="CameraComponent.Default"/>, whose far plane is
	/// 1000. Changing it would change the depth distribution of the
	/// established scene.
	/// </remarks>
	public const float FarPlane = 100.0f;

	/// <summary>
	/// Gets the fixed camera position, in world space.
	/// </summary>
	public static Vector3 CameraPosition { get; } = new(0.0f, 0.0f, 3.0f);

	/// <summary>
	/// Gets the fixed camera target, the world origin.
	/// </summary>
	public static Vector3 CameraTarget { get; } = Vector3.Zero;

	/// <summary>
	/// Gets the fixed camera up vector.
	/// </summary>
	public static Vector3 CameraUp { get; } = Vector3.UnitY;

	/// <summary>
	/// Gets the camera scene's projection settings.
	/// </summary>
	/// <remarks>
	/// The far plane remains the sample's 100 units; the ECS default
	/// of 1000 is deliberately not used.
	/// </remarks>
	public static CameraComponent PerspectiveCamera { get; } =
		new(FieldOfViewRadians, NearPlane, FarPlane);

	/// <summary>
	/// Gets the model transform for the first triangle instance.
	/// </summary>
	/// <remarks>
	/// The triangle is scaled down and moved to the left of the
	/// camera view. It is drawn first.
	/// </remarks>
	public static Matrix4x4 TriangleAModel =>
		TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(-0.75f, 0.35f, 0.0f)),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(0.45f)));

	/// <summary>
	/// Gets the model transform for the second triangle instance.
	/// </summary>
	/// <remarks>
	/// This instance reuses the same uploaded triangle mesh with a
	/// different scale, a rotation around Z and Y, and a translation
	/// to the right of the view. The rotation makes its world-space
	/// depth vary across the triangle, so perspective interpolation
	/// is exercised instead of one constant depth.
	/// </remarks>
	public static Matrix4x4 TriangleBModel =>
		TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(1.05f, 0.50f, -0.35f)),
			new RotationComponent(TriangleRotation),
			new ScaleComponent(new Vector3(0.40f)));

	/// <summary>
	/// Gets the model transform for the indexed quad. The quad is
	/// behind both triangles and acts as a depth-tested background.
	/// </summary>
	public static Matrix4x4 QuadModel =>
		TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(0.0f, 0.0f, -1.1f)),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(1.35f, 0.75f, 1.0f)));

	/// <summary>
	/// Gets the model transform for the 32-bit indexed hexagon,
	/// placed below and left of the other objects.
	/// </summary>
	public static Matrix4x4 HexagonModel =>
		TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(-1.60f, -0.85f, -0.30f)),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(0.30f)));

	/// <summary>
	/// Creates the default view matrix for a scene.
	/// </summary>
	/// <remarks>
	/// The legacy gallery and depth-occlusion scenes keep their
	/// original clip-space positions, so their view is the identity.
	/// The camera-transforms scene uses a right-handed look-at matrix
	/// from <see cref="CameraPosition"/> to the origin. This is the
	/// pose a <see cref="CameraController"/> starts in before any
	/// movement or look input.
	/// </remarks>
	public static Matrix4x4 CreateView(DemoScene scene)
	{
		return scene == DemoScene.CameraTransforms
			? Matrix4x4.CreateLookAt(
				CameraPosition,
				CameraTarget,
				CameraUp)
			: Matrix4x4.Identity;
	}

	/// <summary>
	/// Creates the view matrix for a scene, using a movable camera for
	/// the perspective scene when one is supplied.
	/// </summary>
	/// <remarks>
	/// The legacy scenes remain clip-space, so they always receive the
	/// identity view. The camera is queried every frame instead of
	/// being cached, which lets movement reach the GPU without
	/// recreating any renderer resource.
	/// </remarks>
	public static Matrix4x4 CreateView(
		DemoScene scene,
		CameraController? camera)
	{
		if (scene != DemoScene.CameraTransforms)
		{
			return Matrix4x4.Identity;
		}

		return camera is null
			? CreateView(scene)
			: camera.CreateView();
	}

	/// <summary>
	/// Creates the projection matrix for a scene and aspect ratio.
	/// </summary>
	/// <remarks>
	/// The legacy scenes reproduce the previous shader exactly: a
	/// horizontal scale of <c>1 / max(aspect, 1)</c>, a vertical
	/// scale of <c>min(aspect, 1)</c>, and the Y-axis inversion that
	/// Vulkan's downward viewport requires. Authored Z passes through
	/// as clip-space depth with <c>W = 1</c>.
	///
	/// The camera scene uses <see cref="CameraProjection"/> with the
	/// sample's <see cref="PerspectiveCamera"/> settings.
	/// <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/> already
	/// maps the near plane to depth 0 and the far plane to depth 1,
	/// which matches Vulkan's 0..1 clip-space depth range. Only the
	/// viewport orientation is sample-specific: Vulkan's framebuffer
	/// Y axis points down, so the projection is requested with
	/// <see cref="ViewportYOrientation.YDown"/>.
	/// </remarks>
	public static Matrix4x4 CreateProjection(DemoScene scene, float aspect)
	{
		if (!float.IsFinite(aspect) || aspect <= 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(aspect),
				"the aspect ratio must be finite and positive.");
		}

		if (scene != DemoScene.CameraTransforms)
		{
			float horizontalScale = 1.0f / MathF.Max(aspect, 1.0f);
			float verticalScale = MathF.Min(aspect, 1.0f);

			return Matrix4x4.CreateScale(
				horizontalScale,
				-verticalScale,
				1.0f);
		}

		return CameraProjection.CreatePerspective(
			PerspectiveCamera,
			aspect,
			ViewportYOrientation.YDown);
	}

	/// <summary>
	/// Composes one draw instance's complete transform.
	/// </summary>
	/// <remarks>
	/// The result is <c>model * view * projection</c> in
	/// row-vector order, ready to be pushed without transposition.
	/// </remarks>
	public static Matrix4x4 CreateModelViewProjection(
		DemoScene scene,
		Matrix4x4 model,
		float aspect)
	{
		return TransformMatrices.CreateModelViewProjection(
			model,
			CreateView(scene),
			CreateProjection(scene, aspect));
	}

	/// <summary>
	/// Gets the combined rotation applied to the second triangle.
	/// </summary>
	/// <remarks>
	/// The scene rotates 25 degrees around Z and then 35 degrees
	/// around Y. <see cref="Matrix4x4"/> multiplication reverses the
	/// order of <see cref="Quaternion"/> multiplication, so
	/// <c>M(a * b) == M(b) * M(a)</c>. Writing the Y rotation first
	/// therefore produces the same matrix as
	/// <c>CreateRotationZ * CreateRotationY</c>. The product of two
	/// unit quaternions is normalized to satisfy the
	/// <see cref="RotationComponent"/> contract.
	/// </remarks>
	private static Quaternion TriangleRotation
	{
		get
		{
			Quaternion yRotation = Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				Radians(35.0f));

			Quaternion zRotation = Quaternion.CreateFromAxisAngle(
				Vector3.UnitZ,
				Radians(25.0f));

			return Quaternion.Normalize(yRotation * zRotation);
		}
	}

	private static float Radians(float degrees)
	{
		return degrees * (MathF.PI / 180.0f);
	}
}
