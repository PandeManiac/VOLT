using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Volt.Core.Memory;

/// <summary>Owns a fixed-capacity, aligned block of unmanaged memory.</summary>
/// <remarks>
/// Memory is uninitialized after allocation.
/// Disposal is not thread-safe. All operations accessing this buffer,
/// including outstanding spans and references, must finish before disposal.
/// </remarks>
public sealed unsafe class NativeBuffer<T> : IDisposable where T : unmanaged
{
    private T* _pointer;
    private bool _disposed;

    public int Length { get; }
    public nuint Alignment { get; }
    public bool IsDisposed => _disposed;

    public NativeBuffer(int length, nuint alignment = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        if (alignment == 0
            || (alignment & (alignment - 1)) != 0
            || alignment < (nuint)IntPtr.Size)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment),
                "Alignment must be a power of two and at least pointer-sized.");
        }

        Length = length;
        Alignment = alignment;

        if (length == 0)
        {
            return;
        }

        nuint bytes = checked((nuint)length * (nuint)sizeof(T));
        _pointer = (T*)NativeMemory.AlignedAlloc(bytes, alignment);

        if (_pointer == null)
        {
            throw new OutOfMemoryException();
        }
    }

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if ((uint)index >= (uint)Length)
            {
                throw new IndexOutOfRangeException();
            }

            return ref _pointer[index];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new Span<T>(_pointer, Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        AsSpan().Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        T* pointer = _pointer;
        _pointer = null;

        if (pointer != null)
        {
            NativeMemory.AlignedFree(pointer);
        }
    }
}
