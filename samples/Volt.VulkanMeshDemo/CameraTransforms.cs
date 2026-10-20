using System.Numerics;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Builds the sample's model, view, and projection matrices.
/// </summary>
/// <remarks>
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
/// and the CPU must not transpose again.
/// </para>
/// </remarks>
internal static class CameraTransforms
{
	/// <summary>
	/// Vertical field of view for the perspective camera, in radians.
	/// </summary>
	public const float FieldOfViewRadians = MathF.PI / 3.0f;

	public const float NearPlane = 0.1f;

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
	/// Gets the model transform for the first triangle instance.
	/// </summary>
	/// <remarks>
	/// The triangle is scaled down and moved to the left of the
	/// camera view. It is drawn first.
	/// </remarks>
	public static Matrix4x4 TriangleAModel =>
		Matrix4x4.CreateScale(0.45f) *
		Matrix4x4.CreateTranslation(-0.75f, 0.35f, 0.0f);

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
		Matrix4x4.CreateScale(0.40f) *
		Matrix4x4.CreateRotationZ(Radians(25.0f)) *
		Matrix4x4.CreateRotationY(Radians(35.0f)) *
		Matrix4x4.CreateTranslation(1.05f, 0.50f, -0.35f);

	/// <summary>
	/// Gets the model transform for the indexed quad. The quad is
	/// behind both triangles and acts as a depth-tested background.
	/// </summary>
	public static Matrix4x4 QuadModel =>
		Matrix4x4.CreateScale(1.35f, 0.75f, 1.0f) *
		Matrix4x4.CreateTranslation(0.0f, 0.0f, -1.1f);

	/// <summary>
	/// Gets the model transform for the 32-bit indexed hexagon,
	/// placed below and left of the other objects.
	/// </summary>
	public static Matrix4x4 HexagonModel =>
		Matrix4x4.CreateScale(0.30f) *
		Matrix4x4.CreateTranslation(-1.60f, -0.85f, -0.30f);

	/// <summary>
	/// Creates the view matrix for a scene.
	/// </summary>
	/// <remarks>
	/// The legacy gallery and depth-occlusion scenes keep their
	/// original clip-space positions, so their view is the identity.
	/// The camera-transforms scene uses a right-handed look-at matrix
	/// from <see cref="CameraPosition"/> to the origin.
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
	/// Creates the projection matrix for a scene and aspect ratio.
	/// </summary>
	/// <remarks>
	/// The legacy scenes reproduce the previous shader exactly: a
	/// horizontal scale of <c>1 / max(aspect, 1)</c>, a vertical
	/// scale of <c>min(aspect, 1)</c>, and the Y-axis inversion that
	/// Vulkan's downward viewport requires. Authored Z passes through
	/// as clip-space depth with <c>W = 1</c>.
	///
	/// The camera scene uses a right-handed perspective projection.
	/// <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/> already
	/// maps the near plane to depth 0 and the far plane to depth 1,
	/// which matches Vulkan's 0..1 clip-space depth range. Its Y row
	/// is negated so world-space +Y appears upwards in Vulkan's
	/// downward viewport.
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

		Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
			FieldOfViewRadians,
			aspect,
			NearPlane,
			FarPlane);

		// Vulkan's framebuffer Y axis points down. Negating the
		// projected Y coordinate keeps world-space +Y at the top of
		// the image instead of mirroring the scene vertically.
		projection.M22 = -projection.M22;

		return projection;
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
		return model *
			CreateView(scene) *
			CreateProjection(scene, aspect);
	}

	private static float Radians(float degrees)
	{
		return degrees * (MathF.PI / 180.0f);
	}
}
