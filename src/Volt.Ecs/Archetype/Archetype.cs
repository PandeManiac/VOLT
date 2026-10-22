
namespace Volt.Ecs.Component;

/// <summary>
/// Describes an archetype containing entities with an identical
/// set of component types.
/// </summary>
/// <remarks>
/// An archetype owns its component signature and the metadata
/// required to construct its storage layout.
///
/// Physical entity and component storage will be implemented
/// by ArchetypeChunk.
/// </remarks>
public sealed class Archetype
{
	private readonly ComponentRegistry.ComponentInfo[] _components;

	/// <summary>
	/// Gets the canonical component signature for this archetype.
	/// </summary>
	public ComponentSignature Signature { get; }

	/// <summary>
	/// Gets the number of component types in this archetype.
	/// </summary>
	public int ComponentCount => _components.Length;

	/// <summary>
	/// Gets the number of entities currently stored in this archetype.
	/// </summary>
	public int EntityCount { get; private set; }

	/// <summary>
	/// Gets the number of allocated storage chunks.
	/// </summary>
	public int ChunkCount { get; private set; }

	/// <summary>
	/// Creates an archetype for the specified component signature.
	/// All component types must be registered.
	/// </summary>
	public Archetype(ComponentSignature signature)
	{
		ArgumentNullException.ThrowIfNull(signature);

		Signature = signature;
		_components = new ComponentRegistry.ComponentInfo[signature.Count];

		for (int i = 0; i < signature.Count; i++)
		{
			_components[i] = ComponentRegistry.GetInfo(signature[i]);
		}
	}

	/// <summary>
	/// Returns whether this archetype contains the specified component.
	/// </summary>
	public bool Contains(ComponentType type)
	{
		return Signature.Contains(type);
	}

	/// <summary>
	/// Returns whether this archetype contains every component
	/// in the specified signature.
	/// </summary>
	public bool ContainsAll(ComponentSignature signature)
	{
		return Signature.ContainsAll(signature);
	}

	/// <summary>
	/// Returns whether this archetype contains any component
	/// in the specified signature.
	/// </summary>
	public bool ContainsAny(ComponentSignature signature)
	{
		return Signature.ContainsAny(signature);
	}

	/// <summary>
	/// Gets the registered metadata for a component type.
	/// </summary>
	public ComponentRegistry.ComponentInfo GetComponentInfo(ComponentType type)
	{
		int index = FindComponentIndex(type);

		if (index < 0)
		{
			throw new ArgumentException(
				"The component type does not belong to this archetype.",
				nameof(type));
		}

		return _components[index];
	}

	/// <summary>
	/// Attempts to retrieve metadata for a component type.
	/// </summary>
	public bool TryGetComponentInfo(
		ComponentType type,
		out ComponentRegistry.ComponentInfo info)
	{
		int index = FindComponentIndex(type);

		if (index < 0)
		{
			info = default;
			return false;
		}

		info = _components[index];
		return true;
	}

	/// <summary>
	/// Gets component metadata by its position in the canonical
	/// component ordering.
	/// </summary>
	public ComponentRegistry.ComponentInfo GetComponentInfoAt(int index)
	{
		return _components[index];
	}

	/// <summary>
	/// Finds the canonical index of a component type.
	/// Returns -1 when the component is absent.
	/// </summary>
	public int FindComponentIndex(ComponentType type)
	{
		int left = 0;
		int right = _components.Length - 1;

		while (left <= right)
		{
			int middle = left + ((right - left) / 2);
			uint current = _components[middle].Type.Id;

			if (current == type.Id)
			{
				return middle;
			}

			if (current < type.Id)
			{
				left = middle + 1;
			}

			else
			{
				right = middle - 1;
			}
		}

		return -1;
	}
}
