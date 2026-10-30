using System.Globalization;
using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Runtime;

/// <summary>
/// One named value of a runtime component, already formatted for
/// display.
/// </summary>
public sealed record RuntimeFieldSnapshot(string Label, string Value);

/// <summary>
/// The runtime value of one component on one entity.
/// </summary>
public sealed record RuntimeComponentSnapshot(
	string DisplayName,
	string TypeName,
	int Size,
	IReadOnlyList<RuntimeFieldSnapshot> Fields,
	string? Note,
	Vector3? Value = null,
	RuntimeEditChannel? Editable = null);

/// <summary>
/// Measured storage statistics for one runtime archetype.
/// </summary>
public sealed record RuntimeArchetypeSnapshot(
	string Signature,
	int EntityCount,
	int ChunkCount,
	int Capacity,
	long AllocatedBytes)
{
	public float Occupancy => Capacity <= 0
		? 0f
		: (float)EntityCount / Capacity;
}

/// <summary>
/// A runtime entity, its real ECS handle, and the values actually stored
/// in its component columns.
/// </summary>
public sealed record RuntimeEntitySnapshot(
	Guid AuthoredId,
	string Name,
	Entity Handle,
	string Archetype,
	IReadOnlyList<RuntimeComponentSnapshot> Components);

/// <summary>
/// One authored transform value compared with what the runtime world
/// currently stores for the same channel.
/// </summary>
public sealed record RuntimeChannelComparison(
	RuntimeEditChannel Channel,
	string Label,
	Vector3 RuntimeValue,
	Vector3 AuthoredValue,
	bool Diverged);

/// <summary>
/// The authored-versus-runtime comparison for one selected entity.
/// </summary>
public sealed record RuntimeTransformComparison(
	IReadOnlyList<RuntimeChannelComparison> Channels)
{
	public bool AnyDiverged => Channels.Any(channel => channel.Diverged);

	/// <summary>
	/// Gets the comparison for one channel, or null when the runtime
	/// entity does not store that channel.
	/// </summary>
	public RuntimeChannelComparison? For(RuntimeEditChannel channel)
	{
		foreach (RuntimeChannelComparison entry in Channels)
		{
			if (entry.Channel == channel)
			{
				return entry;
			}
		}

		return null;
	}
}

/// <summary>
/// A read-only capture of the running ECS world.
/// </summary>
/// <remarks>
/// <para>
/// This inspects the play session's own <see cref="World"/>, not the
/// authored scene. A persistent scene entity and its runtime handle are
/// deliberately different things: the authored GUID is carried as the
/// source identity, while the handle, archetype, and component values
/// describe the temporary world that Play Mode built.
/// </para>
/// <para>
/// Capture copies every value it displays. It never retains a
/// <c>ref</c> into chunk storage, so a snapshot stays valid after the
/// world migrates an entity or shuts down. Capture is meant for a
/// throttled tool refresh, not for every rendered frame.
/// </para>
/// </remarks>
public sealed class RuntimeWorldSnapshot
{
	/// <summary>
	/// The difference below which a runtime value is treated as matching
	/// the authored one.
	/// </summary>
	/// <remarks>
	/// The two values live in different representations and the
	/// simulation accumulates float error, so an exact comparison would
	/// flag a motionless object as diverged. This tolerance is smaller
	/// than the three-decimal precision the panel displays.
	/// </remarks>
	public const float DivergenceTolerance = 0.0005f;

	private RuntimeWorldSnapshot(
		string worldName,
		PlayState state,
		int entityCount,
		IReadOnlyList<RuntimeArchetypeSnapshot> archetypes,
		RuntimeEntitySnapshot? selectedEntity)
	{
		WorldName = worldName;
		State = state;
		EntityCount = entityCount;
		Archetypes = archetypes;
		SelectedEntity = selectedEntity;
	}

	public string WorldName { get; }

	public PlayState State { get; }

	public int EntityCount { get; }

	public IReadOnlyList<RuntimeArchetypeSnapshot> Archetypes { get; }

	public RuntimeEntitySnapshot? SelectedEntity { get; }

	public static RuntimeWorldSnapshot Empty { get; } = new(
		"No runtime world",
		PlayState.Stopped,
		0,
		[],
		null);

	public static RuntimeWorldSnapshot Capture(
		RuntimeScene? runtime,
		PlayState state,
		Guid? selectedAuthoredId)
	{
		if (runtime is null)
		{
			// Without a world there is nothing to describe, and the
			// state is necessarily stopped.
			return Empty;
		}

		World world = runtime.World;
		List<RuntimeArchetypeSnapshot> archetypes = [];

		foreach (Archetype archetype in world.GetArchetypes())
		{
			if (!world.TryGetArchetypeStorage(
					archetype,
					out ArchetypeStorageInfo storage))
			{
				continue;
			}

			archetypes.Add(new RuntimeArchetypeSnapshot(
				DescribeSignature(archetype.Signature),
				storage.EntityCount,
				storage.ChunkCount,
				storage.Capacity,
				storage.AllocatedBytes));
		}

		archetypes.Sort(
			static (left, right) => right.EntityCount.CompareTo(left.EntityCount));

		RuntimeEntitySnapshot? selected = null;

		if (selectedAuthoredId is Guid authoredId &&
			runtime.Find(authoredId) is RuntimeEntityInfo info)
		{
			selected = CaptureEntity(world, info);
		}

		return new RuntimeWorldSnapshot(
			"Play Session",
			state,
			world.Count,
			archetypes,
			selected);
	}

	/// <summary>
	/// Compares the runtime position and scale with the authored
	/// transform, so the panel can show which values were never copied
	/// back.
	/// </summary>
	/// <remarks>
	/// Only channels the runtime entity actually stores are returned.
	/// Velocity and angular velocity are excluded because the authored
	/// transform has no field for them. An entity that has left the
	/// scene yields an empty comparison rather than an error: the panel
	/// is describing a runtime world that may outlive the selection.
	/// </remarks>
	public static RuntimeTransformComparison CompareTransform(
		RuntimeEntitySnapshot entity,
		SceneDocument scene)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentNullException.ThrowIfNull(scene);

		List<RuntimeChannelComparison> comparisons = [];

		if (scene.Find(entity.AuthoredId) is not SceneEntity authored)
		{
			return new RuntimeTransformComparison(comparisons);
		}

		foreach (RuntimeComponentSnapshot component in entity.Components)
		{
			if (component.Editable is not RuntimeEditChannel channel ||
				component.Value is not Vector3 runtime)
			{
				continue;
			}

			Vector3 authoredValue;

			switch (channel)
			{
				case RuntimeEditChannel.Position:
					authoredValue = authored.Transform.Position;
					break;

				case RuntimeEditChannel.Scale:
					authoredValue = authored.Transform.Scale;
					break;

				default:
					continue;
			}

			comparisons.Add(new RuntimeChannelComparison(
				channel,
				ChannelLabel(channel),
				runtime,
				authoredValue,
				!WithinTolerance(runtime, authoredValue)));
		}

		return new RuntimeTransformComparison(comparisons);
	}

	private static bool WithinTolerance(Vector3 left, Vector3 right)
	{
		return MathF.Abs(left.X - right.X) <= DivergenceTolerance &&
			MathF.Abs(left.Y - right.Y) <= DivergenceTolerance &&
			MathF.Abs(left.Z - right.Z) <= DivergenceTolerance;
	}

	private static string ChannelLabel(RuntimeEditChannel channel)
	{
		return channel switch
		{
			RuntimeEditChannel.Position => "Position",
			RuntimeEditChannel.Scale => "Scale",
			_ => channel.ToString()
		};
	}

	/// <summary>
	/// Runs a real ECS query over the runtime world and reports the
	/// archetypes it matched and the entities stored in them.
	/// </summary>
	/// <remarks>
	/// The world's structure is frozen for the duration so a concurrent
	/// structural change cannot invalidate the enumeration. Matches are
	/// ordered by authored scene order, with runtime-only entities after
	/// them, so a paged view stays stable between queries.
	/// </remarks>
	public static RuntimeQueryResult RunQuery(
		RuntimeScene runtime,
		IReadOnlyList<ComponentType> required,
		Func<Guid, string?>? authoredName = null)
	{
		ArgumentNullException.ThrowIfNull(runtime);
		ArgumentNullException.ThrowIfNull(required);

		ComponentSignature signature = ComponentSignature.Empty;

		foreach (ComponentType type in required)
		{
			signature = signature.With(type);
		}

		ComponentQuery query = new(required: signature);
		List<string> signatures = [];
		List<RuntimeQueryEntity> entities = [];
		Dictionary<Entity, RuntimeEntityInfo> byHandle = [];
		Dictionary<Entity, int> authoredOrder = [];

		foreach (RuntimeEntityInfo info in runtime.Entities)
		{
			byHandle[info.Entity] = info;
			authoredOrder[info.Entity] = authoredOrder.Count;
		}

		using World.StructuralFreezeScope freeze =
			runtime.World.FreezeStructure();

		foreach (WorldQueryChunk chunk in runtime.World.Query(query))
		{
			string description = DescribeSignature(chunk.Archetype.Signature);

			if (!signatures.Contains(description))
			{
				signatures.Add(description);
			}

			foreach (Entity handle in chunk.GetEntities())
			{
				byHandle.TryGetValue(handle, out RuntimeEntityInfo? info);

				// The name a world was built with can be stale: a rename
				// changes the authored object, not the running entity. The
				// caller's lookup keeps the listed names current.
				string name = info?.AuthoredId is Guid authored &&
					authoredName?.Invoke(authored) is string live &&
					live.Length > 0
						? live
						: info?.Name ?? string.Empty;

				entities.Add(new RuntimeQueryEntity(
					handle,
					info?.AuthoredId,
					name,
					description));
			}
		}

		entities.Sort((left, right) =>
		{
			int leftOrder = authoredOrder.GetValueOrDefault(
				left.Handle,
				int.MaxValue);

			int rightOrder = authoredOrder.GetValueOrDefault(
				right.Handle,
				int.MaxValue);

			return leftOrder != rightOrder
				? leftOrder.CompareTo(rightOrder)
				: left.Handle.Index.CompareTo(right.Handle.Index);
		});

		signatures.Sort(StringComparer.Ordinal);

		return new RuntimeQueryResult(
			signatures.Count,
			entities.Count,
			signatures,
			entities);
	}

	/// <summary>
	/// Describes a signature with the short component names the engine
	/// uses in its own diagnostics.
	/// </summary>
	public static string DescribeSignature(ComponentSignature signature)
	{
		ArgumentNullException.ThrowIfNull(signature);

		if (signature.Count == 0)
		{
			return "(no components)";
		}

		string[] names = new string[signature.Count];

		for (int index = 0; index < signature.Count; index++)
		{
			names[index] = ShortName(
				ComponentRegistry.GetInfo(signature[index]).ClrType);
		}

		return string.Join(" + ", names);
	}

	private static RuntimeEntitySnapshot CaptureEntity(
		World world,
		RuntimeEntityInfo info)
	{
		if (!world.TryGetSignature(info.Entity, out ComponentSignature signature))
		{
			return new RuntimeEntitySnapshot(
				info.AuthoredId,
				info.Name,
				info.Entity,
				"(entity no longer alive)",
				[]);
		}

		List<RuntimeComponentSnapshot> components = [];

		for (int index = 0; index < signature.Count; index++)
		{
			components.Add(DescribeComponent(
				world,
				info.Entity,
				signature[index]));
		}

		return new RuntimeEntitySnapshot(
			info.AuthoredId,
			info.Name,
			info.Entity,
			DescribeSignature(signature),
			components);
	}

	private static RuntimeComponentSnapshot DescribeComponent(
		World world,
		Entity entity,
		ComponentType type)
	{
		ComponentRegistry.ComponentInfo metadata =
			ComponentRegistry.GetInfo(type);

		Type componentType = metadata.ClrType;
		string displayName = ShortName(componentType);

		if (componentType ==
			typeof(Volt.Ecs.Components.Spatial.RotationComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Spatial.RotationComponent>(
					entity,
					out Volt.Ecs.Components.Spatial.RotationComponent rotation)
				? new RuntimeComponentSnapshot(
					displayName,
					componentType.Name,
					metadata.Size,
					Rotation(rotation.Value),
					"Stored as a world-space quaternion; rotation is not " +
					"edited here. Change the authored Euler angles in the " +
					"Inspector before Play instead.")
				: new RuntimeComponentSnapshot(
					displayName,
					componentType.Name,
					metadata.Size,
					[],
					"The rotation component is no longer readable.");
		}

		if (TryReadVector(world, entity, componentType, out Vector3 value))
		{
			return new RuntimeComponentSnapshot(
				displayName,
				componentType.Name,
				metadata.Size,
				Vector(value.X, value.Y, value.Z),
				null,
				value,
				EditChannelFor(componentType));
		}

		return new RuntimeComponentSnapshot(
			displayName,
			componentType.Name,
			metadata.Size,
			[],
			$"No inspector formatter; {metadata.Size} bytes per entity.");
	}

	/// <summary>
	/// Gets the edit channel for a runtime vector component, or null
	/// when the component's value is not editable from this panel.
	/// </summary>
	private static RuntimeEditChannel? EditChannelFor(Type componentType)
	{
		if (componentType == typeof(
			Volt.Ecs.Components.Spatial.PositionComponent))
		{
			return RuntimeEditChannel.Position;
		}

		if (componentType == typeof(Volt.Ecs.Components.Spatial.ScaleComponent))
		{
			return RuntimeEditChannel.Scale;
		}

		if (componentType == typeof(Volt.Ecs.Components.Motion.VelocityComponent))
		{
			return RuntimeEditChannel.Velocity;
		}

		if (componentType ==
			typeof(Volt.Ecs.Components.Motion.AngularVelocityComponent))
		{
			return RuntimeEditChannel.AngularVelocity;
		}

		return null;
	}

	/// <summary>
	/// Reads the vector a runtime component stores, without retaining
	/// a reference into chunk storage.
	/// </summary>
	private static bool TryReadVector(
		World world,
		Entity entity,
		Type componentType,
		out Vector3 value)
	{
		if (componentType == typeof(Volt.Ecs.Components.Spatial.PositionComponent))
		{
			if (world.TryReadComponent<
				Volt.Ecs.Components.Spatial.PositionComponent>(
				entity,
				out Volt.Ecs.Components.Spatial.PositionComponent position))
			{
				value = position.Value;
				return true;
			}
		}
		else if (componentType ==
			typeof(Volt.Ecs.Components.Spatial.ScaleComponent))
		{
			if (world.TryReadComponent<
				Volt.Ecs.Components.Spatial.ScaleComponent>(
				entity,
				out Volt.Ecs.Components.Spatial.ScaleComponent scale))
			{
				value = scale.Value;
				return true;
			}
		}
		else if (componentType ==
			typeof(Volt.Ecs.Components.Motion.VelocityComponent))
		{
			if (world.TryReadComponent<
				Volt.Ecs.Components.Motion.VelocityComponent>(
				entity,
				out Volt.Ecs.Components.Motion.VelocityComponent velocity))
			{
				value = velocity.Value;
				return true;
			}
		}
		else if (componentType ==
			typeof(Volt.Ecs.Components.Motion.AngularVelocityComponent))
		{
			if (world.TryReadComponent<
				Volt.Ecs.Components.Motion.AngularVelocityComponent>(
				entity,
				out Volt.Ecs.Components.Motion.AngularVelocityComponent angular))
			{
				value = angular.Value;
				return true;
			}
		}

		value = default;
		return false;
	}

	private static IReadOnlyList<RuntimeFieldSnapshot> Vector(
		float x,
		float y,
		float z)
	{
		return
		[
			new RuntimeFieldSnapshot("X", Number(x)),
			new RuntimeFieldSnapshot("Y", Number(y)),
			new RuntimeFieldSnapshot("Z", Number(z))
		];
	}

	private static IReadOnlyList<RuntimeFieldSnapshot> Rotation(
		System.Numerics.Quaternion rotation)
	{
		return
		[
			new RuntimeFieldSnapshot("X", Number(rotation.X)),
			new RuntimeFieldSnapshot("Y", Number(rotation.Y)),
			new RuntimeFieldSnapshot("Z", Number(rotation.Z)),
			new RuntimeFieldSnapshot("W", Number(rotation.W))
		];
	}

	private static string Number(float value)
	{
		return value.ToString("0.###", CultureInfo.InvariantCulture);
	}

	private static string ShortName(Type type)
	{
		string name = type.Name;
		const string Suffix = "Component";

		return name.EndsWith(Suffix, StringComparison.Ordinal) &&
			name.Length > Suffix.Length
				? name[..^Suffix.Length]
				: name;
	}
}

/// <summary>
/// One entity matched by a runtime component query.
/// </summary>
/// <remarks>
/// <see cref="AuthoredId"/> is null for an entity the world created
/// outside the authored scene, and <see cref="Name"/> is empty then.
/// </remarks>
public sealed record RuntimeQueryEntity(
	Entity Handle,
	Guid? AuthoredId,
	string Name,
	string Archetype);

/// <summary>
/// The measured result of a component query over the runtime world.
/// </summary>
public sealed record RuntimeQueryResult(
	int ArchetypeCount,
	int EntityCount,
	IReadOnlyList<string> Signatures,
	IReadOnlyList<RuntimeQueryEntity> Entities);
