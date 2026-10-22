
using System.Runtime.CompilerServices;

namespace Volt.Ecs.Component;

/// <summary>
/// Provides contiguous access to entities and components
/// stored in a matching archetype chunk.
/// </summary>
/// <remarks>
/// This is a non-owning view. Structural changes to the
/// owning world may invalidate the view or its spans.
/// </remarks>
public readonly unsafe struct WorldQueryChunk
{
	private readonly ArchetypeChunk _chunk;

	public int Count => _chunk.Count;
	public Archetype Archetype => _chunk.Archetype;

	internal WorldQueryChunk(ArchetypeChunk chunk)
	{
		ArgumentNullException.ThrowIfNull(chunk);
		_chunk = chunk;
	}

	/// <summary>
	/// Returns the entities stored in this chunk.
	/// </summary>
	public ReadOnlySpan<Entity> GetEntities()
	{
		Entity* pointer = _chunk.GetEntityColumnPointer();
		return new ReadOnlySpan<Entity>(pointer, _chunk.Count);
	}

	/// <summary>
	/// Returns contiguous, mutable component storage.
	/// </summary>
	/// <typeparam name="T">
	/// The unmanaged component type to access.
	/// </typeparam>
	public Span<T> GetSpan<T>() where T : unmanaged
	{
		ComponentType type = ComponentRegistry.Get<T>();

		int componentIndex =
			_chunk.Archetype.FindComponentIndex(type);

		if (componentIndex < 0)
		{
			throw new ArgumentException(
				$"Component {typeof(T).Name} does not belong to this archetype.");
		}

		void* pointer =
			_chunk.GetComponentColumnPointer(componentIndex);

		nuint stride =
			_chunk.GetComponentStride(componentIndex);

		if (stride != (nuint)Unsafe.SizeOf<T>())
		{
			throw new InvalidOperationException(
				"Component storage stride does not match the requested type.");
		}

		return new Span<T>(pointer, _chunk.Count);
	}
}
