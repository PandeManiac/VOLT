
using System.Runtime.CompilerServices;

namespace Volt.Ecs.Component;

/// <summary>
/// Registers unmanaged component types and assigns stable,
/// process-wide component identifiers.
/// </summary>
/// <remarks>
/// Registration is thread-safe. Each component type receives
/// one identifier for the lifetime of the process.
///
/// Component identifiers are not guaranteed to remain the
/// same between process executions.
/// </remarks>
public static class ComponentRegistry
{
	private const int ComponentStorageAlignment = 16;

	private static readonly Lock s_lock = new();

	private static readonly Dictionary<Type, ComponentInfo> s_components = [];
	private static readonly List<ComponentInfo> s_componentsById = [];

	private static uint s_nextId = 1;

	/// <summary>
	/// Gets the number of registered component types.
	/// </summary>
	public static int Count
	{
		get
		{
			lock (s_lock)
			{
				return s_componentsById.Count;
			}
		}
	}

	/// <summary>
	/// Gets a snapshot of every registered component type.
	/// </summary>
	/// <remarks>
	/// Registration is process-wide and append-only, so the order is
	/// stable and the identifiers stay valid. A tool can enumerate this
	/// once instead of reflecting over loaded assemblies.
	/// </remarks>
	public static IReadOnlyList<ComponentInfo> GetRegisteredComponents()
	{
		lock (s_lock)
		{
			return [.. s_componentsById];
		}
	}

	/// <summary>
	/// Attempts to retrieve metadata for a component CLR type.
	/// </summary>
	public static bool TryGetInfoForType(Type componentType, out ComponentInfo info)
	{
		ArgumentNullException.ThrowIfNull(componentType);

		lock (s_lock)
		{
			return s_components.TryGetValue(componentType, out info);
		}
	}

	/// <summary>
	/// Registers an unmanaged component type.
	/// Repeated registrations return the same identifier.
	/// </summary>
	public static ComponentType Register<T>() where T : unmanaged
	{
		return Cache<T>.Type;
	}

	/// <summary>
	/// Gets the registered identifier for an unmanaged
	/// component type, registering it if necessary.
	/// </summary>
	public static ComponentType Get<T>() where T : unmanaged
	{
		return Cache<T>.Type;
	}

	/// <summary>
	/// Attempts to retrieve metadata for a component ID.
	/// </summary>
	public static bool TryGetInfo(
		ComponentType componentType,
		out ComponentInfo info)
	{
		lock (s_lock)
		{
			uint id = componentType.Id;

			if (id == 0 || id > (uint)s_componentsById.Count)
			{
				info = default;
				return false;
			}

			info = s_componentsById[(int)(id - 1)];
			return true;
		}
	}

	/// <summary>
	/// Retrieves metadata for a registered component ID.
	/// </summary>
	public static ComponentInfo GetInfo(ComponentType componentType)
	{
		if (!TryGetInfo(componentType, out ComponentInfo info))
		{
			throw new ArgumentOutOfRangeException(
				nameof(componentType),
				"The component type is not registered.");
		}

		return info;
	}

	private static ComponentInfo RegisterCore<T>() where T : unmanaged
	{
		lock (s_lock)
		{
			Type type = typeof(T);

			if (s_components.TryGetValue(type, out ComponentInfo existing))
			{
				return existing;
			}

			if (s_nextId == 0)
			{
				throw new InvalidOperationException(
					"The component registry has exhausted its identifiers.");
			}

			ComponentType componentType = new(s_nextId++);

			ComponentInfo info = new(
				componentType,
				type,
				Unsafe.SizeOf<T>(),
				ComponentStorageAlignment);

			s_components.Add(type, info);
			s_componentsById.Add(info);

			return info;
		}
	}

	private static class Cache<T> where T : unmanaged
	{
		internal static readonly ComponentInfo Info = RegisterCore<T>();
		internal static readonly ComponentType Type = Info.Type;
	}

	/// <summary>
	/// Describes the identity, size, and guaranteed column-base
	/// alignment of a registered unmanaged component type.
	/// </summary>
	public readonly record struct ComponentInfo(
		ComponentType Type,
		Type ClrType,
		int Size,
		int Alignment);
}
