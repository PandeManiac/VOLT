
using System.Runtime.CompilerServices;
using Volt.Rendering.Resources;
using Xunit;

namespace Volt.Rendering.Tests.Resources;

public sealed class MaterialHandleTests
{
	[Fact]
	public void Default_IsInvalid()
	{
		MaterialHandle handle = default;
		Assert.False(handle.IsValid);
	}

	[Fact]
	public void Constructor_StoresIndexAndGeneration()
	{
		MaterialHandle handle = new(12, 3);
		Assert.Equal(12u, handle.Index);
		Assert.Equal(3u, handle.Generation);
	}

	[Fact]
	public void NonzeroGeneration_IsValid()
	{
		MaterialHandle handle = new(0, 1);
		Assert.True(handle.IsValid);
	}

	[Fact]
	public void ZeroGeneration_IsInvalid()
	{
		MaterialHandle handle = new(12, 0);
		Assert.False(handle.IsValid);
	}

	[Fact]
	public void EqualHandles_CompareEqual()
	{
		MaterialHandle first = new(12, 3);
		MaterialHandle second = new(12, 3);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void DifferentIndices_CompareUnequal()
	{
		MaterialHandle first = new(12, 3);
		MaterialHandle second = new(13, 3);
		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentGenerations_CompareUnequal()
	{
		MaterialHandle oldHandle = new(12, 1);
		MaterialHandle newHandle = new(12, 2);
		Assert.NotEqual(oldHandle, newHandle);
	}

	[Fact]
	public void Handle_HasExpectedSize()
	{
		Assert.Equal(8, Unsafe.SizeOf<MaterialHandle>());
	}

	[Fact]
	public void Handle_CanBeStoredInUnmanagedMemory()
	{
		Span<MaterialHandle> handles = stackalloc MaterialHandle[2];

		handles[0] = new MaterialHandle(1, 1);
		handles[1] = new MaterialHandle(2, 1);

		Assert.True(handles[0].IsValid);
		Assert.True(handles[1].IsValid);
		Assert.NotEqual(handles[0], handles[1]);
	}
}
