
using Volt.Platform.Windows;

namespace Volt.Platform.Tests.Windows;

public sealed class WindowDescriptionTests
{
    [Fact]
    public void Constructor_StoresSettings()
    {
        WindowDescription description = new(
            "VOLT",
            new WindowSize(1280, 720),
            Resizable: false,
            HighPixelDensity: false,
            VulkanCompatible: true);

        Assert.Equal("VOLT", description.Title);
        Assert.Equal(new WindowSize(1280, 720), description.Size);
        Assert.False(description.Resizable);
        Assert.False(description.HighPixelDensity);
        Assert.True(description.VulkanCompatible);
    }

    [Fact]
    public void Constructor_UsesExpectedDefaults()
    {
        WindowDescription description = new(
            "VOLT",
            new WindowSize(800, 600));

        Assert.True(description.Resizable);
        Assert.True(description.HighPixelDensity);
        Assert.False(description.VulkanCompatible);
    }

    [Fact]
    public void ValidDescription_PassesValidation()
    {
        new WindowDescription(
            "VOLT",
            new WindowSize(800, 600)).Validate();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidTitle_Throws(string? title)
    {
        WindowDescription description = new(
            title!,
            new WindowSize(800, 600));

        ArgumentException error =
            Assert.Throws<ArgumentException>(description.Validate);

        Assert.Equal("Title", error.ParamName);
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, 600)]
    [InlineData(800, -1)]
    public void InvalidSize_Throws(int width, int height)
    {
        WindowDescription description = new(
            "VOLT",
            new WindowSize(width, height));

        ArgumentOutOfRangeException error =
            Assert.Throws<ArgumentOutOfRangeException>(
                description.Validate);

        Assert.Equal("Size", error.ParamName);
    }

    [Fact]
    public void DefaultDescription_IsInvalid()
    {
        WindowDescription description = default;
        Assert.Throws<ArgumentException>(description.Validate);
    }
}
