using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Motion;
using Volt.Ecs.Components.Spatial;
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
/// Use the Inspector to change what is saved.
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
