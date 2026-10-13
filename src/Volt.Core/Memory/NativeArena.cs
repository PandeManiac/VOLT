using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Volt.Core.Memory;

/// <summary>A checkpoint belonging to a particular NativeArena.</summary>
public readonly struct NativeArenaMark
{
    internal readonly object? Token;
    internal NativeArenaMark(object token) => Token = token;
}

/// <summary>A fixed-capacity, single-threaded unmanaged bump allocator.</summary>
/// <remarks>
/// Borrowed memory must remain valid for the arena lifetime. Spans and pointers
/// become logically invalid after the region is rewound, reset, or disposed.
/// Mark() creates a managed checkpoint object; Allocate/TryAllocate do not allocate
/// managed memory on their successful or capacity-exhaustion paths.
/// </remarks>
public sealed unsafe class NativeArena : IDisposable
{
    private sealed class Checkpoint
    {
        internal readonly NativeArena Owner;
        internal readonly nuint Offset;
        internal readonly Checkpoint? Previous;
        internal bool Valid = true;

        internal Checkpoint(NativeArena owner, nuint offset, Checkpoint? previous)
        {
            Owner = owner;
            Offset = offset;
            Previous = previous;
        }
    }

    private byte* _memory;
    private nuint _offset;
    private readonly bool _ownsMemory;
    private bool _disposed;
    private Checkpoint? _latest;

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

    public NativeArena(void* memory, nuint capacity)
    {
        if (capacity != 0 && memory == null)
        {
            throw new ArgumentNullException(nameof(memory));
        }

        _memory = (byte*)memory;
        Capacity = capacity;
    }

    /// <summary>Captures the current offset. Allocates one managed checkpoint object.</summary>
    public NativeArenaMark Mark()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _latest = new Checkpoint(this, _offset, _latest);
        return new NativeArenaMark(_latest);
    }

    /// <summary>Rewinds to a valid mark, invalidating newer marks.</summary>
    public void Rewind(NativeArenaMark mark)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (mark.Token is not Checkpoint target
            || !ReferenceEquals(target.Owner, this)
            || !target.Valid
            || target.Offset > _offset)
        {
            throw new ArgumentException("Invalid or stale arena mark.", nameof(mark));
        }

        Checkpoint? node = _latest;

        while (node != null && !ReferenceEquals(node, target))
        {
            node.Valid = false;
            node = node.Previous;
        }

        if (node == null)
        {
            throw new ArgumentException("Arena mark is no longer reachable.", nameof(mark));
        }

        _latest = target;
        _offset = target.Offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> Allocate<T>(int count, nuint alignment = 0) where T : unmanaged
    {
        if (TryAllocate(count, out Span<T> result, alignment))
        {
            return result;
        }

        throw new OutOfMemoryException("NativeArena capacity exceeded.");
    }

    /// <summary>Returns false only for insufficient capacity, leaving Used unchanged.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAllocate<T>(int count, out Span<T> result, nuint alignment = 0)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (alignment == 0)
        {
            alignment = 16;
        }

        ValidateAlignment(alignment);
        result = [];

        if (count == 0)
        {
            return true;
        }

        nuint bytes = checked((nuint)count * (nuint)sizeof(T));
        nuint current = checked((nuint)_memory + _offset);
        nuint mask = alignment - 1;
        nuint padding = (alignment - (current & mask)) & mask;
        nuint remaining = Capacity - _offset;

        if (padding > remaining || bytes > remaining - padding)
        {
            return false;
        }

        nuint address = checked(current + padding);
        nuint nextOffset = checked(_offset + padding + bytes);

        result = new Span<T>((void*)address, count);
        _offset = nextOffset;

        return true;
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        InvalidateMarks();
        _offset = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        InvalidateMarks();

        byte* memory = _memory;

        _memory = null;
        _offset = 0;

        if (_ownsMemory && memory != null)
        {
            NativeMemory.AlignedFree(memory);
        }
    }

    private void InvalidateMarks()
    {
        for (Checkpoint? node = _latest; node != null; node = node.Previous)
        {
            node.Valid = false;
        }

        _latest = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateAlignment(nuint alignment)
    {
        if (alignment == 0 || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment),
                "Alignment must be a nonzero power of two.");
        }
    }
}
