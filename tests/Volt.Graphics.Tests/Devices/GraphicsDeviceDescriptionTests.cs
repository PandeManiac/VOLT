
using System.Runtime.CompilerServices;
using Volt.Graphics.Devices;
using Xunit;

namespace Volt.Graphics.Tests.Devices;

public sealed class GraphicsDeviceDescriptionTests
{
	[Fact]
	public void Default_HasUnknownBackend()
	{
		GraphicsDeviceDescription description = default;

		Assert.Equal(
			GraphicsBackend.Unknown,
			description.Backend);

		Assert.False(description.EnableValidation);
	}

	[Fact]
	public void Constructor_StoresBackend()
	{
		GraphicsDeviceDescription description = new(
			GraphicsBackend.Vulkan);

		Assert.Equal(
			GraphicsBackend.Vulkan,
			description.Backend);
	}

	[Fact]
	public void Constructor_DisablesValidationByDefault()
	{
		GraphicsDeviceDescription description = new(
			GraphicsBackend.Vulkan);

		Assert.False(description.EnableValidation);
	}

	[Fact]
	public void Constructor_CanEnableValidation()
	{
		GraphicsDeviceDescription description = new(
			GraphicsBackend.Vulkan,
			EnableValidation: true);

		Assert.True(description.EnableValidation);
	}

	[Theory]
	[InlineData(GraphicsBackend.Vulkan)]
	[InlineData(GraphicsBackend.Direct3D12)]
	public void Validate_AcceptsDefinedBackends(
		GraphicsBackend backend)
	{
		GraphicsDeviceDescription description = new(backend);
		description.Validate();
	}

	[Fact]
	public void Validate_RejectsUnknownBackend()
	{
		GraphicsDeviceDescription description = new(
			GraphicsBackend.Unknown);

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				description.Validate);

		Assert.Equal("Backend", exception.ParamName);
	}

	[Fact]
	public void Validate_RejectsUndefinedBackend()
	{
		GraphicsDeviceDescription description = new(
			(GraphicsBackend)999);

		Assert.Throws<ArgumentOutOfRangeException>(
			description.Validate);
	}

	[Fact]
	public void EqualDescriptions_CompareEqual()
	{
		GraphicsDeviceDescription first = new(
			GraphicsBackend.Vulkan,
			EnableValidation: true);

		GraphicsDeviceDescription second = new(
			GraphicsBackend.Vulkan,
			EnableValidation: true);

		Assert.Equal(first, second);
		Assert.Equal(first.GetHashCode(), second.GetHashCode());
	}

	[Fact]
	public void DifferentBackends_CompareUnequal()
	{
		GraphicsDeviceDescription first = new(
			GraphicsBackend.Vulkan);

		GraphicsDeviceDescription second = new(
			GraphicsBackend.Direct3D12);

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void DifferentValidationSettings_CompareUnequal()
	{
		GraphicsDeviceDescription first = new(
			GraphicsBackend.Vulkan,
			EnableValidation: false);

		GraphicsDeviceDescription second = new(
			GraphicsBackend.Vulkan,
			EnableValidation: true);

		Assert.NotEqual(first, second);
	}

	[Fact]
	public void Description_ContainsNoManagedReferences()
	{
		Assert.False(
			RuntimeHelpers.IsReferenceOrContainsReferences<
				GraphicsDeviceDescription>());
	}
}
