using System.Runtime.CompilerServices;
using System.Threading;
using Volt.Ecs.Component;

namespace Volt.Ecs;

/// <summary>
/// Owns entity lifetimes and the native archetype storage for one ECS world.
/// Structural mutations are serialized and rejected during structural freezes.
/// Concurrent component access requires separate synchronization.
/// </summary>
public sealed unsafe class World : IDisposable
{
	private readonly EntityAllocator _allocator = new();

	private readonly Dictionary<Entity, EntityLocation> _locations = [];
	private readonly Dictionary<ComponentSignature, Archetype> _archetypes = [];
	private readonly Dictionary<Archetype, List<ArchetypeChunk>> _chunks = [];
	private readonly Dictionary<Archetype, HashSet<ArchetypeChunk>> _availableChunks = [];
	private readonly Dictionary<(Archetype Source, Archetype Destination), ComponentCopy[]> _migrationPlans = [];
	private readonly Dictionary<(Archetype Source, ComponentType Component, bool Add), Archetype> _transitionArchetypes = [];

	private readonly record struct ComponentCopy(
		int SourceIndex,
		int DestinationIndex,
		int Size);

	public sealed class StructuralFreezeScope : IDisposable
	{
		private World? _world;

		internal StructuralFreezeScope(World world)
		{
			_world = world;
		}

		public void Dispose()
		{
			World? world = Interlocked.Exchange(ref _world, null);
			world?.EndStructuralFreeze();
		}
	}

	private bool _disposed;

	private readonly Dictionary<ComponentQuery, List<Archetype>> _queryCache = [];

	private int _structuralVersion;

	/// <summary>
	/// Prevents structural changes while component storage is
	/// being accessed by concurrent workers.
	/// </summary>
	/// <remarks>
	/// Callers must complete all work using the protected storage
	/// before disposing the returned scope.
	///
	/// This does not synchronize conflicting component writes.
	/// </remarks>
	public StructuralFreezeScope FreezeStructure()
	{
		lock (_structureGate)
		{
			ThrowIfDisposed();

			StructuralFreezeScope scope = new(this);

			_activeStructuralFreezes = checked(
				_activeStructuralFreezes + 1);

			return scope;
		}
	}

	private readonly object _structureGate = new();
	private int _activeStructuralFreezes;

	public int Count
	{
		get
		{
			ThrowIfDisposed();
			return _allocator.Count;
		}
	}

	/// <summary>
	/// Gets the identity of the allocator that issued this world's
	/// entity handles.
	/// </summary>
	/// <remarks>
	/// Optional subsystems that store data beside entity slots use this
	/// to reject handles issued by another world without asking the
	/// world on every access.
	/// </remarks>
	public uint AllocatorId
	{
		get
		{
			ThrowIfDisposed();
			return _allocator.AllocatorId;
		}
	}

	/// <summary>
	/// Occurs after an entity has been destroyed and removed from
	/// storage.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The event fires once per successful
	/// <see cref="DestroyEntity"/>, outside the structural lock, so a
	/// handler may safely query or mutate the world. It does not fire
	/// for a handle that was already dead.
	/// </para>
	/// <para>
	/// Optional subsystems use this to release per-entity state.
	/// With no subscribers the only cost is one null check per
	/// destruction; the event is not part of per-frame execution.
	/// </para>
	/// <para>
	/// An exception thrown by a handler propagates to the caller
	/// after the destruction has been committed, because the world
	/// does not hide failures in user code.
	/// </para>
	/// </remarks>
	public event Action<Entity>? EntityDestroyed;

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
		using StructuralMutationScope scope = EnterStructuralMutation();
		ThrowIfDisposed();

		Archetype archetype = GetOrCreateArchetype(ComponentSignature.Empty);
		ArchetypeChunk chunk = GetOrCreateAvailableChunk(archetype);
		Entity entity = _allocator.Create();

		bool appended = false;

		try
		{
			bool wasFull = chunk.IsFull;
			int row = chunk.Add(entity);
			appended = true;
			UpdateChunkAvailability(chunk, wasFull);

			_locations.Add(entity, new EntityLocation(chunk, row));
			_structuralVersion++;

			return entity;
		}

		catch
		{
			try
			{
				if (appended)
				{
					bool wasFull = chunk.IsFull;
					chunk.RemoveAt(chunk.Count - 1, out _);
					UpdateChunkAvailability(chunk, wasFull);
				}
			}
			finally
			{
				_allocator.Destroy(entity);
			}

			throw;
		}
	}

	public bool DestroyEntity(Entity entity)
	{
		bool destroyed;

		using (StructuralMutationScope scope = EnterStructuralMutation())
		{
			ThrowIfDisposed();

			if (!_allocator.IsAlive(entity))
			{
				return false;
			}

			EntityLocation location = _locations[entity];
			RemoveFromChunk(location);

			_locations.Remove(entity);
			destroyed = _allocator.Destroy(entity);

			if (destroyed)
			{
				_structuralVersion++;
			}
		}

		// The destruction is committed before user code runs, so a
		// handler may create, destroy, or query entities.
		if (destroyed)
		{
			EntityDestroyed?.Invoke(entity);
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
		using StructuralMutationScope scope = EnterStructuralMutation();

		EntityLocation source = GetLocation(entity);
		ComponentType type = ComponentRegistry.Register<T>();

		if (source.Chunk.Archetype.FindComponentIndex(type) >= 0)
		{
			throw new InvalidOperationException("The entity already has this component.");
		}

		Migrate(entity, source, type, addValue: true, value);
		_structuralVersion++;
	}

	public bool RemoveComponent<T>(Entity entity) where T : unmanaged
	{
		using StructuralMutationScope scope = EnterStructuralMutation();

		EntityLocation source = GetLocation(entity);
		ComponentType type = ComponentRegistry.Register<T>();

		if (source.Chunk.Archetype.FindComponentIndex(type) < 0)
		{
			return false;
		}

		Migrate(entity, source, type, addValue: false, default(T));
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
	/// Attempts to read a component value without throwing when the
	/// entity or the component is absent.
	/// </summary>
	/// <remarks>
	/// This is the read-only shape a debugger or inspector needs: a
	/// stale selection is an ordinary result rather than an exception
	/// on a per-frame path. The value is copied, so the caller never
	/// holds a reference into chunk storage that a structural change
	/// could invalidate.
	/// </remarks>
	public bool TryReadComponent<T>(Entity entity, out T value)
		where T : unmanaged
	{
		ThrowIfDisposed();

		if (!_locations.TryGetValue(entity, out EntityLocation location))
		{
			value = default;
			return false;
		}

		ComponentType type = ComponentRegistry.Register<T>();

		if (location.Chunk.Archetype.FindComponentIndex(type) < 0)
		{
			value = default;
			return false;
		}

		value = location.Chunk.GetComponent<T>(location.Row);
		return true;
	}

	/// <summary>
	/// Attempts to read the component signature of a live entity.
	/// </summary>
	public bool TryGetSignature(
		Entity entity,
		out ComponentSignature signature)
	{
		ThrowIfDisposed();

		if (!_locations.TryGetValue(entity, out EntityLocation location))
		{
			signature = ComponentSignature.Empty;
			return false;
		}

		signature = location.Chunk.Archetype.Signature;
		return true;
	}

	/// <summary>
	/// Gets a snapshot of the archetypes this world has allocated.
	/// </summary>
	/// <remarks>
	/// The list is a copy; the archetypes it names are live. Read their
	/// counts from the thread that owns the simulation, because a
	/// structural change on another thread would produce a torn view
	/// rather than a consistent one. Nothing here keeps the caller
	/// inside chunk storage.
	/// </remarks>
	public IReadOnlyList<Archetype> GetArchetypes()
	{
		ThrowIfDisposed();

		lock (_structureGate)
		{
			return [.. _archetypes.Values];
		}
	}

	/// <summary>
	/// Measures one archetype's chunks, capacity, and native allocation.
	/// </summary>
	public bool TryGetArchetypeStorage(
		Archetype archetype,
		out ArchetypeStorageInfo storage)
	{
		ArgumentNullException.ThrowIfNull(archetype);
		ThrowIfDisposed();

		lock (_structureGate)
		{
			if (!_chunks.TryGetValue(archetype, out List<ArchetypeChunk>? chunks))
			{
				storage = default;
				return false;
			}

			int entities = 0;
			int capacity = 0;
			long bytes = 0;

			foreach (ArchetypeChunk chunk in chunks)
			{
				entities += chunk.Count;
				capacity += chunk.Capacity;
				bytes += (long)chunk.AllocatedBytes;
			}

			storage = new ArchetypeStorageInfo(
				archetype,
				chunks.Count,
				entities,
				capacity,
				bytes);

			return true;
		}
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
		using StructuralMutationScope scope = EnterStructuralMutation();

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
		_migrationPlans.Clear();
		_transitionArchetypes.Clear();

		GC.SuppressFinalize(this);
	}

	internal List<Archetype> GetMatchingArchetypes(ComponentQuery query)
	{
		lock (_structureGate)
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
	}

	internal List<ArchetypeChunk> GetChunks(Archetype archetype)
	{
		ThrowIfDisposed();
		return _chunks[archetype];
	}

	private EntityLocation GetLocation(Entity entity)
	{
		ThrowIfDisposed();

		if (!_locations.TryGetValue(entity, out EntityLocation location))
		{
			throw new ArgumentException("The entity is not alive in this world.", nameof(entity));
		}

		return location;
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

	private void RemoveFromChunk(
		EntityLocation location,
		bool clearVacatedStorage = true)
	{
		ArchetypeChunk chunk = location.Chunk;
		bool wasFull = chunk.IsFull;
		Entity movedEntity;
		bool moved;

		if (clearVacatedStorage)
		{
			moved = chunk.RemoveAt(location.Row, out movedEntity);
		}

		else
		{
			moved = chunk.RemoveAtForMigration(location.Row, out movedEntity);
		}

		if (moved)
		{
			_locations[movedEntity] = new EntityLocation(chunk, location.Row);
		}

		UpdateChunkAvailability(chunk, wasFull);
	}

	private ComponentCopy[] GetMigrationPlan(
		Archetype source,
		Archetype destination)
	{
		var key = (Source: source, Destination: destination);

		if (_migrationPlans.TryGetValue(key, out ComponentCopy[]? plan))
		{
			return plan;
		}

		List<ComponentCopy> copies = [];

		int sourceIndex = 0;
		int destinationIndex = 0;

		while (sourceIndex < source.ComponentCount
				&& destinationIndex < destination.ComponentCount)
		{
			ComponentRegistry.ComponentInfo sourceInfo =
				source.GetComponentInfoAt(sourceIndex);

			ComponentRegistry.ComponentInfo destinationInfo =
				destination.GetComponentInfoAt(destinationIndex);

			uint sourceId = sourceInfo.Type.Id;
			uint destinationId = destinationInfo.Type.Id;

			if (sourceId == destinationId)
			{
				copies.Add(new ComponentCopy(
					sourceIndex,
					destinationIndex,
					sourceInfo.Size));

				sourceIndex++;
				destinationIndex++;
			}

			else if (sourceId < destinationId)
			{
				sourceIndex++;
			}

			else
			{
				destinationIndex++;
			}
		}

		plan = [.. copies];
		_migrationPlans.Add(key, plan);

		return plan;
	}

	private Archetype GetTransitionArchetype(
		Archetype source,
		ComponentType component,
		bool add)
	{
		var key = (Source: source, Component: component, Add: add);

		if (_transitionArchetypes.TryGetValue(key, out Archetype? destination))
		{
			return destination;
		}

		ComponentSignature signature = add
			? source.Signature.With(component)
			: source.Signature.Without(component);

		destination = GetOrCreateArchetype(signature);
		_transitionArchetypes.Add(key, destination);
		return destination;
	}

	private void Migrate<T>(
		Entity entity,
		EntityLocation source,
		ComponentType component,
		bool addValue,
		T value) where T : unmanaged
	{
		Archetype sourceArchetype = source.Chunk.Archetype;
		Archetype destinationArchetype = GetTransitionArchetype(
			sourceArchetype,
			component,
			addValue);
		ArchetypeChunk destinationChunk = GetOrCreateAvailableChunk(destinationArchetype);

		bool wasFull = destinationChunk.IsFull;
		int destinationRow = destinationChunk.AddForMigration(entity);

		try
		{
			UpdateChunkAvailability(destinationChunk, wasFull);
			ComponentCopy[] plan = GetMigrationPlan(
				sourceArchetype,
				destinationArchetype);

			foreach (ComponentCopy copy in plan)
			{
				byte* sourcePointer = source.Chunk.GetComponentRowPointerUnchecked(
					copy.SourceIndex,
					source.Row);

				byte* destinationPointer = destinationChunk.GetComponentRowPointerUnchecked(
					copy.DestinationIndex,
					destinationRow);

				Buffer.MemoryCopy(
					sourcePointer,
					destinationPointer,
					copy.Size,
					copy.Size);
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

		RemoveFromChunk(source, clearVacatedStorage: false);
		_locations[entity] = new EntityLocation(destinationChunk, destinationRow);
	}

	private readonly struct StructuralMutationScope(object gate) : IDisposable
	{
		private readonly object _gate = gate;

		public void Dispose()
		{
			Monitor.Exit(_gate);
		}
	}

	private StructuralMutationScope EnterStructuralMutation()
	{
		Monitor.Enter(_structureGate);

		if (_activeStructuralFreezes != 0)
		{
			Monitor.Exit(_structureGate);

			throw new InvalidOperationException(
				"Structural changes are not permitted while " +
				"the world's structure is frozen.");
		}

		return new StructuralMutationScope(_structureGate);
	}

	private void EndStructuralFreeze()
	{
		lock (_structureGate)
		{
			if (_activeStructuralFreezes == 0)
			{
				throw new InvalidOperationException("No structural freeze is active.");
			}

			_activeStructuralFreezes--;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
