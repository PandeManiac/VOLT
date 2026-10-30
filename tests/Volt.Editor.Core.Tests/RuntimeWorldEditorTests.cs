using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Motion;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests;

/// <summary>
/// Covers the write-back rules behind runtime editing: a value changes
/// the live ECS world, nothing reaches the authored scene, and the
/// editor refuses work it cannot do safely.
/// </summary>
public sealed class RuntimeWorldEditorTests
{
	[Fact]
	public void Apply_Position_ChangesTheWorldAndNotTheAuthoredScene()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using RuntimeScene runtime = new(scene);

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(7f, 8f, 9f));

		Assert.True(result.Applied);

		Entity entity = runtime.Find(player.Id)!.Entity;

		Assert.Equal(
			new Vector3(7f, 8f, 9f),
			runtime.World.GetComponent<PositionComponent>(entity).Value);

		// The saved transform is the source of truth for the next Play
		// session and must not move because the simulation was nudged.
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
	}

	[Fact]
	public void Apply_Scale_WritesTheStoredComponent()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");

		using RuntimeScene runtime = new(scene);

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Scale,
			new Vector3(2f, 3f, 4f));

		Assert.True(result.Applied);
		Assert.Equal(
			new Vector3(2f, 3f, 4f),
			runtime.World.GetComponent<ScaleComponent>(
				runtime.Find(player.Id)!.Entity).Value);
		Assert.Equal(Vector3.One, player.Transform.Scale);
	}

	[Fact]
	public void Apply_MissingComponent_IsRefusedWithoutStructuralChange()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");

		using RuntimeScene runtime = new(scene);
		Entity entity = runtime.Find(player.Id)!.Entity;

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Velocity,
			new Vector3(1f, 0f, 0f));

		Assert.False(result.Applied);
		Assert.Contains("not present", result.Message);
		Assert.False(runtime.World.HasComponent<VelocityComponent>(entity));
	}

	[Fact]
	public void Apply_UnknownAuthoredId_IsRefused()
	{
		SceneDocument scene = new("Edit Scene");
		scene.CreateEntity("Player");

		using RuntimeScene runtime = new(scene);

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			runtime,
			Guid.NewGuid(),
			RuntimeEditChannel.Position,
			Vector3.Zero);

		Assert.False(result.Applied);
		Assert.Contains("no runtime entity", result.Message);
	}

	[Fact]
	public void Apply_NonFiniteValue_IsRefused()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using RuntimeScene runtime = new(scene);

		RuntimeEditResult result = RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(float.NaN, 2f, 3f));

		Assert.False(result.Applied);
		Assert.Contains("finite", result.Message);
		Assert.Equal(
			new Vector3(1f, 2f, 3f),
			runtime.World.GetComponent<PositionComponent>(
				runtime.Find(player.Id)!.Entity).Value);
	}

	[Fact]
	public void Apply_ThroughSession_StopsAtThePlayModeBoundary()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using PlaySession session = new(scene);

		RuntimeEditResult stopped = RuntimeWorldEditor.Apply(
			session,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(4f, 5f, 6f));

		Assert.False(stopped.Applied);

		RuntimeScene runtime = session.Start();

		RuntimeEditResult playing = RuntimeWorldEditor.Apply(
			session,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(4f, 5f, 6f));

		Assert.True(playing.Applied);
		Assert.Equal(
			new Vector3(4f, 5f, 6f),
			runtime.World.GetComponent<PositionComponent>(
				runtime.Find(player.Id)!.Entity).Value);

		session.Stop();

		RuntimeEditResult afterStop = RuntimeWorldEditor.Apply(
			session,
			player.Id,
			RuntimeEditChannel.Position,
			Vector3.Zero);

		Assert.False(afterStop.Applied);
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
	}

	[Fact]
	public void ApplyToScene_CopiesTheRuntimePositionAsOneUndoStep()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(7f, 8f, 9f));

		Assert.Equal(0, undo.UndoCount);

		RuntimeEditResult result = RuntimeWorldEditor.ApplyToScene(
			session,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.Position);

		Assert.True(result.Applied);
		Assert.Contains("undo step", result.Message);
		Assert.Equal(new Vector3(7f, 8f, 9f), player.Transform.Position);
		Assert.Equal(1, undo.UndoCount);
		Assert.Equal("Transform Player", undo.UndoDescription);

		Assert.True(undo.Undo());
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(1, undo.RedoCount);

		Assert.True(undo.Redo());
		Assert.Equal(new Vector3(7f, 8f, 9f), player.Transform.Position);
	}

	[Fact]
	public void ApplyToScene_Scale_KeepsRotationAndPosition()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);
		player.Transform.EulerAngles = new Vector3(10f, 20f, 30f);

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Scale,
			new Vector3(2f, 3f, 4f));

		RuntimeEditResult result = RuntimeWorldEditor.ApplyToScene(
			runtime,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.Scale);

		Assert.True(result.Applied);
		Assert.Equal(new Vector3(2f, 3f, 4f), player.Transform.Scale);
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(new Vector3(10f, 20f, 30f), player.Transform.EulerAngles);
		Assert.Equal(1, undo.UndoCount);
	}

	[Fact]
	public void ApplyTransformToScene_CopiesPositionAndScaleAsOneUndoStep()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(4f, 5f, 6f));

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Scale,
			new Vector3(2f, 3f, 4f));

		Assert.Equal(0, undo.UndoCount);

		RuntimeEditResult result = RuntimeWorldEditor.ApplyTransformToScene(
			session,
			scene,
			undo,
			player.Id);

		Assert.True(result.Applied);
		Assert.Contains("one undo step", result.Message);
		Assert.Equal(new Vector3(4f, 5f, 6f), player.Transform.Position);
		Assert.Equal(new Vector3(2f, 3f, 4f), player.Transform.Scale);
		Assert.Equal(1, undo.UndoCount);
		Assert.Equal("Transform Player", undo.UndoDescription);

		Assert.True(undo.Undo());
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(Vector3.One, player.Transform.Scale);

		Assert.True(undo.Redo());
		Assert.Equal(new Vector3(4f, 5f, 6f), player.Transform.Position);
		Assert.Equal(new Vector3(2f, 3f, 4f), player.Transform.Scale);
	}

	[Fact]
	public void ApplyTransformToScene_UnchangedTransformCreatesNoUndoStep()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);
		player.Transform.Scale = new Vector3(2f, 2f, 2f);

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeEditResult result = RuntimeWorldEditor.ApplyTransformToScene(
			runtime,
			scene,
			undo,
			player.Id);

		Assert.False(result.Applied);
		Assert.Contains("already matches", result.Message);
		Assert.Equal(0, undo.UndoCount);
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(new Vector3(2f, 2f, 2f), player.Transform.Scale);
	}

	[Fact]
	public void ApplyTransformToScene_RefusesStoppedPlayMode()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		UndoStack undo = new();

		using PlaySession session = new(scene);

		RuntimeEditResult result = RuntimeWorldEditor.ApplyTransformToScene(
			session,
			scene,
			undo,
			player.Id);

		Assert.False(result.Applied);
		Assert.Contains("Start Play Mode", result.Message);
		Assert.Equal(0, undo.UndoCount);
	}

	[Fact]
	public void ApplyToScene_RefusesMotionChannelsWithoutAnAuthoredCounterpart()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeEditResult velocity = RuntimeWorldEditor.ApplyToScene(
			runtime,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.Velocity);

		Assert.False(velocity.Applied);
		Assert.Contains("no authored counterpart", velocity.Message);

		RuntimeEditResult angular = RuntimeWorldEditor.ApplyToScene(
			runtime,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.AngularVelocity);

		Assert.False(angular.Applied);
		Assert.Contains("no authored counterpart", angular.Message);
		Assert.Equal(0, undo.UndoCount);
	}

	[Fact]
	public void ApplyToScene_UnchangedValueCreatesNoUndoStep()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using PlaySession session = new(scene);
		RuntimeScene runtime = session.Start();
		UndoStack undo = new();

		RuntimeEditResult result = RuntimeWorldEditor.ApplyToScene(
			runtime,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.Position);

		Assert.False(result.Applied);
		Assert.Contains("already matches", result.Message);
		Assert.Equal(0, undo.UndoCount);
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
	}

	[Fact]
	public void ApplyToScene_StopsAtThePlayModeBoundary()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		using PlaySession session = new(scene);
		UndoStack undo = new();

		RuntimeEditResult stopped = RuntimeWorldEditor.ApplyToScene(
			session,
			scene,
			undo,
			player.Id,
			RuntimeEditChannel.Position);

		Assert.False(stopped.Applied);
		Assert.Contains("Start Play Mode", stopped.Message);
		Assert.Equal(0, undo.UndoCount);
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
	}

	[Fact]
	public void Capture_MarksEditableComponentsAndKeepsRotationReadOnly()
	{
		SceneDocument scene = new("Edit Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(4f, 5f, 6f);
		player.Transform.EulerAngles = new Vector3(10f, 20f, 30f);

		using RuntimeScene runtime = new(scene);

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Paused,
			player.Id);

		RuntimeComponentSnapshot position = Assert.Single(
			snapshot.SelectedEntity!.Components,
			component => component.TypeName == nameof(PositionComponent));

		Assert.Equal(RuntimeEditChannel.Position, position.Editable);
		Assert.Equal(new Vector3(4f, 5f, 6f), position.Value.GetValueOrDefault());

		RuntimeComponentSnapshot rotation = Assert.Single(
			snapshot.SelectedEntity.Components,
			component => component.TypeName == nameof(RotationComponent));

		Assert.Null(rotation.Editable);
		Assert.Null(rotation.Value);
		Assert.NotNull(rotation.Note);
		Assert.Equal(4, rotation.Fields.Count);
	}
}
