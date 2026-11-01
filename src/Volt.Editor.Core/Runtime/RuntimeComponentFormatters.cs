using Volt.Ecs;
using Volt.Ecs.Component;

namespace Volt.Editor.Core.Runtime;

/// <summary>
/// Describes one project-owned ECS component for the runtime inspector.
/// </summary>
/// <remarks>
/// <para>
/// A game's component is unmanaged data in native storage. The runtime
/// inspector can read it, but it cannot know which fields are meaningful:
/// a struct is bytes, not labels. A formatter is where a project says what
/// its component holds, so Play Mode shows real gameplay values instead of
/// a size.
/// </para>
/// <para>
/// <see cref="Describe"/> reads through the world's copying accessor, so it
/// never returns a reference into chunk storage and stays valid while
/// structural changes happen elsewhere.
/// </para>
/// </remarks>
public interface IRuntimeComponentFormatter
{
	/// <summary>
	/// Gets the name the inspector shows for this component.
	/// </summary>
	string DisplayName { get; }

	/// <summary>
	/// Gets the CLR type name of the component being described.
	/// </summary>
	string TypeName { get; }

	/// <summary>
	/// Gets the component's identity in the ECS registry.
	/// </summary>
	/// <remarks>
	/// Registration is per CLR type, so a reloaded gameplay assembly
	/// receives a fresh identifier and a stale formatter can never describe
	/// a newer component by accident.
	/// </remarks>
	ComponentType Type { get; }

	/// <summary>
	/// Reads the component's presentable values from one live entity.
	/// </summary>
	/// <returns>
	/// The values to display, or an empty list when the entity no longer
	/// stores this component.
	/// </returns>
	IReadOnlyList<RuntimeFieldSnapshot> Describe(World world, Entity entity);
}

/// <summary>
/// Describes one unmanaged component type with a caller-supplied
/// projection.
/// </summary>
public sealed class RuntimeComponentFormatter<T> : IRuntimeComponentFormatter
	where T : unmanaged
{
	private readonly Func<T, IReadOnlyList<RuntimeFieldSnapshot>> _describe;

	internal RuntimeComponentFormatter(
		string displayName,
		Func<T, IReadOnlyList<RuntimeFieldSnapshot>> describe)
	{
		DisplayName = displayName;
		TypeName = typeof(T).Name;
		Type = ComponentRegistry.Register<T>();
		_describe = describe;
	}

	public string DisplayName { get; }

	public string TypeName { get; }

	public ComponentType Type { get; }

	public IReadOnlyList<RuntimeFieldSnapshot> Describe(
		World world,
		Entity entity)
	{
		ArgumentNullException.ThrowIfNull(world);

		return world.TryReadComponent<T>(entity, out T value)
			? _describe(value)
			: [];
	}
}

/// <summary>
/// The registry of runtime component descriptions.
/// </summary>
/// <remarks>
/// <para>
/// Entries are keyed by the ECS component identity, which is process-wide
/// and assigned per CLR type. A gameplay assembly that is rebuilt produces
/// new component identities, so its new descriptions replace nothing and
/// the old ones simply stop matching a live world. The registry is bounded
/// so a long session that reloads repeatedly cannot grow it without limit.
/// </para>
/// <para>
/// Access is serialized because the runtime inspector reads descriptions
/// while a gameplay assembly load may register new ones.
/// </para>
/// </remarks>
public static class RuntimeComponentFormatters
{
	private const int Capacity = 512;

	private static readonly Lock s_gate = new();

	private static readonly Dictionary<ComponentType, IRuntimeComponentFormatter>
		s_formatters = [];

	/// <summary>
	/// Gets the number of registered descriptions.
	/// </summary>
	public static int Count
	{
		get
		{
			lock (s_gate)
			{
				return s_formatters.Count;
			}
		}
	}

	/// <summary>
	/// Registers a description for an unmanaged component type.
	/// </summary>
	/// <returns>
	/// The formatter, or the existing one when this type already has one.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The registry is full, which means an unbounded number of gameplay
	/// assemblies have been loaded in this process.
	/// </exception>
	public static IRuntimeComponentFormatter Register<T>(
		string displayName,
		Func<T, IReadOnlyList<RuntimeFieldSnapshot>> describe)
		where T : unmanaged
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
		ArgumentNullException.ThrowIfNull(describe);

		RuntimeComponentFormatter<T> formatter = new(displayName, describe);

		lock (s_gate)
		{
			if (s_formatters.TryGetValue(
				formatter.Type,
				out IRuntimeComponentFormatter? existing))
			{
				return existing;
			}

			if (s_formatters.Count >= Capacity)
			{
				throw new InvalidOperationException(
					$"More than {Capacity} component descriptions are " +
					"registered. Reloading gameplay assemblies repeatedly " +
					"has filled the runtime inspector's registry.");
			}

			s_formatters.Add(formatter.Type, formatter);
			return formatter;
		}
	}

	/// <summary>
	/// Finds the description for a component identity.
	/// </summary>
	public static bool TryGet(
		ComponentType type,
		out IRuntimeComponentFormatter formatter)
	{
		lock (s_gate)
		{
			return s_formatters.TryGetValue(type, out formatter!);
		}
	}

	/// <summary>
	/// Removes one description.
	/// </summary>
	public static bool Unregister<T>() where T : unmanaged
	{
		return Unregister(ComponentRegistry.Register<T>());
	}

	/// <summary>
	/// Removes the description for one component identity.
	/// </summary>
	/// <remarks>
	/// A formatter closes over the component's CLR type, so an entry left
	/// behind by an unloaded gameplay assembly keeps that assembly's load
	/// context alive. Unloading the assembly therefore releases its
	/// descriptions, which is what makes the context collectable.
	/// </remarks>
	public static bool Unregister(ComponentType type)
	{
		lock (s_gate)
		{
			return s_formatters.Remove(type);
		}
	}

	/// <summary>
	/// Removes every description.
	/// </summary>
	/// <remarks>
	/// Only a test or a tool that owns the whole process should need this;
	/// a session unloads its assembly instead.
	/// </remarks>
	public static void Clear()
	{
		lock (s_gate)
		{
			s_formatters.Clear();
		}
	}
}
