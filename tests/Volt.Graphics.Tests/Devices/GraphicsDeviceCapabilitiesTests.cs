
using Volt.Graphics.Devices;
using Xunit;

namespace Volt.Graphics.Tests.Devices;

public sealed class GraphicsDeviceCapabilitiesTests
{
	[Fact]
	public void Constructor_StoresCapabilities()
	{
		GraphicsDeviceCapabilities capabilities = CreateValid();

		Assert.Equal(GraphicsBackend.Vulkan, capabilities.Backend);
		Assert.Equal("Test GPU", capabilities.AdapterName);
		Assert.Equal(16384u, capabilities.MaxTexture2DDimension);
		Assert.Equal(8u, capabilities.MaxColorAttachments);
		Assert.True(capabilities.SupportsWireframe);
		Assert.True(capabilities.SupportsTimestampQueries);
	}

	[Fact]
	public void Default_IsNotValid()
	{
		GraphicsDeviceCapabilities capabilities = default;

		Assert.Throws<ArgumentOutOfRangeException>(
			capabilities.Validate);
	}

	[Theory]
	[InlineData(GraphicsBackend.Vulkan)]
	[InlineData(GraphicsBackend.Direct3D12)]
	public void Validate_AcceptsSupportedBackends(
		GraphicsBackend backend)
	{
		GraphicsDeviceCapabilities capabilities =
			CreateValid(backend);

		capabilities.Validate();
	}

	[Theory]
	[InlineData(GraphicsBackend.Unknown)]
	[InlineData((GraphicsBackend)999)]
	public void Validate_RejectsInvalidBackends(
		GraphicsBackend backend)
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			Backend = backend
		};

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				capabilities.Validate);

		Assert.Equal("Backend", exception.ParamName);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	public void Validate_RejectsInvalidAdapterNames(
		string adapterName)
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			AdapterName = adapterName
		};

		ArgumentException exception =
			Assert.Throws<ArgumentException>(
				capabilities.Validate);

		Assert.Equal("AdapterName", exception.ParamName);
	}

	[Fact]
	public void Validate_RejectsNullAdapterName()
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			AdapterName = null!
		};

		Assert.Throws<ArgumentException>(
			capabilities.Validate);
	}

	[Fact]
	public void Validate_RejectsZeroTextureDimension()
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			MaxTexture2DDimension = 0
		};

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				capabilities.Validate);

		Assert.Equal(
			"MaxTexture2DDimension",
			exception.ParamName);
	}

	[Fact]
	public void Validate_RejectsZeroColorAttachments()
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			MaxColorAttachments = 0
		};

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				capabilities.Validate);

		Assert.Equal(
			"MaxColorAttachments",
			exception.ParamName);
	}

	[Fact]
	public void Validate_AcceptsDisabledOptionalFeatures()
	{
		GraphicsDeviceCapabilities capabilities = CreateValid() with
		{
			SupportsWireframe = false,
			SupportsTimestampQueries = false
		};

		capabilities.Validate();

		Assert.False(capabilities.SupportsWireframe);
		Assert.False(capabilities.SupportsTimestampQueries);
	}

	[Fact]
	public void EqualCapabilities_CompareEqual()
	{
		GraphicsDeviceCapabilities first = CreateValid();
		GraphicsDeviceCapabilities second = CreateValid();

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void DifferentCapabilities_CompareUnequal()
	{
		GraphicsDeviceCapabilities first = CreateValid();

		GraphicsDeviceCapabilities second = first with
		{
			SupportsWireframe = false
		};

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void WithExpression_DoesNotModifyOriginal()
	{
		GraphicsDeviceCapabilities original = CreateValid();

		GraphicsDeviceCapabilities modified = original with
		{
			MaxTexture2DDimension = 8192
		};

		Assert.Equal(16384u, original.MaxTexture2DDimension);
		Assert.Equal(8192u, modified.MaxTexture2DDimension);
	}

	private static GraphicsDeviceCapabilities CreateValid(
		GraphicsBackend backend = GraphicsBackend.Vulkan)
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
