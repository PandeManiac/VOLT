
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Tests.Instance;

public sealed class VulkanInstanceDescriptionTests
{
	[Fact]
	public void Constructor_StoresConfiguration()
	{
		VulkanInstanceDescription description = new(
			"VOLT",
			EnableValidation: true,
			ApiMajor: 1,
			ApiMinor: 3);

		Assert.Equal("VOLT", description.ApplicationName);
		Assert.True(description.EnableValidation);
		Assert.Equal(1u, description.ApiMajor);
		Assert.Equal(3u, description.ApiMinor);
	}

	[Fact]
	public void Constructor_UsesExpectedDefaults()
	{
		VulkanInstanceDescription description = new("VOLT");

		Assert.False(description.EnableValidation);
		Assert.Equal(1u, description.ApiMajor);
		Assert.Equal(2u, description.ApiMinor);
	}

	[Fact]
	public void ValidDescription_PassesValidation()
	{
		VulkanInstanceDescription description = new("VOLT");
		description.Validate();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void InvalidApplicationName_Throws(string? name)
	{
		VulkanInstanceDescription description = new(name!);

		ArgumentException exception =
			Assert.Throws<ArgumentException>(description.Validate);

		Assert.Equal("ApplicationName", exception.ParamName);
	}

	[Fact]
	public void EmbeddedNullCharacter_Throws()
	{
		VulkanInstanceDescription description = new("VOLT\0Test");

		ArgumentException exception =
			Assert.Throws<ArgumentException>(description.Validate);

		Assert.Equal("ApplicationName", exception.ParamName);
	}

	[Theory]
	[InlineData(0, 2)]
	[InlineData(128, 2)]
	public void InvalidApiMajor_Throws(int major, int minor)
	{
		VulkanInstanceDescription description = new(
			"VOLT",
			ApiMajor: (uint)major,
			ApiMinor: (uint)minor);

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				description.Validate);

		Assert.Equal("ApiMajor", exception.ParamName);
	}

	[Fact]
	public void InvalidApiMinor_Throws()
	{
		VulkanInstanceDescription description = new(
			"VOLT",
			ApiMinor: 1024);

		ArgumentOutOfRangeException exception =
			Assert.Throws<ArgumentOutOfRangeException>(
				description.Validate);

		Assert.Equal("ApiMinor", exception.ParamName);
	}

	[Theory]
	[InlineData(1, 0)]
	[InlineData(1, 4)]
	[InlineData(127, 1023)]
	public void RepresentableApiVersions_PassValidation(
		int major,
		int minor)
	{
		VulkanInstanceDescription description = new(
			"VOLT",
			ApiMajor: (uint)major,
			ApiMinor: (uint)minor);

		description.Validate();
	}

	[Fact]
	public void DefaultDescription_IsInvalid()
	{
		VulkanInstanceDescription description = default;
		Assert.Throws<ArgumentException>(description.Validate);
	}

	[Fact]
	public void EqualDescriptions_CompareEqual()
	{
		VulkanInstanceDescription first = new("VOLT");
		VulkanInstanceDescription second = new("VOLT");
		Assert.Equal(first, second);
	}

	[Fact]
	public void DifferentDescriptions_CompareUnequal()
	{
		VulkanInstanceDescription first = new(
			"VOLT",
			EnableValidation: true);

		VulkanInstanceDescription second = new(
			"VOLT",
			EnableValidation: false);

		Assert.NotEqual(first, second);
	}
}
