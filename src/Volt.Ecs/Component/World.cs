using System.Runtime.CompilerServices;
using Volt.Ecs.Component;

namespace Volt.Ecs;

/// <summary>
/// Owns entity lifetimes and the native archetype storage for one ECS world.
/// Structural operations are not thread-safe.
/// </summary>
public sealed unsafe class World : IDisposable
{
    private readonly EntityAllocator _allocator = new();
    private readonly Dictionary<Entity, EntityLocation> _locations = new();
    private readonly Dictionary<ComponentSignature, Archetype> _archetypes = new();
    private readonly Dictionary<Archetype, List<ArchetypeChunk>> _chunks = new();

    private bool _disposed;

    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _allocator.Count;
        }
    }

    public Entity CreateEntity()
    {
        ThrowIfDisposed();

        Archetype archetype = GetOrCreateArchetype(ComponentSignature.Empty);
        ArchetypeChunk chunk = GetOrCreateAvailableChunk(archetype);
        Entity entity = _allocator.Create();

        try
        {
            int row = chunk.Add(entity);
            _locations.Add(entity, new EntityLocation(chunk, row));
            return entity;
        }

        catch
        {
            _allocator.Destroy(entity);
            throw;
        }
    }

    public bool DestroyEntity(Entity entity)
    {
        ThrowIfDisposed();

        if (!_allocator.IsAlive(entity))
        {
            return false;
        }

        EntityLocation location = _locations[entity];
        RemoveFromChunk(location);

        _locations.Remove(entity);
        return _allocator.Destroy(entity);
    }

    public bool IsAlive(Entity entity)
    {
        ThrowIfDisposed();
        return _allocator.IsAlive(entity);
    }

    public bool HasComponent<T>(Entity entity) where T : unmanaged
    {
        EntityLocation location = GetLocation(entity);
        ComponentType type = ComponentRegistry.Register<T>();
        return location.Chunk.Archetype.FindComponentIndex(type) >= 0;
    }

    public void AddComponent<T>(Entity entity, T value) where T : unmanaged
    {
        EntityLocation source = GetLocation(entity);
        ComponentType type = ComponentRegistry.Register<T>();

        if (source.Chunk.Archetype.FindComponentIndex(type) >= 0)
        {
            throw new InvalidOperationException("The entity already has this component.");
        }

        ComponentSignature destinationSignature = source.Chunk.Archetype.Signature.With(type);
        Migrate(entity, source, destinationSignature, addValue: true, value);
    }

    public bool RemoveComponent<T>(Entity entity) where T : unmanaged
    {
        EntityLocation source = GetLocation(entity);
        ComponentType type = ComponentRegistry.Register<T>();

        if (source.Chunk.Archetype.FindComponentIndex(type) < 0)
        {
            return false;
        }

        ComponentSignature destinationSignature = source.Chunk.Archetype.Signature.Without(type);
        Migrate(entity, source, destinationSignature, addValue: false, default(T));
        return true;
    }

    /// <summary>
    /// Returns a reference that is invalidated by structural changes or disposal.
    /// </summary>
    public ref T GetComponent<T>(Entity entity) where T : unmanaged
    {
        EntityLocation location = GetLocation(entity);
        return ref location.Chunk.GetComponent<T>(location.Row);
    }

    public void SetComponent<T>(Entity entity, T value) where T : unmanaged
    {
        ref T component = ref GetComponent<T>(entity);
        component = value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (List<ArchetypeChunk> chunks in _chunks.Values)
        {
            foreach (ArchetypeChunk chunk in chunks)
            {
                chunk.Dispose();
            }
        }

        _chunks.Clear();
        _archetypes.Clear();
        _locations.Clear();

        GC.SuppressFinalize(this);
    }

    private EntityLocation GetLocation(Entity entity)
    {
        ThrowIfDisposed();

        if (!_allocator.IsAlive(entity))
        {
            throw new ArgumentException("The entity is not alive in this world.", nameof(entity));
        }

        return _locations[entity];
    }

    private Archetype GetOrCreateArchetype(ComponentSignature signature)
    {
        if (!_archetypes.TryGetValue(signature, out Archetype? archetype))
        {
            archetype = new Archetype(signature);
            _archetypes.Add(signature, archetype);
            _chunks.Add(archetype, new List<ArchetypeChunk>());
        }

        return archetype;
    }

    private ArchetypeChunk GetOrCreateAvailableChunk(Archetype archetype)
    {
        List<ArchetypeChunk> chunks = _chunks[archetype];

        foreach (ArchetypeChunk chunk in chunks)
        {
            if (!chunk.IsFull)
            {
                return chunk;
            }
        }

        ArchetypeChunk created = new(archetype);
        chunks.Add(created);
        return created;
    }

    private void RemoveFromChunk(EntityLocation location)
    {
        if (location.Chunk.RemoveAt(location.Row, out Entity movedEntity))
        {
            _locations[movedEntity] = new EntityLocation(location.Chunk, location.Row);
        }
    }

    private void Migrate<T>(
        Entity entity,
        EntityLocation source,
        ComponentSignature destinationSignature,
        bool addValue,
        T value) where T : unmanaged
    {
        Archetype destinationArchetype = GetOrCreateArchetype(destinationSignature);
        ArchetypeChunk destinationChunk = GetOrCreateAvailableChunk(destinationArchetype);

        int destinationRow = destinationChunk.Add(entity);

        try
        {
            Archetype sourceArchetype = source.Chunk.Archetype;

            for (int sourceIndex = 0; sourceIndex < sourceArchetype.ComponentCount; sourceIndex++)
            {
                ComponentRegistry.ComponentInfo info = sourceArchetype.GetComponentInfoAt(sourceIndex);
                int destinationIndex = destinationArchetype.FindComponentIndex(info.Type);

                if (destinationIndex < 0)
                {
                    continue;
                }

                byte* sourcePointer = (byte*)source.Chunk.GetComponentColumnPointer(sourceIndex)
                    + (nuint)source.Row * source.Chunk.GetComponentStride(sourceIndex);

                byte* destinationPointer = (byte*)destinationChunk.GetComponentColumnPointer(destinationIndex)
                    + (nuint)destinationRow * destinationChunk.GetComponentStride(destinationIndex);

                long byteCount = info.Size;
                Buffer.MemoryCopy(sourcePointer, destinationPointer, byteCount, byteCount);
            }

            if (addValue)
            {
                destinationChunk.SetComponent(destinationRow, value);
            }
        }

        catch
        {
            destinationChunk.RemoveAt(destinationRow, out _);
            throw;
        }

        RemoveFromChunk(source);
        _locations[entity] = new EntityLocation(destinationChunk, destinationRow);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
