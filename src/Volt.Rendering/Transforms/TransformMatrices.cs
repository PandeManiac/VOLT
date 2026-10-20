using System.Numerics;
using Volt.Ecs.Components.Spatial;

namespace Volt.Rendering.Transforms;

/// <summary>
/// Builds world-space transform matrices from the ECS spatial
/// components.
/// </summary>
/// <remarks>
/// <para>
/// VOLT uses right-handed coordinates with +Y as up and -Z as
/// forward. <see cref="Matrix4x4"/> uses row-vector transforms, so a
/// position is multiplied on the left of a matrix and a model
/// transform composes as <c>scale * rotation * translation</c>.
/// </para>
/// <para>
/// The rotation is taken from <see cref="RotationComponent"/> exactly
/// as supplied. That component documents an expectation that its
/// quaternion is normalized; this utility does not normalize it
/// again, because doing so would silently change the orientation an
/// entity was given. Scale values, including zero and negative ones,
/// are applied as supplied.
/// </para>
/// <para>
/// Every method returns a value type and performs no heap allocation,
/// so the matrices can be rebuilt per frame and per draw.
/// </para>
/// </remarks>
public static class TransformMatrices
{
	/// <summary>
	/// Creates a model matrix that scales, rotates, then translates a
	/// local-space position into world space.
	/// </summary>
	/// <param name="position">The entity's world-space position.</param>
	/// <param name="rotation">
	/// The entity's orientation, expected to be normalized.
	/// </param>
	/// <param name="scale">The entity's scale.</param>
	/// <returns>
	/// <c>scale * rotation * translation</c> in row-vector order.
	/// Identity components produce <see cref="Matrix4x4.Identity"/>.
	/// </returns>
	public static Matrix4x4 CreateModelMatrix(
		in PositionComponent position,
		in RotationComponent rotation,
		in ScaleComponent scale)
	{
		return Matrix4x4.CreateScale(scale.Value)
			* Matrix4x4.CreateFromQuaternion(rotation.Value)
			* Matrix4x4.CreateTranslation(position.Value);
	}

	/// <summary>
	/// Composes one draw's complete model-view-projection matrix.
	/// </summary>
	/// <param name="model">The world-space model matrix.</param>
	/// <param name="view">The world-to-view matrix.</param>
	/// <param name="projection">The view-to-clip matrix.</param>
	/// <returns>
	/// <c>model * view * projection</c> in row-vector order.
	/// </returns>
	/// <remarks>
	/// The sixteen floats are intended to be uploaded in their natural
	/// memory order. A GLSL <c>mat4</c> reads them as column-major,
	/// which transposes the matrix implicitly, so the CPU must not
	/// transpose the result again. That upload convention belongs to
	/// the shader that consumes the matrix, not to this composition.
	/// </remarks>
	public static Matrix4x4 CreateModelViewProjection(
		Matrix4x4 model,
		Matrix4x4 view,
		Matrix4x4 projection)
	{
		return model * view * projection;
	}
}
