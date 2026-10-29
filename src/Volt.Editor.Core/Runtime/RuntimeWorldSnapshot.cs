using System.Globalization;
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
	string? Note);

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
	/// Runs a real ECS query over the runtime world and reports the
	/// archetypes and entity count it matched.
	/// </summary>
	/// <remarks>
	/// The world's structure is frozen for the duration so a concurrent
	/// structural change cannot invalidate the enumeration. The result
	/// holds only counts and signature strings.
	/// </remarks>
	public static RuntimeQueryResult RunQuery(
		RuntimeScene runtime,
		IReadOnlyList<ComponentType> required)
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
		int entities = 0;

		using World.StructuralFreezeScope freeze =
			runtime.World.FreezeStructure();

		foreach (WorldQueryChunk chunk in runtime.World.Query(query))
		{
			entities += chunk.Count;

			string description = DescribeSignature(chunk.Archetype.Signature);

			if (!signatures.Contains(description))
			{
				signatures.Add(description);
			}
		}

		signatures.Sort(StringComparer.Ordinal);

		return new RuntimeQueryResult(
			signatures.Count,
			entities,
			signatures);
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

		IReadOnlyList<RuntimeFieldSnapshot>? fields =
			ReadFields(world, entity, componentType);

		return new RuntimeComponentSnapshot(
			ShortName(componentType),
			componentType.Name,
			metadata.Size,
			fields ?? [],
			fields is null
				? $"No inspector formatter for {componentType.Name}; " +
					$"{metadata.Size} bytes per entity."
				: null);
	}

	private static IReadOnlyList<RuntimeFieldSnapshot>? ReadFields(
		World world,
		Entity entity,
		Type componentType)
	{
		if (componentType == typeof(Volt.Ecs.Components.Spatial.PositionComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Spatial.PositionComponent>(
					entity,
					out Volt.Ecs.Components.Spatial.PositionComponent position)
				? Vector(position.Value.X, position.Value.Y, position.Value.Z)
				: null;
		}

		if (componentType == typeof(Volt.Ecs.Components.Spatial.RotationComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Spatial.RotationComponent>(
					entity,
					out Volt.Ecs.Components.Spatial.RotationComponent rotation)
				? Rotation(rotation.Value)
				: null;
		}

		if (componentType == typeof(Volt.Ecs.Components.Spatial.ScaleComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Spatial.ScaleComponent>(
					entity,
					out Volt.Ecs.Components.Spatial.ScaleComponent scale)
				? Vector(scale.Value.X, scale.Value.Y, scale.Value.Z)
				: null;
		}

		if (componentType == typeof(Volt.Ecs.Components.Motion.VelocityComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Motion.VelocityComponent>(
					entity,
					out Volt.Ecs.Components.Motion.VelocityComponent velocity)
				? Vector(velocity.Value.X, velocity.Value.Y, velocity.Value.Z)
				: null;
		}

		if (componentType ==
			typeof(Volt.Ecs.Components.Motion.AngularVelocityComponent))
		{
			return world.TryReadComponent<
					Volt.Ecs.Components.Motion.AngularVelocityComponent>(
					entity,
					out Volt.Ecs.Components.Motion.AngularVelocityComponent angular)
				? Vector(angular.Value.X, angular.Value.Y, angular.Value.Z)
				: null;
		}

		return null;
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
/// The measured result of a component query over the runtime world.
/// </summary>
public sealed record RuntimeQueryResult(
	int ArchetypeCount,
	int EntityCount,
	IReadOnlyList<string> Signatures);
