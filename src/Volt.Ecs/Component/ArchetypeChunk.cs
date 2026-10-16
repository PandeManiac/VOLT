
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Volt.Ecs.Component;

/// <summary>
/// Owns a fixed-capacity block of native, column-oriented storage
/// for entities belonging to a single archetype.
/// </summary>
/// <remarks>
/// Component columns are stored in canonical signature order.
/// This class manages storage only; entity insertion and removal
/// are introduced separately.
/// </remarks>
public sealed unsafe class ArchetypeChunk : IDisposable
{
    public const int DefaultChunkSize = 16 * 1024;

    private const nuint MinimumAlignment = 16;

    private readonly nuint[] _componentOffsets;
    private readonly nuint[] _componentStrides;

    private void* _memory;
    private nuint _allocatedBytes;
    private bool _disposed;

    public Archetype Archetype { get; }

    public int ChunkSize { get; }
    public int Capacity { get; }
    public int Count { get; private set; }

    public int ComponentCount => _componentOffsets.Length;

    public bool IsEmpty => Count == 0;
    public bool IsFull => Count == Capacity;

    /// <summary>
    /// Gets the total number of bytes allocated for chunk storage.
    /// </summary>
    public nuint AllocatedBytes
    {
        get
        {
            ThrowIfDisposed();
            return _allocatedBytes;
        }
    }

    /// <summary>
    /// Constructs a chunk whose total native allocation does not
    /// exceed the requested byte budget.
    /// </summary>
    public ArchetypeChunk(
        Archetype archetype,
        int chunkSize = DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(archetype);

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        Archetype = archetype;
        ChunkSize = chunkSize;

        _componentOffsets = new nuint[archetype.ComponentCount];
        _componentStrides = new nuint[archetype.ComponentCount];

        nuint[] alignments = new nuint[archetype.ComponentCount];
        nuint bytesPerEntity = (nuint)Unsafe.SizeOf<Entity>();

        for (int i = 0; i < archetype.ComponentCount; i++)
        {
            ComponentRegistry.ComponentInfo info =
                archetype.GetComponentInfoAt(i);

            if (info.Size <= 0)
            {
                throw new ArgumentException(
                    "Component sizes must be positive.",
                    nameof(archetype));
            }

            nuint alignment = (nuint)info.Alignment;

            if (!IsPowerOfTwo(alignment))
            {
                throw new ArgumentException(
                    "Component alignment must be a power of two.",
                    nameof(archetype));
            }

            alignments[i] = alignment;
            _componentStrides[i] = (nuint)info.Size;

            bytesPerEntity = checked(
                bytesPerEntity + _componentStrides[i]);
        }

        if (bytesPerEntity > (nuint)chunkSize)
        {
            throw new ArgumentException(
                "The chunk budget is too small to store one entity.",
                nameof(chunkSize));
        }

        int upperCapacity = checked(
            (int)((nuint)chunkSize / bytesPerEntity));

        int low = 1;
        int high = upperCapacity;
        int bestCapacity = 0;
        nuint bestSize = 0;

        nuint allocationAlignment = MinimumAlignment;

        foreach (nuint alignment in alignments)
        {
            allocationAlignment = Math.Max(
                allocationAlignment,
                alignment);
        }

        while (low <= high)
        {
            int candidate = low + ((high - low) / 2);

            nuint required = AlignUp(
                CalculateLayout(candidate, alignments, offsets: null),
                allocationAlignment);

            if (required <= (nuint)chunkSize)
            {
                bestCapacity = candidate;
                bestSize = required;
                low = candidate + 1;
            }

            else
            {
                high = candidate - 1;
            }
        }

        if (bestCapacity == 0)
        {
            throw new ArgumentException(
                "The chunk budget cannot accommodate the component layout.",
                nameof(chunkSize));
        }

        Capacity = bestCapacity;

        CalculateLayout(
            Capacity,
            alignments,
            _componentOffsets);

        _allocatedBytes = bestSize;

        _memory = NativeMemory.AlignedAlloc(
            _allocatedBytes,
            allocationAlignment);

        if (_memory == null)
        {
            throw new OutOfMemoryException();
        }

        NativeMemory.Clear(_memory, _allocatedBytes);
    }

    /// <summary>
    /// Gets the offset of a component column from the allocation base.
    /// </summary>
    public nuint GetComponentOffset(int componentIndex)
    {
        ThrowIfDisposed();
        ValidateComponentIndex(componentIndex);

        return _componentOffsets[componentIndex];
    }

    /// <summary>
    /// Gets the byte stride between consecutive component rows.
    /// </summary>
    public nuint GetComponentStride(int componentIndex)
    {
        ThrowIfDisposed();
        ValidateComponentIndex(componentIndex);

        return _componentStrides[componentIndex];
    }

    /// <summary>
    /// Gets the address of the beginning of a component column.
    /// The pointer remains valid until the chunk is disposed.
    /// </summary>
    public void* GetComponentColumnPointer(int componentIndex)
    {
        ThrowIfDisposed();
        ValidateComponentIndex(componentIndex);

        nuint offset = _componentOffsets[componentIndex];
        return (byte*)_memory + offset;
    }

    /// <summary>
    /// Gets the address of the entity handle column.
    /// </summary>
    public Entity* GetEntityColumnPointer()
    {
        ThrowIfDisposed();
        return (Entity*)_memory;
    }


    /// <summary>
    /// Appends an entity and returns its row index.
    /// Component storage for the new row is zero-initialized.
    /// </summary>
    public int Add(Entity entity)
    {
        int row = PrepareRow(entity);

        for (int i = 0; i < ComponentCount; i++)
        {
            byte* destination = GetComponentRowPointerUnchecked(i, row);

            NativeMemory.Clear(
                destination,
                _componentStrides[i]);
        }

        Count++;
        return row;
    }

    // Migration must overwrite every component column before the row is observed.
    internal int AddForMigration(Entity entity)
    {
        int row = PrepareRow(entity);
        Count++;
        return row;
    }

    /// <summary>
    /// Returns the entity stored at a live row.
    /// </summary>
    public Entity GetEntity(int row)
    {
        ThrowIfDisposed();
        ValidateRow(row);
        return GetEntityColumnPointer()[row];
    }

    /// <summary>
    /// Returns a reference to an unmanaged component at a live row.
    /// </summary>
    public ref T GetComponent<T>(int row) where T : unmanaged
    {
        ThrowIfDisposed();
        ValidateRow(row);

        ComponentType type = ComponentRegistry.Get<T>();
        int componentIndex = Archetype.FindComponentIndex(type);

        if (componentIndex < 0)
        {
            throw new ArgumentException(
                $"Component {typeof(T).Name} does not belong to this archetype.");
        }

        byte* pointer = GetComponentRowPointerUnchecked(componentIndex, row);

        return ref Unsafe.AsRef<T>(pointer);
    }

    /// <summary>
    /// Writes an unmanaged component to a live row.
    /// </summary>
    public void SetComponent<T>(int row, T value) where T : unmanaged
    {
        ref T component = ref GetComponent<T>(row);
        component = value;
    }


    /// <summary>
    /// Removes a row by moving the last entity into its position.
    /// Returns true if another entity was moved, and outputs
    /// that entity. Otherwise returns false.
    /// </summary>
    public bool RemoveAt(int row, out Entity movedEntity)
    {
        return RemoveAtCore(row, out movedEntity, clearVacatedStorage: true);
    }

    // Migration overwrites the vacated row before exposing it again.
    internal bool RemoveAtForMigration(int row, out Entity movedEntity)
    {
        return RemoveAtCore(row, out movedEntity, clearVacatedStorage: false);
    }

    private bool RemoveAtCore(
        int row,
        out Entity movedEntity,
        bool clearVacatedStorage)
    {
        ThrowIfDisposed();
        ValidateRow(row);

        int lastRow = Count - 1;
        bool moved = row != lastRow;

        movedEntity = default;

        Entity* entities = GetEntityColumnPointer();

        if (moved)
        {
            movedEntity = entities[lastRow];
            entities[row] = movedEntity;

            for (int i = 0; i < ComponentCount; i++)
            {
                byte* destination = GetComponentRowPointerUnchecked(i, row);
                byte* source = GetComponentRowPointerUnchecked(i, lastRow);

                Buffer.MemoryCopy(
                    source,
                    destination,
                    (long)_componentStrides[i],
                    (long)_componentStrides[i]);
            }
        }

        entities[lastRow] = default;

        if (clearVacatedStorage)
        {
            for (int i = 0; i < ComponentCount; i++)
            {
                byte* pointer = GetComponentRowPointerUnchecked(i, lastRow);

                NativeMemory.Clear(
                    pointer,
                    _componentStrides[i]);
            }
        }

        Count--;
        return moved;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        NativeMemory.AlignedFree(_memory);

        _memory = null;
        _allocatedBytes = 0;
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    ~ArchetypeChunk()
    {
        Dispose();
    }

    private nuint CalculateLayout(
        int capacity,
        nuint[] alignments,
        nuint[]? offsets)
    {
        nuint cursor = checked(
            (nuint)capacity * (nuint)Unsafe.SizeOf<Entity>());

        for (int i = 0; i < _componentStrides.Length; i++)
        {
            cursor = AlignUp(cursor, alignments[i]);
            offsets?[i] = cursor;

            cursor = checked(
                cursor + (nuint)capacity * _componentStrides[i]);
        }

        return cursor;
    }

    private int PrepareRow(Entity entity)
    {
        ThrowIfDisposed();

        if (entity == default)
        {
            throw new ArgumentException(
                "Cannot store an invalid entity.",
                nameof(entity));
        }

        if (IsFull)
        {
            throw new InvalidOperationException(
                "The archetype chunk is full.");
        }

        int row = Count;
        GetEntityColumnPointer()[row] = entity;
        return row;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint AlignUp(nuint value, nuint alignment)
    {
        return checked(value + alignment - 1) & ~(alignment - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPowerOfTwo(nuint value)
    {
        return value != 0 && (value & (value - 1)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateComponentIndex(int componentIndex)
    {
        if ((uint)componentIndex >= (uint)ComponentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(componentIndex));
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateRow(int row)
    {
        if ((uint)row >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
    }

    // Caller must validate the component index and row against this chunk.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal byte* GetComponentRowPointerUnchecked(int componentIndex, int row)
    {
        return (byte*)_memory
            + _componentOffsets[componentIndex]
            + (nuint)row * _componentStrides[componentIndex];
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
