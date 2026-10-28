using System.Numerics;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies the editor's project, authoring, and play-mode behavior
/// without opening a window.
/// </summary>
public sealed class EditorSessionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-editor-tests-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EditorSessionTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file on some platforms must not fail a test
			// that already completed.
		}
	}

	[Fact]
	public void NewProject_OpensStarterSceneThatReopensWithSameIds()
	{
		string projectRoot = Path.Combine(_root, "Game");

		_session.NewProject(projectRoot, "Game");

		Assert.NotNull(_session.Project);
		Assert.NotNull(_session.Scene);
		Assert.NotNull(_session.ScenePath);
		Assert.True(
			File.Exists(Path.Combine(projectRoot, "project.volt.json")));

		Guid sceneId = _session.Scene!.Id;
		Guid[] entityIds = [.. _session.Scene.Entities
			.Select(entity => entity.Id)];

		_session.SaveScene();
		_session.CloseProject();
		_session.OpenProject(projectRoot);

		Assert.NotNull(_session.Scene);
		Assert.Equal(sceneId, _session.Scene!.Id);
		Assert.Equal(
			entityIds,
			_session.Scene.Entities.Select(entity => entity.Id));

		Assert.Contains(
			_session.Scene.Entities,
			entity => entity.Name == "Cube" &&
				entity.GetComponent<SpinBehaviourData>() is not null);
	}

	[Fact]
	public void PlayMode_RunsRuntimeAndStopPreservesAuthoredState()
	{
		_session.NewProject(
			Path.Combine(_root, "Play"),
			"Play");

		SceneDocument scene = _session.Scene!;
		SceneEntity cube = Assert.Single(
			scene.Entities,
			entity => entity.Name == "Cube");

		_session.Select(cube.Id);
		_session.SetTransform(
			cube.Id,
			new TransformData
			{
				Position = new Vector3(1f, 2f, 3f),
				Scale = Vector3.One
			});

		TransformData authored = cube.Transform;
		int authoredCount = scene.EntityCount;

		_session.StartPlay();
		_session.Update(0.4);

		Assert.Equal(PlayState.Playing, _session.PlayState);
		Assert.NotNull(_session.Play);
		Assert.NotNull(_session.Play!.Runtime);
		Assert.True(_session.Play.Runtime!.TickCount > 0);
		Assert.Equal(authoredCount, scene.EntityCount);
		Assert.Equal(authored.Position, cube.Transform.Position);

		_session.Stop();

		Assert.Equal(PlayState.Stopped, _session.PlayState);
		Assert.Null(_session.Play);
		Assert.Equal(authoredCount, scene.EntityCount);
		Assert.Equal(authored.Position, cube.Transform.Position);
		Assert.Equal(
			new Vector3(1f, 2f, 3f),
			cube.Transform.Position);
	}

	[Fact]
	public void TransformGesture_IsOneUndoableAction()
	{
		_session.NewProject(
			Path.Combine(_root, "Undo"),
			"Undo");

		SceneEntity cube = Assert.Single(
			_session.Scene!.Entities,
			entity => entity.Name == "Cube");

		Vector3 original = cube.Transform.Position;

		// The viewport previews a drag by mutating the authored
		// transform, then commits the gesture once.
		cube.Transform.Position = new Vector3(9f, 9f, 9f);

		_session.CommitTransform(cube.Id, new TransformData
		{
			Position = original,
			EulerAngles = Vector3.Zero,
			Scale = Vector3.One
		});

		Assert.Equal(new Vector3(9f, 9f, 9f), cube.Transform.Position);

		_session.UndoLast();

		Assert.Equal(original, cube.Transform.Position);

		_session.RedoLast();

		Assert.Equal(new Vector3(9f, 9f, 9f), cube.Transform.Position);
	}

	[Fact]
	public void Reparent_RejectsHierarchyCycles()
	{
		_session.NewProject(
			Path.Combine(_root, "Hierarchy"),
			"Hierarchy");

		SceneDocument scene = _session.Scene!;
		SceneEntity parent = scene.CreateEntity("Parent");
		SceneEntity child = scene.CreateEntity("Child", parent.Id);
		SceneEntity grandChild = scene.CreateEntity("Grandchild", child.Id);

		Assert.False(scene.CanReparent(parent.Id, grandChild.Id));
		Assert.Throws<InvalidOperationException>(
			() => scene.ReparentEntity(parent.Id, grandChild.Id));

		Assert.Null(parent.ParentId);
	}
}
