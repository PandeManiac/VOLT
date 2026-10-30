using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Motion;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Runtime;

/// <summary>
/// A runtime component value the ECS inspector can edit.
/// </summary>
/// <remarks>
/// Only vector-valued components are listed. Rotation is deliberately
/// absent: the runtime stores a world-space quaternion, and an Euler
/// editor would have to invent a convention the simulation does not
/// use. Authored Euler angles stay in the Inspector.
/// </remarks>
public enum RuntimeEditChannel
{
	Position = 0,
	Scale = 1,
	Velocity = 2,
	AngularVelocity = 3
}

/// <summary>
/// The outcome of one runtime component edit.
/// </summary>
public readonly record struct RuntimeEditResult(bool Applied, string Message)
{
	public static RuntimeEditResult Failure(string message)
	{
		return new RuntimeEditResult(false, message);
	}

	public static RuntimeEditResult Success(string message)
	{
		return new RuntimeEditResult(true, message);
	}
}

/// <summary>
/// Writes component values into the live Play Mode world.
/// </summary>
/// <remarks>
/// <para>
/// The write-back rule is explicit and deliberately narrow: an edit
/// changes the ECS component that is running now. It never mutates the
/// authored <see cref="SceneDocument"/>, never marks it dirty, and
/// never creates an undo entry. Stopping Play disposes the world, so
/// every edit disappears with it; saving the scene cannot capture one.
/// <see cref="ApplyToScene"/> is the one explicit exception: the
/// developer asks for it, it copies position or scale into the saved
/// transform, and it records a single undo step.
/// </para>
/// <para>
/// The editor also refuses to add or remove components. Adding one
/// would migrate the entity to another archetype from the UI thread
/// while the simulation and renderer may be reading chunks, so the
/// panel is limited to writing values of components the entity already
/// has. A running system may overwrite the value on the next tick;
/// that is reported to the caller instead of being hidden.
/// </para>
/// </remarks>
public static class RuntimeWorldEditor
{
	/// <summary>
	/// Applies an edit through the play session, refusing it unless
	/// Play Mode is running or paused.
	/// </summary>
	public static RuntimeEditResult Apply(
		PlaySession session,
		Guid authoredId,
		RuntimeEditChannel channel,
		Vector3 value)
	{
		ArgumentNullException.ThrowIfNull(session);

		if (session.State == PlayState.Stopped ||
			session.Runtime is not RuntimeScene runtime)
		{
			return RuntimeEditResult.Failure(
				"Start Play Mode to edit runtime values.");
		}

		return Apply(runtime, authoredId, channel, value);
	}

	/// <summary>
	/// Applies an edit directly to a runtime world.
	/// </summary>
	public static RuntimeEditResult Apply(
		RuntimeScene runtime,
		Guid authoredId,
		RuntimeEditChannel channel,
		Vector3 value)
	{
		ArgumentNullException.ThrowIfNull(runtime);

		if (!IsFinite(value))
		{
			return RuntimeEditResult.Failure(
				"Runtime values must be finite numbers.");
		}

		if (runtime.Find(authoredId) is not RuntimeEntityInfo info)
		{
			return RuntimeEditResult.Failure(
				"The selected object has no runtime entity.");
		}

		if (!runtime.World.TryGetSignature(info.Entity, out ComponentSignature signature))
		{
			return RuntimeEditResult.Failure(
				"The runtime entity is no longer alive.");
		}

		if (!signature.Contains(ComponentTypeFor(channel)))
		{
			return RuntimeEditResult.Failure(
				$"{Label(channel)} is not present on this runtime entity.");
		}

		switch (channel)
		{
			case RuntimeEditChannel.Position:
				runtime.World.SetComponent(
					info.Entity,
					new PositionComponent(value));
				break;

			case RuntimeEditChannel.Scale:
				runtime.World.SetComponent(
					info.Entity,
					new ScaleComponent(value));
				break;

			case RuntimeEditChannel.Velocity:
				runtime.World.SetComponent(
					info.Entity,
					new VelocityComponent(value));
				break;

			case RuntimeEditChannel.AngularVelocity:
				runtime.World.SetComponent(
					info.Entity,
					new AngularVelocityComponent(value));
				break;

			default:
				return RuntimeEditResult.Failure(
					"That runtime value is not editable.");
		}

		return RuntimeEditResult.Success(
			$"{Label(channel)} applied to the live world. " +
			"Stopping Play discards it.");
	}

	/// <summary>
	/// Copies one runtime transform value into the authored scene as a
	/// single undoable step.
	/// </summary>
	/// <remarks>
	/// This is the only path that writes runtime data back to the scene,
	/// and it runs only when the developer clicks <b>Apply to Scene</b>.
	/// Only position and scale have an authored counterpart in
	/// <see cref="TransformData"/>; velocity and angular velocity are
	/// runtime-only and are refused. The runtime keeps running, so a
	/// later tick may move the live value away from what was copied;
	/// the caller sees that in the scene and in the ECS panel.
	/// </remarks>
	public static RuntimeEditResult ApplyToScene(
		PlaySession session,
		SceneDocument scene,
		UndoStack undo,
		Guid authoredId,
		RuntimeEditChannel channel)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(undo);

		if (session.State == PlayState.Stopped ||
			session.Runtime is not RuntimeScene runtime)
		{
			return RuntimeEditResult.Failure(
				"Start Play Mode to copy a runtime value into the scene.");
		}

		return ApplyToScene(runtime, scene, undo, authoredId, channel);
	}

	/// <summary>
	/// Copies one runtime transform value into the authored scene using
	/// a world that is already running.
	/// </summary>
	public static RuntimeEditResult ApplyToScene(
		RuntimeScene runtime,
		SceneDocument scene,
		UndoStack undo,
		Guid authoredId,
		RuntimeEditChannel channel)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(undo);

		if (channel is RuntimeEditChannel.Velocity or
			RuntimeEditChannel.AngularVelocity)
		{
			return RuntimeEditResult.Failure(
				$"{Label(channel)} has no authored counterpart; only " +
				"position and scale can be copied into the scene.");
		}

		if (runtime.Find(authoredId) is not RuntimeEntityInfo info)
		{
			return RuntimeEditResult.Failure(
				"The selected object has no runtime entity.");
		}

		if (!TryReadChannel(
			runtime.World,
			info.Entity,
			channel,
			out Vector3 value))
		{
			return RuntimeEditResult.Failure(
				$"{Label(channel)} is not present on this runtime entity.");
		}

		if (scene.Find(authoredId) is not SceneEntity entity)
		{
			return RuntimeEditResult.Failure(
				"The selected object is no longer in the scene.");
		}

		TransformData after = entity.Transform.Clone();

		switch (channel)
		{
			case RuntimeEditChannel.Position:
				after.Position = value;
				break;

			case RuntimeEditChannel.Scale:
				after.Scale = value;
				break;

			default:
				return RuntimeEditResult.Failure(
					"That runtime value has no authored counterpart.");
		}

		if (after.Equals(entity.Transform))
		{
			return RuntimeEditResult.Failure(
				$"The scene already matches the runtime {Label(channel)}; " +
				"no undo step was created.");
		}

		undo.Execute(new SetTransformCommand(scene, authoredId, after));

		return RuntimeEditResult.Success(
			$"Copied runtime {Label(channel)} into the authored scene as " +
			"one undo step.");
	}

	/// <summary>
	/// Copies the live position and scale into the authored transform
	/// as one undoable step.
	/// </summary>
	/// <remarks>
	/// This is the combined form of <see cref="ApplyToScene"/> for a
	/// simulated pose: position and scale are read from the runtime in
	/// one pass and written by a single <see cref="SetTransformCommand"/>,
	/// so they cannot be undone separately. Rotation is not copied
	/// because the runtime stores a world-space quaternion. When only
	/// one of the two components is present, the other is left alone and
	/// the message says so.
	/// </remarks>
	public static RuntimeEditResult ApplyTransformToScene(
		PlaySession session,
		SceneDocument scene,
		UndoStack undo,
		Guid authoredId)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(undo);

		if (session.State == PlayState.Stopped ||
			session.Runtime is not RuntimeScene runtime)
		{
			return RuntimeEditResult.Failure(
				"Start Play Mode to copy a runtime value into the scene.");
		}

		return ApplyTransformToScene(runtime, scene, undo, authoredId);
	}

	/// <summary>
	/// Copies the live position and scale into the authored transform
	/// using a world that is already running.
	/// </summary>
	public static RuntimeEditResult ApplyTransformToScene(
		RuntimeScene runtime,
		SceneDocument scene,
		UndoStack undo,
		Guid authoredId)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(undo);

		if (runtime.Find(authoredId) is not RuntimeEntityInfo info)
		{
			return RuntimeEditResult.Failure(
				"The selected object has no runtime entity.");
		}

		if (scene.Find(authoredId) is not SceneEntity entity)
		{
			return RuntimeEditResult.Failure(
				"The selected object is no longer in the scene.");
		}

		bool hasPosition = TryReadChannel(
			runtime.World,
			info.Entity,
			RuntimeEditChannel.Position,
			out Vector3 position);

		bool hasScale = TryReadChannel(
			runtime.World,
			info.Entity,
			RuntimeEditChannel.Scale,
			out Vector3 scale);

		if (!hasPosition && !hasScale)
		{
			return RuntimeEditResult.Failure(
				"The runtime entity has neither position nor scale to copy.");
		}

		TransformData after = entity.Transform.Clone();

		if (hasPosition)
		{
			after.Position = position;
		}

		if (hasScale)
		{
			after.Scale = scale;
		}

		if (after.Equals(entity.Transform))
		{
			return RuntimeEditResult.Failure(
				"The scene already matches the runtime transform; no undo " +
				"step was created.");
		}

		undo.Execute(new SetTransformCommand(scene, authoredId, after));

		if (hasPosition && hasScale)
		{
			return RuntimeEditResult.Success(
				"Copied runtime position and scale into the authored scene " +
				"as one undo step.");
		}

		return RuntimeEditResult.Success(hasPosition
			? "Copied runtime position into the authored scene as one undo " +
				"step; the runtime entity has no scale."
			: "Copied runtime scale into the authored scene as one undo " +
				"step; the runtime entity has no position.");
	}

	private static bool TryReadChannel(
		World world,
		Entity entity,
		RuntimeEditChannel channel,
		out Vector3 value)
	{
		switch (channel)
		{
			case RuntimeEditChannel.Position
				when world.TryReadComponent<PositionComponent>(
					entity,
					out PositionComponent position):
				value = position.Value;
				return true;

			case RuntimeEditChannel.Scale
				when world.TryReadComponent<ScaleComponent>(
					entity,
					out ScaleComponent scale):
				value = scale.Value;
				return true;

			case RuntimeEditChannel.Velocity
				when world.TryReadComponent<VelocityComponent>(
					entity,
					out VelocityComponent velocity):
				value = velocity.Value;
				return true;

			case RuntimeEditChannel.AngularVelocity
				when world.TryReadComponent<AngularVelocityComponent>(
					entity,
					out AngularVelocityComponent angular):
				value = angular.Value;
				return true;

			default:
				value = default;
				return false;
		}
	}

	private static ComponentType ComponentTypeFor(RuntimeEditChannel channel)
	{
		return channel switch
		{
			RuntimeEditChannel.Position =>
				ComponentRegistry.Get<PositionComponent>(),
			RuntimeEditChannel.Scale =>
				ComponentRegistry.Get<ScaleComponent>(),
			RuntimeEditChannel.Velocity =>
				ComponentRegistry.Get<VelocityComponent>(),
			RuntimeEditChannel.AngularVelocity =>
				ComponentRegistry.Get<AngularVelocityComponent>(),
			_ => throw new ArgumentOutOfRangeException(
				nameof(channel),
				channel,
				"Unknown runtime edit channel.")
		};
	}

	private static string Label(RuntimeEditChannel channel)
	{
		return channel switch
		{
			RuntimeEditChannel.Position => "Position",
			RuntimeEditChannel.Scale => "Scale",
			RuntimeEditChannel.Velocity => "Velocity",
			RuntimeEditChannel.AngularVelocity => "Angular velocity",
			_ => channel.ToString()
		};
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X) &&
			float.IsFinite(value.Y) &&
			float.IsFinite(value.Z);
	}
}
