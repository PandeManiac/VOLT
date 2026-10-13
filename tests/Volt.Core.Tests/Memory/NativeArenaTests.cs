
using Volt.Core.Memory;

namespace Volt.Core.Tests.Memory;

public sealed unsafe class NativeArenaTests
{
    [Fact]
    public void Constructor_OwningArena_HasCorrectState()
    {
        using var arena = new NativeArena(1024);

        Assert.Equal((nuint)1024, arena.Capacity);
        Assert.Equal((nuint)0, arena.Used);
        Assert.Equal((nuint)1024, arena.Remaining);
        Assert.True(arena.OwnsMemory);
        Assert.False(arena.IsDisposed);
    }

    [Fact]
    public void Constructor_ZeroCapacity_IsValid()
    {
        using var arena = new NativeArena(0);

        Assert.Equal((nuint)0, arena.Capacity);
        Assert.True(arena.Allocate<int>(0).IsEmpty);
        Assert.Throws<OutOfMemoryException>(
            () => arena.Allocate<int>(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(24)]
    public void Constructor_InvalidAlignment_Throws(int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NativeArena(1024, (nuint)alignment));
    }

    [Fact]
    public void Constructor_BorrowedArena_DoesNotOwnMemory()
    {
        using var backing = new NativeBuffer<byte>(1024);

        fixed (byte* pointer = backing.AsSpan())
        {
            using var arena = new NativeArena(pointer, 1024);

            Assert.False(arena.OwnsMemory);
            Assert.Equal((nuint)1024, arena.Capacity);

            arena.Allocate<int>(4).Fill(123);
        }

        Assert.False(backing.IsDisposed);
    }

    [Fact]
    public void Constructor_NullPointerWithCapacity_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new NativeArena((void*)null, 128));
    }

    [Fact]
    public void Constructor_NullPointerWithZeroCapacity_IsValid()
    {
        using var arena = new NativeArena((void*)null, 0);

        Assert.Equal((nuint)0, arena.Capacity);
        Assert.True(arena.Allocate<byte>(0).IsEmpty);
    }

    [Fact]
    public void Allocate_AdvancesOffset()
    {
        using var arena = new NativeArena(1024);

        Span<int> values = arena.Allocate<int>(10);

        Assert.Equal(10, values.Length);
        Assert.Equal((nuint)40, arena.Used);
        Assert.Equal((nuint)984, arena.Remaining);
    }

    [Fact]
    public void Allocate_MultipleSpans_DoNotOverlap()
    {
        using var arena = new NativeArena(1024);

        Span<int> first = arena.Allocate<int>(16);
        Span<int> second = arena.Allocate<int>(16);

        first.Fill(111);
        second.Fill(222);

        foreach (int value in first)
        {
            Assert.Equal(111, value);
        }

        foreach (int value in second)
        {
            Assert.Equal(222, value);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void Allocate_RespectsAlignment(int alignment)
    {
        using var arena = new NativeArena(4096);
        arena.Allocate<byte>(3);

        Span<byte> values = arena.Allocate<byte>(
            32, (nuint)alignment);

        fixed (byte* pointer = values)
        {
            Assert.Equal(
                (nuint)0,
                (nuint)pointer % (nuint)alignment);
        }
    }

    [Fact]
    public void Allocate_UnalignedBorrowedPointer_AlignsCorrectly()
    {
        using var backing = new NativeBuffer<byte>(1024);

        fixed (byte* pointer = backing.AsSpan())
        {
            using var arena = new NativeArena(pointer + 1, 1023);
            Span<int> values = arena.Allocate<int>(8, 64);

            fixed (int* result = values)
            {
                Assert.Equal((nuint)0, (nuint)result % 64);
            }

            Assert.True(arena.Used >= (nuint)32);
        }
    }

    [Fact]
    public void Allocate_StructWithNonPowerOfTwoSize_Works()
    {
        using var arena = new NativeArena(1024);
        Span<ThreeFloats> values = arena.Allocate<ThreeFloats>(10);

        values[0] = new ThreeFloats
        {
            X = 1,
            Y = 2,
            Z = 3
        };

        Assert.Equal(1f, values[0].X);
        Assert.Equal(2f, values[0].Y);
        Assert.Equal(3f, values[0].Z);
    }

    [Fact]
    public void Allocate_NegativeCount_Throws()
    {
        using var arena = new NativeArena(1024);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => arena.Allocate<int>(-1));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(24)]
    public void Allocate_InvalidAlignment_Throws(int alignment)
    {
        using var arena = new NativeArena(1024);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => arena.Allocate<int>(4, (nuint)alignment));
    }

    [Fact]
    public void Allocate_ExceedingCapacity_Throws()
    {
        using var arena = new NativeArena(64);

        Assert.Throws<OutOfMemoryException>(
            () => arena.Allocate<byte>(65));
    }

    [Fact]
    public void Allocate_FailedAllocation_DoesNotAdvanceOffset()
    {
        using var arena = new NativeArena(64);
        arena.Allocate<byte>(16);

        nuint before = arena.Used;

        Assert.Throws<OutOfMemoryException>(
            () => arena.Allocate<byte>(1024));

        Assert.Equal(before, arena.Used);
    }

    [Fact]
    public void Allocate_ZeroCount_DoesNotAdvanceOffset()
    {
        using var arena = new NativeArena(1024);
        arena.Allocate<byte>(3);

        nuint before = arena.Used;

        Span<int> result = arena.Allocate<int>(0);

        Assert.True(result.IsEmpty);
        Assert.Equal(before, arena.Used);
    }

    [Fact]
    public void Reset_RewindsArena()
    {
        using var arena = new NativeArena(1024);

        arena.Allocate<int>(100);
        arena.Reset();

        Assert.Equal((nuint)0, arena.Used);
        Assert.Equal(arena.Capacity, arena.Remaining);
    }

    [Fact]
    public void Reset_ReusesMemory()
    {
        using var arena = new NativeArena(1024);
        Span<int> first = arena.Allocate<int>(16);

        nint firstAddress;

        fixed (int* pointer = first)
        {
            firstAddress = (nint)pointer;
        }

        arena.Reset();

        Span<int> second = arena.Allocate<int>(16);

        fixed (int* pointer = second)
        {
            Assert.Equal(firstAddress, (nint)pointer);
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var arena = new NativeArena(1024);

        arena.Dispose();
        arena.Dispose();

        Assert.True(arena.IsDisposed);
    }

    [Fact]
    public void Dispose_BorrowedArena_DoesNotFreeBackingMemory()
    {
        using var backing = new NativeBuffer<byte>(1024);

        fixed (byte* pointer = backing.AsSpan())
        {
            var arena = new NativeArena(pointer, 1024);

            arena.Allocate<byte>(32).Fill(42);
            arena.Dispose();

            Assert.True(arena.IsDisposed);
            Assert.Equal((byte)42, backing[0]);
        }
    }

    [Fact]
    public void Allocate_AfterDispose_Throws()
    {
        var arena = new NativeArena(1024);
        arena.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => arena.Allocate<int>(4));
    }

    [Fact]
    public void Reset_AfterDispose_Throws()
    {
        var arena = new NativeArena(1024);
        arena.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => arena.Reset());
    }

    [Fact]
    public void HotPath_DoesNotAllocateManagedMemory()
    {
        using var arena = new NativeArena(1024 * 1024);

        for (int i = 0; i < 10_000; i++)
        {
            arena.Reset();
            arena.Allocate<int>(16)[0] = i;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
        {
            arena.Reset();
            arena.Allocate<int>(16)[0] = i;
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    private struct ThreeFloats
    {
        public float X;
        public float Y;
        public float Z;
    }
}
