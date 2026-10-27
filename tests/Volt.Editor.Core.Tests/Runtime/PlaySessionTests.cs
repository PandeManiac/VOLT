using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Runtime;

public sealed class PlaySessionTests
{
	[Fact]
	public void Start_BuildsRealEcsWorldAndStopsWithoutMutatingAuthoredScene()
	{
		SceneDocument scene = new("Play");
		SceneEntity camera = scene.CreateEntity("Camera");
		camera.AddComponent(new CameraData());
		SceneEntity moving = scene.CreateEntity("Moving");
		moving.AddComponent(new MeshRendererData
		{
			Mesh = "builtin:cube",
			Material = "builtin:default"
		});
		moving.AddComponent(new SpinBehaviourData
		{
			Axis = Vector3.UnitY,
			DegreesPerSecond = 180f
		});

		string before = SceneSerializer.Serialize(scene);
		using PlaySession session = new(scene);

		RuntimeScene runtime = session.Start();

		Assert.Equal(PlayState.Playing, session.State);
		Assert.Equal(2, runtime.World.Count);
		Assert.Equal(1, runtime.RenderableCount);
		Assert.NotNull(runtime.Camera);

		RuntimeEntityInfo movingRuntime = runtime.Find(moving.Id)!;
		Quaternion initial = runtime.World
			.GetComponent<RotationComponent>(movingRuntime.Entity)
			.Value;

		Assert.Equal(1, session.Update(1f / 60f));

		Quaternion updated = runtime.World
			.GetComponent<RotationComponent>(movingRuntime.Entity)
			.Value;

		Assert.NotEqual(initial, updated);

		session.Stop();

		Assert.Equal(PlayState.Stopped, session.State);
		Assert.Null(session.Runtime);
		Assert.Equal(before, SceneSerializer.Serialize(scene));
		Assert.Equal(0f, moving.Transform.EulerAngles.Y);
	}

	[Fact]
	public void PauseAndStep_AdvanceExactlyOneFixedTick()
	{
		SceneDocument scene = new("Pause");
		SceneEntity moving = scene.CreateEntity("Moving");
		moving.AddComponent(new SpinBehaviourData());
		using PlaySession session = new(scene);

		session.Start();
		session.Pause();

		int ticksBefore = session.Runtime!.TickCount;

		Assert.Equal(0, session.Update(1f));
		Assert.True(session.Step());
		Assert.Equal(ticksBefore + 1, session.Runtime.TickCount);

		session.Resume();
		Assert.Equal(1, session.Update(1f / 60f));
	}

	[Fact]
	public void RuntimeParenting_UsesWorldTransformsAndInputMovesEntity()
	{
		SceneDocument scene = new("Parenting");
		SceneEntity parent = scene.CreateEntity("Parent");
		parent.Transform.Position = new Vector3(2f, 0f, 0f);

		SceneEntity child = scene.CreateEntity("Child", parent.Id);
		child.Transform.Position = new Vector3(1f, 0f, 0f);
		child.AddComponent(new MoveBehaviourData
		{
			Automatic = false,
			Axis = Vector3.UnitX,
			Speed = 6f
		});

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();

		RuntimeEntityInfo childRuntime = runtime.Find(child.Id)!;
		Vector3 start = runtime.World
			.GetComponent<PositionComponent>(childRuntime.Entity)
			.Value;

		Assert.Equal(new Vector3(3f, 0f, 0f), start);

		session.Input.MoveRight = true;
		session.StepForTest();

		Vector3 moved = runtime.World
			.GetComponent<PositionComponent>(childRuntime.Entity)
			.Value;

		Assert.True(moved.X > start.X);
		Assert.Equal(0f, moved.Y);
	}

	[Fact]
	public void Dispose_ReleasesRuntimeWorldAndMissingAssetsAreReported()
	{
		SceneDocument scene = new("Dispose");
		SceneEntity entity = scene.CreateEntity("Missing Mesh");
		entity.AddComponent(new MeshRendererData
		{
			Mesh = "Assets/missing.mesh"
		});

		using PlaySession session = new(
			scene,
			assetExists: _ => false);

		RuntimeScene runtime = session.Start();

		Assert.Contains(
			runtime.Diagnostics,
			message => message.Contains("missing mesh asset"));

		session.Stop();

		Assert.Throws<ObjectDisposedException>(() => _ = runtime.World.Count);
	}
}

internal static class PlaySessionTestExtensions
{
	public static void StepForTest(this PlaySession session)
	{
		session.Pause();
		session.Step();
	}
}
