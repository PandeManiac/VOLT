using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class ProgramOptionsTests
{
	[Fact]
	public void TryParseOptions_EmptyArguments_IsInteractive()
	{
		Assert.True(
			Program.TryParseOptions(
				[],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.False(options.Validation);
		Assert.Null(options.FrameLimit);
	}

	[Fact]
	public void TryParseOptions_ParsesValidationAndFrames()
	{
		Assert.True(
			Program.TryParseOptions(
				["--validation", "--frames", "120"],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.True(options.Validation);
		Assert.Equal(120, options.FrameLimit);
	}

	[Fact]
	public void TryParseOptions_AcceptsFramesBeforeValidation()
	{
		Assert.True(
			Program.TryParseOptions(
				["--frames", "1", "--validation"],
				out SampleOptions options,
				out _));

		Assert.True(options.Validation);
		Assert.Equal(1, options.FrameLimit);
	}

	[Fact]
	public void TryParseOptions_MissingFrameCount_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames"],
				out _,
				out string error));

		Assert.Contains("requires a value", error);
	}

	[Fact]
	public void TryParseOptions_NonPositiveFrameCount_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames", "0"],
				out _,
				out string error));

		Assert.Contains("positive integer", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateValidation_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--validation", "--validation"],
				out _,
				out string error));

		Assert.Contains("twice", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateFrames_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames", "1", "--frames", "2"],
				out _,
				out string error));

		Assert.Contains("twice", error);
	}

	[Fact]
	public void TryParseOptions_UnknownArgument_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--unknown"],
				out _,
				out string error));

		Assert.Contains("Unknown argument", error);
	}

	[Fact]
	public void RequireSwapchainMaintenance1Extension_Missing_Throws()
	{
		NotSupportedException exception = Assert.Throws<NotSupportedException>(
			() => Program.RequireSwapchainMaintenance1Extension(null));

		Assert.Contains("VK_KHR_swapchain_maintenance1", exception.Message);
		Assert.Contains("VK_EXT_swapchain_maintenance1", exception.Message);
		Assert.Contains("presentation fences", exception.Message);
	}

	[Theory]
	[InlineData("VK_KHR_swapchain_maintenance1")]
	[InlineData("VK_EXT_swapchain_maintenance1")]
	public void RequireSwapchainMaintenance1Extension_Supported_ReturnsExtension(
		string extension)
	{
		Assert.Equal(
			extension,
			Program.RequireSwapchainMaintenance1Extension(extension));
	}
}
