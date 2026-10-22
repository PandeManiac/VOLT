
using System.Numerics;
using Volt.Ecs.Components.Spatial;

namespace Volt.Ecs.Components.Rendering;

/// <summary>
/// Represents a perspective camera's projection settings.
/// </summary>
/// <remarks>
/// VOLT uses right-handed coordinates with +Y as up and -Z
/// as forward. Vertical field of view is expressed in radians.
///
/// The camera's position and rotation are stored separately.
/// The aspect ratio is provided by the active viewport.
/// </remarks>
public struct CameraComponent(
	float verticalFieldOfView,
	float nearPlane,
	float farPlane)
{
	public float VerticalFieldOfView = verticalFieldOfView;
	public float NearPlane = nearPlane;
	public float FarPlane = farPlane;

	/// <summary>
	/// Gets a perspective camera with a 60-degree vertical field
	/// of view and a clipping range of 0.1 to 1000 units.
	/// </summary>
	public static CameraComponent Default => new(
		MathF.PI / 3f,
		0.1f,
		1000f);

	/// <summary>
	/// Creates the perspective projection matrix for a viewport.
	/// </summary>
	public readonly Matrix4x4 CreateProjectionMatrix(float aspectRatio)
	{
		if (!float.IsFinite(aspectRatio) || aspectRatio <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(aspectRatio));
		}

		if (!float.IsFinite(VerticalFieldOfView)
			|| VerticalFieldOfView <= 0f
			|| VerticalFieldOfView >= MathF.PI)
		{
			throw new ArgumentOutOfRangeException(
				nameof(VerticalFieldOfView));
		}

		if (!float.IsFinite(NearPlane) || NearPlane <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(NearPlane));
		}

		if (!float.IsFinite(FarPlane) || FarPlane <= NearPlane)
		{
			throw new ArgumentOutOfRangeException(nameof(FarPlane));
		}

		return Matrix4x4.CreatePerspectiveFieldOfView(
			VerticalFieldOfView,
			aspectRatio,
			NearPlane,
			FarPlane);
	}

	/// <summary>
	/// Creates a view matrix from separate position and rotation
	/// components.
	/// </summary>
	/// <remarks>
	/// The rotation must be a normalized quaternion.
	/// Camera scale is intentionally ignored.
	/// </remarks>
	public static Matrix4x4 CreateViewMatrix(
		in PositionComponent position,
		in RotationComponent rotation)
	{
		Quaternion inverseRotation =
			Quaternion.Conjugate(rotation.Value);

		return Matrix4x4.CreateTranslation(-position.Value)
			* Matrix4x4.CreateFromQuaternion(inverseRotation);
	}
}
