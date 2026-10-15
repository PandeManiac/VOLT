using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Volt.Core.Memory;

/// <summary>A checkpoint belonging to a particular NativeArena.</summary>
public readonly struct NativeArenaMark
{
    internal readonly NativeArena? Owner;
    internal readonly ulong Id;
    internal readonly int Index;

    internal NativeArenaMark(NativeArena owner, ulong id, int index)
    {
        Owner = owner;
        Id = id;
        Index = index;
    }
}

/// <summary>A fixed-capacity, single-threaded unmanaged bump allocator.</summary>
/// <remarks>
/// Borrowed memory must remain valid for the arena lifetime. Spans and pointers
/// become logically invalid after the region is rewound, reset, or disposed.
/// Mark/Rewind do not allocate per checkpoint. Mark may allocate when its
/// reusable checkpoint storage first initializes or needs to grow.
/// </remarks>
public sealed unsafe class NativeArena : IDisposable
{
    private struct Checkpoint
    {
        internal readonly nuint Offset;
        internal readonly ulong Id;

        internal Checkpoint(nuint offset, ulong id)
        {
            Offset = offset;
            Id = id;
        }
    }

    private const int InitialCheckpointCapacity = 16;

    private readonly bool _ownsMemory;
    private byte* _memory;
    private nuint _offset;
    private Checkpoint[]? _checkpoints;
    private int _checkpointCount;
    private ulong _nextCheckpointId;
    private bool _disposed;

    public nuint Capacity { get; }
    public nuint Used => _offset;
    public nuint Remaining => Capacity - _offset;
    public bool OwnsMemory => _ownsMemory;
    public bool IsDisposed => _disposed;

    public NativeArena(nuint capacity, nuint alignment = 64)
    {
        ValidateAlignment(alignment);

        Capacity = capacity;
        _ownsMemory = true;

        if (capacity == 0)
        {
            return;
        }

        _memory = (byte*)NativeMemory.AlignedAlloc(capacity, alignment);

        if (_memory == null)
        {
            throw new OutOfMemoryException();
        }
    }

    /// <summary>Creates an arena over caller-owned unmanaged memory.</summary>
    /// <remarks>
    /// The caller must provide a valid, addressable region of at least
    /// capacity bytes. The region must remain allocated for the entire
    /// arena lifetime. This arena does not free the supplied memory.
    /// </remarks>
    public NativeArena(void* memory, nuint capacity)
    {
        if (capacity != 0 && memory == null)
        {
            throw new ArgumentNullException(nameof(memory));
        }

        _memory = (byte*)memory;
        Capacity = capacity;
    }

    /// <summary>
    /// Captures the current offset. Does not allocate a checkpoint object.
    /// May grow reusable checkpoint storage.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NativeArenaMark Mark()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_checkpointCount == int.MaxValue)
        {
            throw new InvalidOperationException("Too many active arena marks.");
        }

        ulong id = unchecked(_nextCheckpointId + 1);

        if (id == 0)
        {
            throw new InvalidOperationException("Arena checkpoint IDs exhausted.");
        }

        Checkpoint[] checkpoints = _checkpoints ??= new Checkpoint[InitialCheckpointCapacity];

        if (_checkpointCount == checkpoints.Length)
        {
            Array.Resize(ref checkpoints, checked(checkpoints.Length * 2));
            _checkpoints = checkpoints;
        }

        int index = _checkpointCount;
        checkpoints[index] = new Checkpoint(_offset, id);
        _checkpointCount = index + 1;
        _nextCheckpointId = id;

        return new NativeArenaMark(this, id, index);
    }

    /// <summary>
    /// Rewinds to a valid mark and invalidates all newer marks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Rewind(NativeArenaMark mark)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ReferenceEquals(mark.Owner, this)
            || (uint)mark.Index >= (uint)_checkpointCount
            || _checkpoints![mark.Index].Id != mark.Id
            || _checkpoints[mark.Index].Offset > _offset)
        {
            throw new ArgumentException(
                "Invalid or stale arena mark.",
                nameof(mark));
        }

        _offset = _checkpoints[mark.Index].Offset;
        _checkpointCount = mark.Index + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Allocate<T>(
        int count,
        nuint alignment = 0)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (alignment == 0)
        {
            alignment = 16;
        }
        else
        {
            ValidateAlignment(alignment);
        }

        if (count == 0)
        {
            return [];
        }

        nuint bytes = unchecked((nuint)count * (nuint)sizeof(T));
        nuint current = unchecked((nuint)_memory + _offset);
        nuint mask = alignment - 1;
        nuint padding = unchecked((nuint)0 - current) & mask;
        nuint remaining = Capacity - _offset;

        if (padding > remaining || bytes > remaining - padding)
        {
            throw new OutOfMemoryException("NativeArena capacity exceeded.");
        }

        nuint address = unchecked(current + padding);
        Span<T> result = new((void*)address, count);
        _offset = unchecked(_offset + padding + bytes);
        return result;
    }

    /// <summary>
    /// Returns false only for insufficient capacity, leaving Used unchanged.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAllocate<T>(
        int count,
        out Span<T> result,
        nuint alignment = 0)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (alignment == 0)
        {
            alignment = 16;
        }
        else
        {
            ValidateAlignment(alignment);
        }

        if (count == 0)
        {
            result = [];
            return true;
        }

        nuint bytes = unchecked((nuint)count * (nuint)sizeof(T));
        nuint current = unchecked((nuint)_memory + _offset);
        nuint mask = alignment - 1;
        nuint padding = unchecked((nuint)0 - current) & mask;
        nuint remaining = Capacity - _offset;

        if (padding > remaining || bytes > remaining - padding)
        {
            result = [];
            return false;
        }

        nuint address = unchecked(current + padding);
        result = new Span<T>((void*)address, count);
        _offset = unchecked(_offset + padding + bytes);
        return true;
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _checkpointCount = 0;
        _offset = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        byte* memory = _memory;
        _memory = null;
        _offset = 0;
        _checkpointCount = 0;
        _checkpoints = null;

        if (_ownsMemory && memory != null)
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateAlignment(nuint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                "Alignment must be a nonzero power of two.");
        }
    }
}
