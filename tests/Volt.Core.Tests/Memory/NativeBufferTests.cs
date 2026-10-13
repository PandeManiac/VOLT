namespace VOLT.Core.Tests.Memory;

public sealed unsafe class NativeBufferTests
{
    [Fact]
    public void Constructor_ValidLength_CreatesBuffer()
    {
        using var buffer = new NativeBuffer<int>(128);

        Assert.Equal(128, buffer.Length);
        Assert.Equal((nuint)64, buffer.Alignment);
        Assert.False(buffer.IsDisposed);
    }

    [Fact]
    public void Constructor_ZeroLength_CreatesValidEmptyBuffer()
    {
        using var buffer = new NativeBuffer<int>(0);

        Assert.Equal(0, buffer.Length);
        Assert.False(buffer.IsDisposed);
        Assert.True(buffer.AsSpan().IsEmpty);
    }

    [Fact]
    public void Constructor_NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NativeBuffer<int>(-1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(24)]
    public void Constructor_InvalidAlignment_Throws(int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new NativeBuffer<int>(16, (nuint)alignment));
    }

    [Fact]
    public void Constructor_ValidAlignment_IsRespected()
    {
        using var buffer = new NativeBuffer<byte>(128, 128);

        fixed (byte* pointer = buffer.AsSpan())
        {
            Assert.Equal((nuint)0, (nuint)pointer % 128);
        }
    }

    [Fact]
    public void Indexer_WriteAndRead_PreservesValue()
    {
        using var buffer = new NativeBuffer<int>(16);

        buffer[5] = 12345;

        Assert.Equal(12345, buffer[5]);
    }

    [Fact]
    public void Indexer_ReturnsWritableReference()
    {
        using var buffer = new NativeBuffer<int>(16);

        ref int value = ref buffer[5];

        value = 999;

        Assert.Equal(999, buffer[5]);
    }

    [Fact]
    public void Indexer_OutOfBounds_Throws()
    {
        using var buffer = new NativeBuffer<int>(16);

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            _ = buffer[-1];
        });

        Assert.Throws<IndexOutOfRangeException>(() =>
        {
            _ = buffer[16];
        });
    }

    [Fact]
    public void Clear_ZerosAllElements()
    {
        using var buffer = new NativeBuffer<int>(128);

        buffer.AsSpan().Fill(42);
        buffer.Clear();

        foreach (int value in buffer.AsSpan())
        {
            Assert.Equal(0, value);
        }
    }

    [Fact]
    public void AsSpan_ModificationsAffectBuffer()
    {
        using var buffer = new NativeBuffer<int>(16);

        Span<int> span = buffer.AsSpan();
        span[3] = 777;

        Assert.Equal(777, buffer[3]);
    }

    [Fact]
    public void Dispose_MarksBufferDisposed()
    {
        var buffer = new NativeBuffer<int>(16);

        buffer.Dispose();

        Assert.True(buffer.IsDisposed);
    }

    [Fact]
    public void Dispose_MultipleCalls_AreSafe()
    {
        var buffer = new NativeBuffer<int>(16);

        buffer.Dispose();
        buffer.Dispose();

        Assert.True(buffer.IsDisposed);
    }

    [Fact]
    public void AccessAfterDispose_Throws()
    {
        var buffer = new NativeBuffer<int>(16);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = buffer[0];
        });

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = buffer.AsSpan();
        });

        Assert.Throws<ObjectDisposedException>(() =>
        {
            buffer.Clear();
        });
    }

    [Fact]
    public void ZeroLength_AccessAfterDispose_Throws()
    {
        var buffer = new NativeBuffer<int>(0);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = buffer.AsSpan();
        });
    }

    [Fact]
    public void RepeatedAllocationAndDisposal_RemainsCorrect()
    {
        for (int iteration = 0; iteration < 10_000; iteration++)
        {
            using var buffer = new NativeBuffer<int>(256);

            buffer.AsSpan().Fill(iteration);

            Assert.Equal(iteration, buffer[0]);
            Assert.Equal(iteration, buffer[255]);
        }
    }

    [Fact]
    public void HotPath_DoesNotAllocateManagedMemory()
    {
        using var buffer = new NativeBuffer<int>(1024);

        for (int i = 0; i < 10_000; i++)
        {
            buffer[i % buffer.Length] = i;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 100_000; i++)
        {
            buffer[i % buffer.Length] = i;
        }

        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
