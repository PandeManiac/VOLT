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

    private readonly Dictionary<Entity, EntityLocation> _locations = [];
    private readonly Dictionary<ComponentSignature, Archetype> _archetypes = [];
    private readonly Dictionary<Archetype, List<ArchetypeChunk>> _chunks = [];
    private readonly Dictionary<Archetype, HashSet<ArchetypeChunk>> _availableChunks = [];

    private bool _disposed;

    private readonly Dictionary<ComponentQuery, List<Archetype>> _queryCache = [];

    private int _structuralVersion;

    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _allocator.Count;
        }
    }

    internal int StructuralVersion
    {
        get
        {
            ThrowIfDisposed();
            return _structuralVersion;
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

            bool wasFull = chunk.IsFull;
            int row = chunk.Add(entity);
            UpdateChunkAvailability(chunk, wasFull);


            _locations.Add(entity, new EntityLocation(chunk, row));
            _structuralVersion++;

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
        bool destroyed = _allocator.Destroy(entity);

        if (destroyed)
        {
            _structuralVersion++;
        }

        return destroyed;
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
        _structuralVersion++;
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
        _structuralVersion++;
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

    /// <summary>
    /// Creates a query over matching, nonempty archetype chunks.
    /// </summary>
    /// <remarks>
    /// Structural changes invalidate active query enumerators.
    /// Component value updates remain valid.
    /// </remarks>
    public WorldQuery Query(ComponentQuery query)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(query);
        return new WorldQuery(this, query);
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
        _availableChunks.Clear();
        _archetypes.Clear();
        _locations.Clear();
        _queryCache.Clear();

        GC.SuppressFinalize(this);
    }

    internal List<Archetype> GetMatchingArchetypes(ComponentQuery query)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(query);

        if (_queryCache.TryGetValue(query, out List<Archetype>? cached))
        {
            return cached;
        }

        List<Archetype> matches = [];

        foreach (Archetype archetype in _archetypes.Values)
        {
            if (query.Matches(archetype))
            {
                matches.Add(archetype);
            }
        }

        _queryCache.Add(query, matches);
        return matches;
    }

    internal List<ArchetypeChunk> GetChunks(Archetype archetype)
    {
        ThrowIfDisposed();
        return _chunks[archetype];
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
            _chunks.Add(archetype, []);
            _availableChunks.Add(archetype, []);

            foreach (KeyValuePair<ComponentQuery, List<Archetype>> entry in _queryCache)
            {
                if (entry.Key.Matches(archetype))
                {
                    entry.Value.Add(archetype);
                }
            }
        }

        return archetype;
    }

    private ArchetypeChunk GetOrCreateAvailableChunk(Archetype archetype)
    {
        HashSet<ArchetypeChunk> available = _availableChunks[archetype];

        foreach (ArchetypeChunk chunk in available)
        {
            return chunk;
        }

        ArchetypeChunk created = new(archetype);

        _chunks[archetype].Add(created);
        available.Add(created);

        return created;
    }


    private void UpdateChunkAvailability(ArchetypeChunk chunk, bool wasFull)
    {
        bool isFull = chunk.IsFull;

        if (wasFull == isFull)
        {
            return;
        }

        HashSet<ArchetypeChunk> available = _availableChunks[chunk.Archetype];

        if (isFull)
        {
            available.Remove(chunk);
        }

        else
        {
            available.Add(chunk);
        }
    }



    private void RemoveFromChunk(EntityLocation location)
    {
        ArchetypeChunk chunk = location.Chunk;
        bool wasFull = chunk.IsFull;

        if (chunk.RemoveAt(location.Row, out Entity movedEntity))
        {
            _locations[movedEntity] = new EntityLocation(chunk, location.Row);
        }

        UpdateChunkAvailability(chunk, wasFull);
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

        bool wasFull = destinationChunk.IsFull;
        int destinationRow = destinationChunk.Add(entity);
        UpdateChunkAvailability(destinationChunk, wasFull);

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
            bool wasFullBeforeRollback = destinationChunk.IsFull;

            destinationChunk.RemoveAt(destinationRow, out _);
            UpdateChunkAvailability(destinationChunk, wasFullBeforeRollback);

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
