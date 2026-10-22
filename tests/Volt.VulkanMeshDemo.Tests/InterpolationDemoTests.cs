using System.Numerics;
using Volt.Core.Timing;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Extraction;
using Volt.Rendering.Transforms;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class InterpolationDemoTests
{
	[Fact]
	public void ScriptedRun_ReachesTheDocumentedFinalState()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: true);
		InterpolationScript script = new(demo, InterpolationDemo.MinimumScriptedFrames);

		RunFrames(demo, script, InterpolationDemo.MinimumScriptedFrames);

		Assert.Equal(532, demo.Loop.TotalFixedSteps);
		Assert.Equal(532, demo.CapturedSteps);
		Assert.Equal(17, demo.ScheduledRotations);
		Assert.False(demo.Loop.IsPaused);
		Assert.Equal(TimeScale.Normal, demo.Loop.TimeScale);
		Assert.Equal(TimeSpan.Zero, demo.Loop.TotalDroppedTime);
		Assert.Equal(TimeSpan.Zero, demo.Loop.TotalDroppedSimulationTime);
		Assert.Equal(4, demo.History.Count);

		Assert.Equal(
			demo.MotionX(532),
			demo.MovingX,
			4);

		Assert.Equal(
			demo.MovingX,
			scene.GetPosition(scene.TriangleAEntity).X,
			4);
	}

	[Fact]
	public void ScriptedRun_HistoryMatchesTheLastTwoSteps()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: true);
		InterpolationScript script = new(demo, InterpolationDemo.MinimumScriptedFrames);

		RunFrames(demo, script, InterpolationDemo.MinimumScriptedFrames);

		Assert.True(demo.History.TryGetPose(
			demo.MovingEntity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(demo.MotionX(531), previous.Position.X, 4);
		Assert.Equal(demo.MotionX(532), current.Position.X, 4);
	}

	[Fact]
	public void ScriptedRun_InterpolationNeverWritesToTheEcs()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: true);
		InterpolationScript script = new(demo, InterpolationDemo.MinimumScriptedFrames);

		RunFrames(demo, script, InterpolationDemo.MinimumScriptedFrames);

		Vector3 position =
			scene.GetPosition(scene.TriangleAEntity);

		Quaternion rotation =
			scene.GetRotation(scene.TriangleAEntity);

		Vector3 scale = scene.GetScale(scene.TriangleAEntity);

		InterpolatedRenderExtractor interpolated =
			new(scene.World);

		EcsRenderExtractor direct = new(scene.World);

		RenderInstance[] interpolatedInstances =
			new RenderInstance[8];

		RenderInstance[] directInstances = new RenderInstance[8];

		interpolated.Extract(
			interpolatedInstances,
			demo.History,
			0.5f);

		direct.Extract(directInstances);

		Assert.Equal(
			position,
			scene.GetPosition(scene.TriangleAEntity));
		Assert.Equal(
			rotation,
			scene.GetRotation(scene.TriangleAEntity));
		Assert.Equal(
			scale,
			scene.GetScale(scene.TriangleAEntity));

		// The interpolated matrix differs because alpha 0.5 is
		// between the two committed poses, not because the ECS moved.
		RenderInstance movingInterpolated = Find(
			interpolatedInstances,
			demo.MovingEntity);

		RenderInstance movingDirect = Find(
			directInstances,
			demo.MovingEntity);

		Assert.NotEqual(movingInterpolated.Model, movingDirect.Model);
	}

	[Fact]
	public void ProbeRun_HoldsTheQuarterStepInterpolation()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: true);
		InterpolationScript script = new(
			demo,
			InterpolationDemo.MinimumScriptedFrames,
			probe: true);

		RunFrames(demo, script, InterpolationDemo.MinimumScriptedFrames);

		Assert.Equal(121, demo.Loop.TotalFixedSteps);
		Assert.Equal(121, demo.CapturedSteps);
		Assert.Equal(0.25f, demo.Loop.LastFrame.InterpolationAlpha, 6);
		Assert.Equal(TimeScale.Normal, demo.Loop.TimeScale);
		Assert.False(demo.Loop.IsPaused);

		// The last completed step is the checkpoint, so the held
		// render pose stays between the two committed poses for the
		// whole run.
		Assert.Equal(demo.MotionX(121), demo.MovingX, 4);

		Assert.True(demo.History.TryGetPose(
			demo.MovingEntity,
			out TransformPose previous,
			out TransformPose current));

		Assert.Equal(demo.MotionX(120), previous.Position.X, 4);
		Assert.Equal(demo.MotionX(121), current.Position.X, 4);
	}

	[Fact]
	public void ScriptedRun_RejectsRunsShorterThanThePlan()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: true);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new InterpolationScript(
				demo,
				InterpolationDemo.MinimumScriptedFrames - 1));
	}

	[Fact]
	public void InteractiveDemo_UsesTheSharedClock()
	{
		using EcsDemoScene scene = CreateScene();
		InterpolationDemo demo = new(scene, scripted: false);

		Assert.False(demo.IsScripted);
		Assert.Equal(TimeScale.Normal, demo.Loop.TimeScale);
		Assert.Equal(4, demo.History.Count);
	}

	private static void RunFrames(
		InterpolationDemo demo,
		InterpolationScript script,
		int frames)
	{
		for (int frame = 1; frame <= frames; frame++)
		{
			demo.Loop.Advance();
			demo.View.Extract();
			script.Advance(frame);
		}
	}

	private static EcsDemoScene CreateScene()
	{
		return new EcsDemoScene(
			EcsMeshResolver.CreateDefault(
				MeshGeometry.CreateCameraTransformMeshes()));
	}

	private static RenderInstance Find(
		ReadOnlySpan<RenderInstance> instances,
		Volt.Ecs.Entity entity)
	{
		foreach (RenderInstance instance in instances)
		{
			if (instance.Entity == entity)
			{
				return instance;
			}
		}

		throw new InvalidOperationException(
			$"Entity {entity} was not extracted.");
	}
}
