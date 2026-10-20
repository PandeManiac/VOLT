using Volt.Platform.Windows;
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
		Assert.Null(options.ResizeAtFrame);
		Assert.Null(options.ResizeTo);
		Assert.Equal(DemoScene.Gallery, options.Scene);
		Assert.False(options.DepthTestDisabled);
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
	public void TryParseOptions_DefaultSceneIsGallery()
	{
		Assert.True(
			Program.TryParseOptions(
				["--frames", "120"],
				out SampleOptions options,
				out _));

		Assert.Equal(DemoScene.Gallery, options.Scene);
		Assert.False(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_ParsesExplicitGallery()
	{
		Assert.True(
			Program.TryParseOptions(
				["--scene", "gallery"],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.Gallery, options.Scene);
		Assert.False(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_ParsesDepthOcclusion()
	{
		Assert.True(
			Program.TryParseOptions(
				["--scene", "depth-occlusion"],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.DepthOcclusion, options.Scene);
		Assert.False(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_ParsesCameraTransforms()
	{
		Assert.True(
			Program.TryParseOptions(
				["--scene", "camera-transforms"],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.CameraTransforms, options.Scene);
		Assert.False(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_ParsesCameraTransformsWithResize()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", "camera-transforms",
					"--resize-at-frame", "200",
					"--resize-to", "960x540"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.CameraTransforms, options.Scene);
		Assert.Equal(200, options.ResizeAtFrame);
		Assert.Equal(new WindowSize(960, 540), options.ResizeTo);
	}

	[Fact]
	public void TryParseOptions_MissingSceneValue_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene"],
				out _,
				out string error));

		Assert.Contains("requires a value", error);
	}

	[Theory]
	[InlineData("depth")]
	[InlineData("Gallery")]
	[InlineData("depth-occlusion-extra")]
	[InlineData("")]
	public void TryParseOptions_UnknownScene_Fails(string scene)
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene", scene],
				out _,
				out string error));

		Assert.Contains("gallery", error);
		Assert.Contains("depth-occlusion", error);
		Assert.Contains("camera-transforms", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateScene_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene", "gallery", "--scene", "depth-occlusion"],
				out _,
				out string error));

		Assert.Contains("supplied twice", error);
	}

	[Fact]
	public void TryParseOptions_ParsesDisabledDepthTest()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--scene", "depth-occlusion",
					"--disable-depth-test"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.DepthOcclusion, options.Scene);
		Assert.True(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_DisableDepthTestWithoutScene_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--disable-depth-test"],
				out _,
				out string error));

		Assert.Contains("requires --scene depth-occlusion", error);
	}

	[Fact]
	public void TryParseOptions_DisableDepthTestInGallery_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene", "gallery", "--disable-depth-test"],
				out _,
				out string error));

		Assert.Contains("requires --scene depth-occlusion", error);
	}

	[Fact]
	public void TryParseOptions_DisableDepthTestInCameraScene_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene", "camera-transforms", "--disable-depth-test"],
				out _,
				out string error));

		Assert.Contains("requires --scene depth-occlusion", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateDisableDepthTest_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--scene", "depth-occlusion",
					"--disable-depth-test",
					"--disable-depth-test"
				],
				out _,
				out string error));

		Assert.Contains("supplied twice", error);
	}

	[Fact]
	public void TryParseOptions_CombinedSceneResizeAndNegativeControl_Parses()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", "depth-occlusion",
					"--disable-depth-test",
					"--resize-at-frame", "200",
					"--resize-to", "960x540"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.DepthOcclusion, options.Scene);
		Assert.True(options.DepthTestDisabled);
		Assert.Equal(200, options.ResizeAtFrame);
		Assert.Equal(new WindowSize(960, 540), options.ResizeTo);
	}

	[Fact]
	public void TryParseOptions_ParsesResizeScenario()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--validation",
					"--frames", "600",
					"--resize-at-frame", "200",
					"--resize-to", "960x540"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.True(options.Validation);
		Assert.Equal(600, options.FrameLimit);
		Assert.Equal(200, options.ResizeAtFrame);
		Assert.Equal(new WindowSize(960, 540), options.ResizeTo);
	}

	[Fact]
	public void TryParseOptions_InteractiveResizeScenario_Parses()
	{
		Assert.True(
			Program.TryParseOptions(
				["--resize-at-frame", "30", "--resize-to", "800x600"],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Null(options.FrameLimit);
		Assert.Equal(30, options.ResizeAtFrame);
		Assert.Equal(new WindowSize(800, 600), options.ResizeTo);
	}

	[Fact]
	public void TryParseOptions_ResizeFrameWithoutTarget_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames", "600", "--resize-at-frame", "200"],
				out _,
				out string error));

		Assert.Contains("must be supplied together", error);
	}

	[Fact]
	public void TryParseOptions_ResizeTargetWithoutFrame_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames", "600", "--resize-to", "960x540"],
				out _,
				out string error));

		Assert.Contains("must be supplied together", error);
	}

	[Fact]
	public void TryParseOptions_MissingResizeFrameValue_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--resize-at-frame"],
				out _,
				out string error));

		Assert.Contains("requires a value", error);
	}

	[Fact]
	public void TryParseOptions_MissingResizeTargetValue_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--resize-to"],
				out _,
				out string error));

		Assert.Contains("requires a value", error);
	}

	[Theory]
	[InlineData("0x540")]
	[InlineData("960x0")]
	[InlineData("0x0")]
	public void TryParseOptions_ZeroResizeDimension_Fails(string target)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", "200",
					"--resize-to", target
				],
				out _,
				out string error));

		Assert.Contains("must be positive", error);
	}

	[Theory]
	[InlineData("960")]
	[InlineData("960x")]
	[InlineData("x540")]
	[InlineData("960X540")]
	[InlineData("960x540x1")]
	[InlineData("abcxdef")]
	[InlineData("-960x540")]
	[InlineData("960x-540")]
	[InlineData("960x 540")]
	public void TryParseOptions_MalformedResizeTarget_Fails(string target)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", "200",
					"--resize-to", target
				],
				out _,
				out string error));

		Assert.False(string.IsNullOrEmpty(error));
	}

	[Theory]
	[InlineData("16385x540")]
	[InlineData("960x16385")]
	[InlineData("99999999999x540")]
	public void TryParseOptions_OversizedResizeDimension_Fails(string target)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", "200",
					"--resize-to", target
				],
				out _,
				out string error));

		Assert.Contains("no greater than", error);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-5")]
	[InlineData("abc")]
	[InlineData("1000001")]
	[InlineData("99999999999999999999")]
	public void TryParseOptions_InvalidResizeFrame_Fails(string frame)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", frame,
					"--resize-to", "960x540"
				],
				out _,
				out string error));

		Assert.Contains("positive integer", error);
	}

	[Theory]
	[InlineData("200", "200")]
	[InlineData("250", "200")]
	[InlineData("1", "1")]
	public void TryParseOptions_ResizeFrameAtOrAfterLimit_Fails(
		string frame,
		string limit)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", limit,
					"--resize-at-frame", frame,
					"--resize-to", "960x540"
				],
				out _,
				out string error));

		Assert.Contains("must be less than", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateResizeFrame_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", "100",
					"--resize-at-frame", "200",
					"--resize-to", "960x540"
				],
				out _,
				out string error));

		Assert.Contains("twice", error);
	}

	[Fact]
	public void TryParseOptions_DuplicateResizeTarget_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--resize-at-frame", "200",
					"--resize-to", "960x540",
					"--resize-to", "800x600"
				],
				out _,
				out string error));

		Assert.Contains("twice", error);
	}

	[Fact]
	public void TryParseOptions_OrdinaryBoundedRunHasNoResize()
	{
		Assert.True(
			Program.TryParseOptions(
				["--frames", "120"],
				out SampleOptions options,
				out _));

		Assert.Null(options.ResizeAtFrame);
		Assert.Null(options.ResizeTo);
		Assert.Equal(DemoScene.Gallery, options.Scene);
		Assert.False(options.DepthTestDisabled);
	}

	[Fact]
	public void TryParseOptions_ParsesCameraScript()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", "camera-transforms",
					"--camera-script"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.Equal(DemoScene.CameraTransforms, options.Scene);
		Assert.True(options.CameraScript);
		Assert.Equal(600, options.FrameLimit);
	}

	[Fact]
	public void TryParseOptions_CameraScriptWithResize_Parses()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", "camera-transforms",
					"--camera-script",
					"--resize-at-frame", "200",
					"--resize-to", "960x540"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.True(options.CameraScript);
		Assert.Equal(200, options.ResizeAtFrame);
		Assert.Equal(new WindowSize(960, 540), options.ResizeTo);
	}

	[Theory]
	[InlineData("gallery")]
	[InlineData("depth-occlusion")]
	public void TryParseOptions_CameraScriptOutsideCameraScene_Fails(
		string scene)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", scene,
					"--camera-script"
				],
				out _,
				out string error));

		Assert.Contains("requires --scene camera-transforms", error);
	}

	[Fact]
	public void TryParseOptions_CameraScriptWithoutScene_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--frames", "600", "--camera-script"],
				out _,
				out string error));

		Assert.Contains("requires --scene camera-transforms", error);
	}

	[Fact]
	public void TryParseOptions_CameraScriptWithoutFrameLimit_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				["--scene", "camera-transforms", "--camera-script"],
				out _,
				out string error));

		Assert.Contains("bounded --frames", error);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("121")]
	public void TryParseOptions_CameraScriptTooShort_Fails(string frames)
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", frames,
					"--scene", "camera-transforms",
					"--camera-script"
				],
				out _,
				out string error));

		Assert.Contains(
			$"at least {CameraScript.MinimumFrameLimit}",
			error);
	}

	[Fact]
	public void TryParseOptions_CameraScriptAtMinimumFrames_Parses()
	{
		Assert.True(
			Program.TryParseOptions(
				[
					"--frames",
					CameraScript.MinimumFrameLimit.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					"--scene", "camera-transforms",
					"--camera-script"
				],
				out SampleOptions options,
				out string error));

		Assert.Empty(error);
		Assert.True(options.CameraScript);
		Assert.Equal(CameraScript.MinimumFrameLimit, options.FrameLimit);
	}

	[Fact]
	public void TryParseOptions_DuplicateCameraScript_Fails()
	{
		Assert.False(
			Program.TryParseOptions(
				[
					"--frames", "600",
					"--scene", "camera-transforms",
					"--camera-script",
					"--camera-script"
				],
				out _,
				out string error));

		Assert.Contains("twice", error);
	}

	[Fact]
	public void TryParseOptions_DefaultRunHasNoCameraScript()
	{
		Assert.True(
			Program.TryParseOptions(
				["--frames", "120"],
				out SampleOptions options,
				out _));

		Assert.False(options.CameraScript);
		Assert.Equal(DemoScene.Gallery, options.Scene);
	}

	[Fact]
	public void ShouldRequestResize_WithoutScenario_IsFalse()
	{
		SampleOptions options = new(
			Validation: false,
			FrameLimit: 600,
			ResizeAtFrame: null,
			ResizeTo: null,
			Scene: DemoScene.Gallery,
			DepthTestDisabled: false);

		Assert.False(Program.ShouldRequestResize(0, options, false));
		Assert.False(Program.ShouldRequestResize(600, options, false));
	}

	[Fact]
	public void ShouldRequestResize_WaitsForTriggerFrame()
	{
		SampleOptions options = ResizeOptions(frame: 10);

		Assert.False(Program.ShouldRequestResize(9, options, false));
		Assert.True(Program.ShouldRequestResize(10, options, false));
		Assert.True(Program.ShouldRequestResize(11, options, false));
	}

	[Fact]
	public void ShouldRequestResize_TriggersOnlyOnce()
	{
		SampleOptions options = ResizeOptions(frame: 10);

		Assert.False(Program.ShouldRequestResize(10, options, true));
		Assert.False(Program.ShouldRequestResize(11, options, true));
	}

	[Fact]
	public void IsResizeObserved_RequiresARequestedExtentChange()
	{
		WindowSize original = new(1280, 720);
		WindowSize resized = new(960, 540);

		Assert.False(
			Program.IsResizeObserved(false, false, original, resized));

		Assert.False(
			Program.IsResizeObserved(true, false, original, original));

		Assert.True(
			Program.IsResizeObserved(true, false, original, resized));

		Assert.False(
			Program.IsResizeObserved(true, true, original, resized));
	}

	private static SampleOptions ResizeOptions(int frame)
	{
		return new SampleOptions(
			Validation: false,
			FrameLimit: 600,
			ResizeAtFrame: frame,
			ResizeTo: new WindowSize(960, 540),
			Scene: DemoScene.Gallery,
			DepthTestDisabled: false);
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
