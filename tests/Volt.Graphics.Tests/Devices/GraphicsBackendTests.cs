
using Volt.Graphics.Devices;
using Xunit;

namespace Volt.Graphics.Tests.Devices;

public sealed class GraphicsBackendTests
{
	[Fact]
	public void Default_IsUnknown()
	{
		GraphicsBackend backend = default;
		Assert.Equal(GraphicsBackend.Unknown, backend);
	}

	[Fact]
	public void Unknown_HasExpectedValue()
	{
		Assert.Equal(0, (int)GraphicsBackend.Unknown);
	}

	[Fact]
	public void Vulkan_HasExpectedValue()
	{
		Assert.Equal(1, (int)GraphicsBackend.Vulkan);
	}

	[Fact]
	public void Direct3D12_HasExpectedValue()
	{
		Assert.Equal(2, (int)GraphicsBackend.Direct3D12);
	}

	[Theory]
	[InlineData(GraphicsBackend.Unknown)]
	[InlineData(GraphicsBackend.Vulkan)]
	[InlineData(GraphicsBackend.Direct3D12)]
	public void DefinedBackend_IsRecognized(GraphicsBackend backend)
	{
		Assert.True(Enum.IsDefined(backend));
	}

	[Fact]
	public void UndefinedBackend_IsNotRecognized()
	{
		GraphicsBackend backend = (GraphicsBackend)999;
		Assert.False(Enum.IsDefined(backend));
	}

	[Fact]
	public void Vulkan_AndDirect3D12_AreDistinct()
	{
		Assert.NotEqual(
			GraphicsBackend.Vulkan,
			GraphicsBackend.Direct3D12);
	}
}
