
using System.Runtime.CompilerServices;
using Volt.Rendering.Resources;
using Xunit;

namespace Volt.Rendering.Tests.Resources;

public sealed class MeshHandleTests
{
	[Fact]
	public void Default_IsInvalid()
	{
		MeshHandle handle = default;
		Assert.False(handle.IsValid);
	}

	[Fact]
	public void Constructor_StoresIndexAndGeneration()
	{
		MeshHandle handle = new(12, 3);
		Assert.Equal(12u, handle.Index);
		Assert.Equal(3u, handle.Generation);
	}

	[Fact]
	public void NonzeroGeneration_IsValid()
	{
		MeshHandle handle = new(0, 1);
		Assert.True(handle.IsValid);
	}

	[Fact]
	public void ZeroGeneration_IsInvalid()
	{
		MeshHandle handle = new(12, 0);
		Assert.False(handle.IsValid);
	}

	[Fact]
	public void EqualHandles_CompareEqual()
	{
		MeshHandle first = new(12, 3);
		MeshHandle second = new(12, 3);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void DifferentIndices_CompareUnequal()
	{
		MeshHandle first = new(12, 3);
		MeshHandle second = new(13, 3);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentGenerations_CompareUnequal()
	{
		MeshHandle oldHandle = new(12, 1);
		MeshHandle newHandle = new(12, 2);
		Assert.NotEqual(oldHandle, newHandle);
	}

	[Fact]
	public void Handle_HasExpectedSize()
	{
		Assert.Equal(8, Unsafe.SizeOf<MeshHandle>());
	}

	[Fact]
	public void Handle_CanBeStoredInUnmanagedMemory()
	{
		Span<MeshHandle> handles = stackalloc MeshHandle[2];

		handles[0] = new MeshHandle(1, 1);
		handles[1] = new MeshHandle(2, 1);

		Assert.True(handles[0].IsValid);
		Assert.True(handles[1].IsValid);
		Assert.NotEqual(handles[0], handles[1]);
	}
}
