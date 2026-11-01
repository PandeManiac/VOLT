using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies the editor half of project-owned gameplay: an explicit build
/// makes the project's components editable through the same catalog and
/// session calls the Inspector uses, and Play Mode runs the project's own
/// systems against real ECS state.
/// </summary>
public sealed class GameplaySessionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-gameplay-editor-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public GameplaySessionTests()
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
			// A leftover file must not fail a test that already ran.
		}
	}

	[Fact]
	public void BuildingGameCodeMakesItsComponentsEditable()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		GameplayBuildResult? build = _session.BuildGameCode();

		Assert.NotNull(build);
		Assert.True(
			build!.Succeeded,
			build.FailureMessage ??
				string.Join(Environment.NewLine, build.Output));

		Assert.NotNull(_session.Gameplay);
		Assert.Equal("GameScripts", _session.Gameplay!.AssemblyName);

		// The Add Component list is built from this catalog, so the
		// project's component is now offered with no editor changes.
		Assert.True(
			ComponentCatalog.Default.TryGet("game.playerMotor", out _));

		Assert.Contains(
			_session.LogEntries,
			entry => entry.Message.Contains(
				"Loaded game code",
				StringComparison.Ordinal));
	}

	[Fact]
	public void StartPlayRunsTheProjectsSystemAndKeepsTheAuthoredScene()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");
		Assert.NotNull(_session.BuildGameCode());

		SceneEntity cube = _session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		Vector3 authored = cube.Transform.Position;

		// The same call the Inspector's Add Component button makes.
		_session.AddComponent(cube.Id, "game.playerMotor");

		Assert.NotNull(cube.GetComponent("game.playerMotor"));

		_session.StartPlay();

		Assert.Equal(PlayState.Playing, _session.PlayState);

		RuntimeScene runtime = _session.Play!.Runtime!;
		RuntimeEntityInfo info = runtime.Find(cube.Id)!;

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out PositionComponent before));

		_session.Input.MoveRight = true;

		for (int frame = 0; frame < 30; frame++)
		{
			_session.Update(PlaySession.FixedDeltaSeconds);
		}

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out PositionComponent after));

		Assert.True(
			after.Value.X > before.Value.X,
			$"The project's system should have moved the cube along +X, " +
			$"but it went from {before.Value.X} to {after.Value.X}.");

		// The ECS panel renders this snapshot, so the developer's own
		// component must be described with live values rather than a size.
		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			_session.PlayState,
			cube.Id);

		// The scaffolded project's component, named by the project's own
		// description rather than by a size line.
		RuntimeComponentSnapshot projectComponent = Assert.Single(
			snapshot.SelectedEntity!.Components,
			candidate => candidate.TypeName == "PlayerMotorState");

		Assert.Equal("Player Motor", projectComponent.DisplayName);

		Assert.Contains(
			projectComponent.Fields,
			field => field.Label == "Speed");

		_session.Stop();

		// Play Mode discards its world and never writes back.
		Assert.Equal(authored, cube.Transform.Position);
		Assert.Equal(PlayState.Stopped, _session.PlayState);
	}

	[Fact]
	public void ABrokenBuildIsReportedAndLeavesTheEditorUsable()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		File.WriteAllText(
			Path.Combine(_root, "Game", "Scripts", "Game.cs"),
			"not valid C#;");

		GameplayBuildResult? build = _session.BuildGameCode();

		Assert.NotNull(build);
		Assert.False(build!.Succeeded);
		Assert.Null(_session.Gameplay);

		// The Console names the file, so the fix is locatable.
		Assert.Contains(
			_session.LogEntries,
			entry => entry.Level == EditorLogLevel.Error &&
				entry.Message.Contains("Game.cs", StringComparison.Ordinal));

		// The editor still works: the scene plays with built-in
		// behaviours only.
		_session.StartPlay();

		Assert.Equal(PlayState.Playing, _session.PlayState);
		Assert.NotNull(_session.Play!.Runtime);

		_session.Stop();
	}
}
