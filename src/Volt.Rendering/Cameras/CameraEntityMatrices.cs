using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;

namespace Volt.Rendering.Cameras;

/// <summary>
/// Builds view and projection matrices from an ECS camera entity.
/// </summary>
/// <remarks>
/// <para>
/// A camera entity owns a <see cref="CameraComponent"/> for its
/// projection settings, plus <see cref="PositionComponent"/> and
/// <see cref="RotationComponent"/> for its transform. The view is
/// built with <see cref="CameraComponent.CreateViewMatrix"/> and the
/// projection with
/// <see cref="CameraProjection.CreatePerspective"/>.
/// </para>
/// <para>
/// Camera scale is not part of a view or projection matrix and is
/// ignored; a camera entity does not need a
/// <see cref="ScaleComponent"/>.
/// </para>
/// <para>
/// Both methods read the entity's components each time they are
/// called, so a component update is visible on the next call. They
/// perform no heap allocation.
/// </para>
/// </remarks>
public static class CameraEntityMatrices
{
	/// <summary>
	/// Attempts to build the view and projection for a camera entity.
	/// </summary>
	/// <param name="world">The world that owns the entity.</param>
	/// <param name="entity">
	/// The camera entity. It must be alive and carry all three camera
	/// components.
	/// </param>
	/// <param name="aspectRatio">
	/// The viewport width divided by its height.
	/// </param>
	/// <param name="viewportY">
	/// Which way the viewport's Y axis points.
	/// </param>
	/// <param name="view">
	/// Receives the world-to-view matrix, or identity on failure.
	/// </param>
	/// <param name="projection">
	/// Receives the view-to-clip matrix, or identity on failure.
	/// </param>
	/// <returns>
	/// True when the entity is alive and has every required camera
	/// component; otherwise false.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="world"/> is null.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The camera's projection settings or the aspect ratio are
	/// invalid, or the viewport orientation is unknown.
	/// </exception>
	public static bool TryCreateMatrices(
		World world,
		Entity entity,
		float aspectRatio,
		ViewportYOrientation viewportY,
		out Matrix4x4 view,
		out Matrix4x4 projection)
	{
		ArgumentNullException.ThrowIfNull(world);

		view = Matrix4x4.Identity;
		projection = Matrix4x4.Identity;

		if (!world.IsAlive(entity) ||
			!world.HasComponent<CameraComponent>(entity) ||
			!world.HasComponent<PositionComponent>(entity) ||
			!world.HasComponent<RotationComponent>(entity))
		{
			return false;
		}

		CameraComponent camera =
			world.GetComponent<CameraComponent>(entity);

		PositionComponent position =
			world.GetComponent<PositionComponent>(entity);

		RotationComponent rotation =
			world.GetComponent<RotationComponent>(entity);

		view = CameraComponent.CreateViewMatrix(
			in position,
			in rotation);

		projection = CameraProjection.CreatePerspective(
			camera,
			aspectRatio,
			viewportY);

		return true;
	}

	/// <summary>
	/// Builds the view and projection for a camera entity, or throws
	/// when the entity cannot act as a camera.
	/// </summary>
	/// <param name="world">The world that owns the entity.</param>
	/// <param name="entity">The camera entity.</param>
	/// <param name="aspectRatio">
	/// The viewport width divided by its height.
	/// </param>
	/// <param name="viewportY">
	/// Which way the viewport's Y axis points.
	/// </param>
	/// <param name="view">Receives the world-to-view matrix.</param>
	/// <param name="projection">
	/// Receives the view-to-clip matrix.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// The entity is not alive or is missing a required component.
	/// The message names the missing pieces.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The camera's projection settings or the aspect ratio are
	/// invalid, or the viewport orientation is unknown.
	/// </exception>
	public static void CreateMatrices(
		World world,
		Entity entity,
		float aspectRatio,
		ViewportYOrientation viewportY,
		out Matrix4x4 view,
		out Matrix4x4 projection)
	{
		ArgumentNullException.ThrowIfNull(world);

		if (!world.IsAlive(entity))
		{
			throw new InvalidOperationException(
				$"The camera entity {entity} is not alive in this " +
				"world.");
		}

		CameraComponent camera =
			RequireComponent<CameraComponent>(world, entity);

		PositionComponent position =
			RequireComponent<PositionComponent>(world, entity);

		RotationComponent rotation =
			RequireComponent<RotationComponent>(world, entity);

		view = CameraComponent.CreateViewMatrix(
			in position,
			in rotation);

		projection = CameraProjection.CreatePerspective(
			camera,
			aspectRatio,
			viewportY);
	}

	private static T RequireComponent<T>(World world, Entity entity)
		where T : unmanaged
	{
		if (!world.HasComponent<T>(entity))
		{
			throw new InvalidOperationException(
				$"The camera entity {entity} is missing " +
				$"{typeof(T).Name}.");
		}

		return world.GetComponent<T>(entity);
	}
}
