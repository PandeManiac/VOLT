using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using Volt.Core.Memory;

namespace Volt.Benchmarks;

[DisassemblyDiagnoser(maxDepth: 3, printSource: true)]
[MemoryDiagnoser]
public unsafe class NativeArenaBenchmarks
{
    private NativeArena _arena = null!;
    private byte* _native;

    [Params(16, 64, 256, 4096)]
    public int Bytes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _arena = new NativeArena(1024 * 1024);
        _native = (byte*)NativeMemory.AlignedAlloc((nuint)Bytes, 64);

        if (_native == null)
        {
            throw new OutOfMemoryException();
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _arena.Dispose();
        NativeMemory.AlignedFree(_native);
    }

    [Benchmark(Baseline = true)]
    public byte ArenaAllocate()
    {
        _arena.Reset();
        Span<byte> span = _arena.Allocate<byte>(Bytes);
        span[0] = 42;
        return span[0];
    }

    [Benchmark]
    public byte ArenaTryAllocate()
    {
        _arena.Reset();

        if (!_arena.TryAllocate<byte>(Bytes, out Span<byte> span))
        {
            throw new InvalidOperationException();
        }

        span[0] = 42;
        return span[0];
    }

    [Benchmark]
    public byte ArenaMarkRewind()
    {
        _arena.Reset();

        NativeArenaMark mark = _arena.Mark();
        Span<byte> span = _arena.Allocate<byte>(Bytes);

        span[0] = 42;
        byte value = span[0];

        _arena.Rewind(mark);
        return value;
    }

    [Benchmark]
    public byte NativeMemoryAllocateFree()
    {
        byte* ptr = (byte*)NativeMemory.AlignedAlloc((nuint)Bytes, 64);

        if (ptr == null)
        {
            throw new OutOfMemoryException();
        }

        ptr[0] = 42;
        byte value = ptr[0];

        NativeMemory.AlignedFree(ptr);
        return value;
    }

    [Benchmark]
    public byte NativeBufferAllocateDispose()
    {
        using var buffer = new NativeBuffer<byte>(Bytes);
        buffer[0] = 42;
        return buffer[0];
    }

    [Benchmark]
    public byte[] ManagedArrayAllocate()
    {
        byte[] bytes = new byte[Bytes];
        bytes[0] = 42;
        return bytes;
    }
}
