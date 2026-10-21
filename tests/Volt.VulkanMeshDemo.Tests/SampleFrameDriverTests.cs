using Volt.Core.Timing;
using Volt.Rendering.Cameras;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class SampleFrameDriverTests
{
	[Fact]
	public void QuickClick_IsConsumedOnceEvenWithFiveFixedSteps()
	{
		using TargetRangeScene scene = new(
			EcsMeshResolver.CreateTargetRange(
				MeshGeometry.CreateTargetRangeMeshes()));

		TargetRangeController controller = new(scene);
		CameraController camera = new(TargetRangeScene.InitialCameraPosition);

		PlatformInputHarness harness = new();

		using CameraInput input = new(harness.Input);

		SampleFrameDriver driver = new(
			camera,
			input,
			interactiveCamera: false,
			controller);

		TestMonotonicClock clock = new();
		GameLoop loop = new(GameLoopSettings.Default, clock);

		loop.RegisterUpdate(driver.Update);

		// One short click: press and release in the same input frame.
		harness.Poll(
			PlatformInputHarness.MouseDown(),
			PlatformInputHarness.MouseUp());

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal(1, driver.UpdateCount);
		Assert.True(input.IsCaptured);
		Assert.Equal(0, controller.ShotsFired);

		// A second short click while captured fires exactly once,
		// even though this frame also executed five fixed steps.
		harness.Poll(
			PlatformInputHarness.MouseDown(),
			PlatformInputHarness.MouseUp());

		clock.AdvanceNanoseconds(250_000_000);
		frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.Equal(2, driver.UpdateCount);
		Assert.Equal(1, controller.ShotsFired);

		for (int index = 0; index < 3; index++)
		{
			harness.Poll();
			clock.AdvanceNanoseconds(250_000_000);
			loop.Advance();
		}

		Assert.Equal(1, controller.ShotsFired);
		Assert.False(driver.LastInput.FireRequested);
	}

	[Fact]
	public void KeyboardEdge_IsNotReplayedAcrossFixedSteps()
	{
		using TargetRangeScene scene = new(
			EcsMeshResolver.CreateTargetRange(
				MeshGeometry.CreateTargetRangeMeshes()));

		TargetRangeController controller = new(scene);
		CameraController camera = new(TargetRangeScene.InitialCameraPosition);

		PlatformInputHarness harness = new();

		using CameraInput input = new(harness.Input);

		SampleFrameDriver driver = new(
			camera,
			input,
			interactiveCamera: false,
			controller);

		TestMonotonicClock clock = new();
		GameLoop loop = new(GameLoopSettings.Default, clock);

		loop.RegisterUpdate(driver.Update);

		harness.Poll(PlatformInputHarness.KeyDown(SDL3.SDL.Scancode.R));

		clock.AdvanceNanoseconds(250_000_000);
		FrameTiming frame = loop.Advance();

		Assert.Equal(5, frame.FixedSteps);
		Assert.True(driver.LastGameplay.Restarted);
		Assert.Equal(1, driver.UpdateCount);

		// The edge belongs to the input frame, not to the five
		// simulation steps it happened to span.
		harness.Poll();
		clock.AdvanceNanoseconds(250_000_000);
		loop.Advance();

		Assert.False(driver.LastGameplay.Restarted);
		Assert.Equal(2, driver.UpdateCount);
	}
}
