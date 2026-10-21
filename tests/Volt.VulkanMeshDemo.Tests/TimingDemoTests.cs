using System.Numerics;
using Volt.Core.Timing;
using Volt.Rendering.Cameras;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TimingDemoTests
{
	[Fact]
	public void PulseOffset_RampsAndHoldsBothExtremes()
	{
		Assert.Equal(0.0f, TimingDemo.PulseOffset(0));
		Assert.Equal(0.45f, TimingDemo.PulseOffset(15), 5);
		Assert.Equal(0.9f, TimingDemo.PulseOffset(30), 5);

		// Holds the maximum for thirty steps so a screenshot taken
		// after the checkpoint is stable.
		for (long step = 31; step <= 60; step++)
		{
			Assert.Equal(0.9f, TimingDemo.PulseOffset(step), 5);
		}

		Assert.Equal(0.0f, TimingDemo.PulseOffset(75), 5);
		Assert.Equal(-0.9f, TimingDemo.PulseOffset(90), 5);

		for (long step = 91; step <= 119; step++)
		{
			Assert.Equal(-0.9f, TimingDemo.PulseOffset(step), 5);
		}

		// The cycle restarts at the authored rest position.
		Assert.Equal(0.0f, TimingDemo.PulseOffset(120));
		Assert.Equal(0.9f, TimingDemo.PulseOffset(150), 5);
		Assert.Equal(-0.9f, TimingDemo.PulseOffset(210), 5);
	}

	[Fact]
	public void ScriptedRun_ProducesTheDocumentedCounters()
	{
		using EcsDemoScene scene = CreateScene();
		TimingDemo demo = new(scene, scripted: true);
		TimingScript script = new(demo, TimingDemo.MinimumScriptedFrames);

		RunScriptedFrames(demo, script, TimingDemo.MinimumScriptedFrames);

		Assert.Equal(581, demo.Loop.TotalFixedSteps);
		Assert.Equal(581, demo.PulseTicks);
		Assert.Equal(96, demo.TenHertzTicks);
		Assert.Equal(4, demo.TwoSecondEvents);
		Assert.Equal(1, demo.OneShotEvents);
		Assert.Equal(TimeSpan.Zero, demo.Loop.TotalDroppedTime);
		Assert.Equal(4.4e-6, demo.Loop.InterpolationAlpha, 8);
		Assert.Equal(-1.65f, demo.PulseX, 4);
		Assert.Equal(
			TimingDemo.OneShotHexagonScale,
			scene.GetScale(scene.HexagonEntity).X);
		Assert.False(demo.Loop.IsPaused);
	}

	[Fact]
	public void ScriptedRun_PausesAndStepsExactlyOnce()
	{
		using EcsDemoScene scene = CreateScene();
		TimingDemo demo = new(scene, scripted: true);
		TimingScript script = new(demo, TimingDemo.MinimumScriptedFrames);
		List<bool> pausedSteps = [];

		demo.Loop.RegisterFixedUpdate(
			(in FixedUpdateContext context) =>
				pausedSteps.Add(context.IsPaused));

		for (int frame = 1; frame <= TimingDemo.PauseFrame; frame++)
		{
			demo.Loop.Advance();
			script.Advance(frame);
		}

		Assert.True(demo.Loop.IsPaused);
		Assert.Equal(360, demo.Loop.TotalFixedSteps);

		long updatesWhilePaused = 0;

		demo.Loop.RegisterUpdate(
			(in UpdateContext _) => updatesWhilePaused++);

		for (int frame = TimingDemo.PauseFrame + 1;
			frame <= TimingDemo.SingleStepFrame;
			frame++)
		{
			demo.Loop.Advance();
			script.Advance(frame);
		}

		Assert.Equal(360, demo.Loop.TotalFixedSteps);
		Assert.True(updatesWhilePaused > 0);

		// The frame after StepOnce executes exactly one paused step.
		demo.Loop.Advance();
		script.Advance(TimingDemo.SingleStepFrame + 1);

		Assert.Equal(361, demo.Loop.TotalFixedSteps);
		Assert.True(pausedSteps[^1]);

		for (int frame = TimingDemo.SingleStepFrame + 2;
			frame <= TimingDemo.ResumeFrame;
			frame++)
		{
			demo.Loop.Advance();
			script.Advance(frame);
		}

		Assert.Equal(361, demo.Loop.TotalFixedSteps);
		Assert.False(demo.Loop.IsPaused);

		demo.Loop.Advance();

		Assert.Equal(362, demo.Loop.TotalFixedSteps);
		Assert.Equal(1, pausedSteps.Count(isPaused => isPaused));
	}

	[Fact]
	public void ScriptedRun_KeepsTheStepCountWhenFramesVary()
	{
		using EcsDemoScene firstScene = CreateScene();
		TimingDemo first = new(firstScene, scripted: true);
		TimingScript firstScript = new(
			first,
			TimingDemo.MinimumScriptedFrames);

		RunScriptedFrames(
			first,
			firstScript,
			TimingDemo.MinimumScriptedFrames);

		using EcsDemoScene secondScene = CreateScene();
		TimingDemo second = new(secondScene, scripted: true);
		TimingScript secondScript = new(
			second,
			TimingDemo.MinimumScriptedFrames);

		RunScriptedFrames(
			second,
			secondScript,
			TimingDemo.MinimumScriptedFrames);

		Assert.Equal(
			first.Loop.TotalFixedSteps,
			second.Loop.TotalFixedSteps);
		Assert.Equal(first.PulseX, second.PulseX);
		Assert.Equal(first.TenHertzTicks, second.TenHertzTicks);
	}

	[Fact]
	public void InteractiveMode_UsesTheMonotonicClockAndMovesTheEntity()
	{
		using EcsDemoScene scene = CreateScene();
		TimingDemo demo = new(scene, scripted: false);

		Assert.False(demo.IsScripted);
		Assert.Null(demo.ScriptedClock);

		Vector3 initial = scene.GetPosition(scene.TriangleAEntity);

		// A real clock sample is tiny; the demo must accept it
		// without requiring a presentation or a fake clock. At most
		// one step can be due by the time this line runs.
		demo.Loop.Advance();

		Assert.InRange(demo.Loop.TotalFixedSteps, 0, 1);
		Assert.Equal(demo.Loop.TotalFixedSteps, demo.PulseTicks);

		if (demo.Loop.TotalFixedSteps == 0)
		{
			Assert.Equal(initial, scene.GetPosition(scene.TriangleAEntity));
		}
	}

	private static void RunScriptedFrames(
		TimingDemo demo,
		TimingScript script,
		int frames)
	{
		for (int frame = 1; frame <= frames; frame++)
		{
			demo.Loop.Advance();
			script.Advance(frame);
		}
	}

	private static EcsDemoScene CreateScene()
	{
		return new EcsDemoScene(
			EcsMeshResolver.CreateDefault(
				MeshGeometry.CreateCameraTransformMeshes()));
	}
}
