using Volt.Core.Memory;

namespace Volt.Core.Tests.Memory;

public sealed unsafe class NativeArenaCheckpointTests
{
    [Fact]
    public void MarkAndRewind_RestoresOffsetAndMemory()
    {
        using var arena = new NativeArena(1024);
        arena.Allocate<byte>(16);

        NativeArenaMark mark = arena.Mark();
        Span<int> original = arena.Allocate<int>(8);

        nint address;

        fixed (int* pointer = original)
        {
            address = (nint)pointer;
        }

        arena.Rewind(mark);
        Assert.Equal((nuint)16, arena.Used);

        Span<int> reused = arena.Allocate<int>(8);

        fixed (int* pointer = reused)
        {
            Assert.Equal(address, (nint)pointer);
        }
    }

    [Fact]
    public void Rewind_NestedMarks_PreservesOlderMarks()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark first = arena.Mark();
        arena.Allocate<int>(4);
        NativeArenaMark second = arena.Mark();
        arena.Allocate<int>(4);

        arena.Rewind(second);
        Assert.Equal((nuint)16, arena.Used);

        arena.Rewind(first);
        Assert.Equal((nuint)0, arena.Used);
    }

    [Fact]
    public void Rewind_InvalidatesDiscardedBranchEvenWhenOffsetIsReused()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark first = arena.Mark();
        arena.Allocate<int>(4);
        NativeArenaMark stale = arena.Mark();

        arena.Rewind(first);
        arena.Allocate<int>(4);

        Assert.Throws<ArgumentException>(() => arena.Rewind(stale));
    }

    [Fact]
    public void Rewind_DiscardedMarkStaysInvalidAfterCheckpointSlotIsReused()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark first = arena.Mark();
        arena.Allocate<int>(4);
        NativeArenaMark stale = arena.Mark();

        arena.Rewind(first);
        arena.Allocate<int>(4);
        NativeArenaMark replacement = arena.Mark();

        Assert.Throws<ArgumentException>(() => arena.Rewind(stale));
        arena.Rewind(replacement);
        Assert.Equal((nuint)16, arena.Used);
    }

    [Fact]
    public void Rewind_RejectsMarkFromAnotherArena()
    {
        using var a = new NativeArena(1024);
        using var b = new NativeArena(1024);

        NativeArenaMark mark = a.Mark();
        Assert.Throws<ArgumentException>(() => b.Rewind(mark));
    }

    [Fact]
    public void Reset_InvalidatesMarks()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark mark = arena.Mark();
        arena.Reset();

        Assert.Throws<ArgumentException>(() => arena.Rewind(mark));
    }

    [Fact]
    public void Reset_InvalidatesMarksEvenAfterCheckpointSlotIsReused()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark stale = arena.Mark();
        arena.Reset();
        NativeArenaMark current = arena.Mark();

        Assert.Throws<ArgumentException>(() => arena.Rewind(stale));
        arena.Rewind(current);
    }

    [Fact]
    public void Rewind_DefaultMark_Throws()
    {
        using var arena = new NativeArena(1024);
        Assert.Throws<ArgumentException>(() => arena.Rewind(default));
    }

    [Fact]
    public void Rewind_CanReuseSameMark()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark mark = arena.Mark();
        arena.Allocate<int>(8);
        arena.Rewind(mark);
        arena.Allocate<int>(8);
        arena.Rewind(mark);

        Assert.Equal((nuint)0, arena.Used);
    }

    [Fact]
    public void Mark_GrowsCheckpointStorage_WithoutLosingMarks()
    {
        using var arena = new NativeArena(1024);
        NativeArenaMark first = arena.Mark();

        for (int i = 0; i < 128; i++)
        {
            arena.Mark();
        }

        arena.Allocate<int>(4);
        arena.Rewind(first);
        Assert.Equal((nuint)0, arena.Used);
    }

    [Fact]
    public void MarkAndRewind_AfterWarmup_DoNotAllocateManagedMemory()
    {
        using var arena = new NativeArena(1024);

        for (int i = 0; i < 10_000; i++)
        {
            arena.Reset();
            NativeArenaMark mark = arena.Mark();
            arena.Allocate<int>(4)[0] = i;
            arena.Rewind(mark);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
        {
            arena.Reset();
            NativeArenaMark mark = arena.Mark();
            arena.Allocate<int>(4)[0] = i;
            arena.Rewind(mark);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Mark_AfterDispose_Throws()
    {
        var arena = new NativeArena(1024);
        arena.Dispose();

        Assert.Throws<ObjectDisposedException>(() => arena.Mark());
    }

    [Fact]
    public void Rewind_AfterDispose_Throws()
    {
        var arena = new NativeArena(1024);
        NativeArenaMark mark = arena.Mark();
        arena.Dispose();

        Assert.Throws<ObjectDisposedException>(() => arena.Rewind(mark));
    }

    [Fact]
    public void TryAllocate_SuccessAndFailure_PreserveCorrectState()
    {
        using var arena = new NativeArena(64);
        Assert.True(arena.TryAllocate<int>(4, out Span<int> first));
        first.Fill(123);

        nuint used = arena.Used;
        Assert.False(arena.TryAllocate<byte>(65, out Span<byte> failed));
        Assert.True(failed.IsEmpty);
        Assert.Equal(used, arena.Used);
        Assert.Equal(123, first[0]);
    }

    [Fact]
    public void TryAllocate_ZeroLength_SucceedsWithoutMovingOffset()
    {
        using var arena = new NativeArena(0);
        Assert.True(arena.TryAllocate<int>(0, out Span<int> empty));
        Assert.True(empty.IsEmpty);
        Assert.Equal((nuint)0, arena.Used);
    }

    [Fact]
    public void TryAllocate_ZeroLengthWithPadding_DoesNotMoveOffset()
    {
        using var arena = new NativeArena(64);
        arena.Allocate<byte>(3);

        nuint before = arena.Used;
        Assert.True(arena.TryAllocate<int>(0, out Span<int> empty, 64));
        Assert.True(empty.IsEmpty);
        Assert.Equal(before, arena.Used);
    }

    [Fact]
    public void TryAllocate_InvalidArguments_Throw()
    {
        using var arena = new NativeArena(128);
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.TryAllocate<int>(-1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.TryAllocate<int>(1, out _, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.TryAllocate<int>(0, out _, 3));
    }

    [Fact]
    public void TryAllocate_AfterDispose_Throws()
    {
        var arena = new NativeArena(128);
        arena.Dispose();
        Assert.Throws<ObjectDisposedException>(() => arena.TryAllocate<int>(1, out _));
        Assert.Throws<ObjectDisposedException>(() => arena.TryAllocate<int>(0, out _));
    }

    [Fact]
    public void TryAllocate_HotPath_HasNoManagedAllocations()
    {
        using var arena = new NativeArena(1024);

        for (int i = 0; i < 10_000; i++)
        {
            arena.Reset();
            arena.TryAllocate<int>(8, out Span<int> values);
            values[0] = i;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
        {
            arena.Reset();
            arena.TryAllocate<int>(8, out Span<int> values);
            values[0] = i;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void TryAllocate_CapacityFailure_HasNoManagedAllocations()
    {
        using var arena = new NativeArena(64);

        for (int i = 0; i < 10_000; i++)
        {
            arena.TryAllocate<byte>(65, out _);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
        {
            arena.TryAllocate<byte>(65, out _);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
