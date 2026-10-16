
using Volt.Graphics.Devices;
using Xunit;

namespace Volt.Graphics.Tests.Devices;

public sealed class IGraphicsDeviceTests
{
    private sealed class FakeGraphicsDevice(
        GraphicsDeviceCapabilities capabilities) : IGraphicsDevice
    {
        public GraphicsBackend Backend => capabilities.Backend;
        public GraphicsDeviceCapabilities Capabilities => capabilities;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [Fact]
    public void Device_ExposesSelectedBackend()
    {
        using FakeGraphicsDevice device = new(
            CreateCapabilities(GraphicsBackend.Vulkan));

        IGraphicsDevice graphicsDevice = device;

        Assert.Equal(
            GraphicsBackend.Vulkan,
            graphicsDevice.Backend);
    }

    [Fact]
    public void Device_ExposesCapabilities()
    {
        GraphicsDeviceCapabilities expected =
            CreateCapabilities(GraphicsBackend.Direct3D12);

        using FakeGraphicsDevice device = new(expected);

        IGraphicsDevice graphicsDevice = device;

        Assert.Equal(
            expected,
            graphicsDevice.Capabilities);

        Assert.Equal(
            graphicsDevice.Backend,
            graphicsDevice.Capabilities.Backend);
    }

    [Fact]
    public void Device_SupportsDeterministicDisposal()
    {
        FakeGraphicsDevice device = new(
            CreateCapabilities(GraphicsBackend.Vulkan));

        Assert.False(device.IsDisposed);

        using (IGraphicsDevice graphicsDevice = device)
        {
            Assert.Equal(
                GraphicsBackend.Vulkan,
                graphicsDevice.Backend);
        }

        Assert.True(device.IsDisposed);
    }

    [Fact]
    public void Device_CanBeConsumedWithoutBackendSpecificTypes()
    {
        using FakeGraphicsDevice device = new(
            CreateCapabilities(GraphicsBackend.Vulkan));

        GraphicsDeviceCapabilities capabilities =
            ReadCapabilities(device);

        Assert.Equal(
            device.Capabilities,
            capabilities);
    }

    private static GraphicsDeviceCapabilities ReadCapabilities(
        IGraphicsDevice device)
    {
        return device.Capabilities;
    }

    private static GraphicsDeviceCapabilities CreateCapabilities(
        GraphicsBackend backend)
    {
        return new GraphicsDeviceCapabilities(
            Backend: backend,
            AdapterName: "Test GPU",
            MaxTexture2DDimension: 16384,
            MaxColorAttachments: 8,
            SupportsWireframe: true,
            SupportsTimestampQueries: true);
    }
}
