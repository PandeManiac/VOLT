using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Gameplay;

/// <summary>
/// Covers the developer workflow end to end: create a project, get real
/// game code, build it with the SDK, load it, and run the developer's own
/// system against a real world.
/// </summary>
/// <remarks>
/// These tests share the process-wide component catalog, so they run in
/// the same xunit collection as the loader tests instead of racing them.
/// </remarks>
[Collection("GameplayCatalog")]
public sealed class GameplayProjectTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, recursive: true);
		}
	}

	[Fact]
	public void CreatingAProjectWritesRealGameCode()
	{
		EditorProject project = EditorProject.Create(_root, "My Game");

		Assert.Null(project.GameplayScaffoldReason);
		Assert.Equal(
			"Scripts/GameScripts.csproj",
			project.GameplayProjectPath);

		// Before a build exists the path is recorded but unresolved; what
		// matters is that it is project-relative and stable.
		Assert.Equal(
			"Scripts/bin/Release/net10.0/GameScripts.dll",
			project.GameplayAssemblyPath);

		Assert.False(project.TryResolveGameplayAssembly(out _));

		string? projectFile = project.ResolveGameplayProjectPath();

		Assert.NotNull(projectFile);

		string code = File.ReadAllText(
			Path.Combine(_root, "Scripts", "Game.cs"));

		// The starter code is a working example of the public API, not a
		// placeholder: a component, its native state, a system, and the
		// module that registers them.
		Assert.Contains("IGameplayModule", code, StringComparison.Ordinal);
		Assert.Contains("IGameplaySystem", code, StringComparison.Ordinal);
		Assert.Contains("BindComponent<", code, StringComparison.Ordinal);
		Assert.Contains("mygame.playerMotor", code, StringComparison.Ordinal);

		string csproj = File.ReadAllText(projectFile!);

		Assert.Contains("Volt.Editor.Core.csproj", csproj, StringComparison.Ordinal);
		Assert.Contains("$(VOLT_ROOT)", csproj, StringComparison.Ordinal);
	}

	[Fact]
	public void TheScaffoldNeverOverwritesEditedCode()
	{
		EditorProject project = EditorProject.Create(_root, "My Game");

		string code = Path.Combine(_root, "Scripts", "Game.cs");
		File.WriteAllText(code, "// the developer's own code");

		GameplayScaffoldResult again = GameplayProjectScaffold.Create(
			_root,
			"My Game",
			engineRoot: project.RootPath);

		Assert.False(again.Created);
		Assert.NotNull(again.SkipReason);
		Assert.Equal("// the developer's own code", File.ReadAllText(code));
	}

	[Fact]
	public void ReopeningAProjectKeepsItsGameCodePaths()
	{
		EditorProject created = EditorProject.Create(_root, "My Game");

		EditorProject reopened = EditorProject.Open(_root);

		Assert.Equal(
			created.GameplayProjectPath,
			reopened.GameplayProjectPath);

		Assert.Equal(
			created.GameplayAssemblyPath,
			reopened.GameplayAssemblyPath);

		Assert.NotNull(reopened.ResolveGameplayProjectPath());

		// Clearing the section returns the project to built-in
		// behaviours, and must round-trip as absent rather than empty.
		reopened.AttachGameplayProject(null, null);

		Assert.Null(EditorProject.Open(_root).GameplayProjectPath);
	}

	[Fact]
	public void CompilerMessagesParseIntoFileLineAndSeverity()
	{
		string[] output =
		[
			"  Determining projects to restore...",
			"/home/dev/MyGame/Scripts/Game.cs(12,25): error CS0103: " +
				"The name 'Nope' does not exist in the current context " +
				"[/home/dev/MyGame/Scripts/GameScripts.csproj]",
			"/home/dev/MyGame/Scripts/Game.cs(4,1): warning CS0168: " +
				"The variable 'unused' is declared but never used",
			"Build FAILED."
		];

		IReadOnlyList<GameplayBuildDiagnostic> diagnostics =
			GameplayProjectBuilder.ParseDiagnostics(output);

		Assert.Equal(2, diagnostics.Count);

		GameplayBuildDiagnostic error = diagnostics[0];

		Assert.True(error.IsError);
		Assert.Equal("/home/dev/MyGame/Scripts/Game.cs", error.File);
		Assert.Equal(12, error.Line);
		Assert.Equal(25, error.Column);
		Assert.Equal("CS0103", error.Code);
		Assert.Equal(
			"The name 'Nope' does not exist in the current context",
			error.Message);

		// Describe keeps the conventional shape a developer can paste into
		// an editor.
		Assert.Equal(
			"/home/dev/MyGame/Scripts/Game.cs(12,25): error CS0103: " +
			"The name 'Nope' does not exist in the current context",
			error.Describe());

		Assert.False(diagnostics[1].IsError);
		Assert.Equal(4, diagnostics[1].Line);
	}

	[Fact]
	public void BuildingTheProjectGameCodeRunsTheDevelopersSystem()
	{
		EditorProject project = EditorProject.Create(_root, "My Game");

		GameplayBuildResult build = Build(project);

		Assert.True(
			build.Succeeded,
			build.FailureMessage ??
				string.Join(Environment.NewLine, build.Output));

		Assert.True(
			project.TryResolveGameplayAssembly(out string assemblyPath),
			"The build produced no assembly the project could resolve.");

		GameplayAssemblyLoadResult loaded = GameplayAssembly.Load(
			assemblyPath,
			ComponentCatalog.Default);

		Assert.True(loaded.Succeeded, loaded.Error);

		using GameplayAssembly assembly = loaded.Assembly!;

		AuthoredComponent component = ComponentCatalog.Default
			.Get("mygame.playerMotor")
			.Create();

		SceneDocument scene = project.LoadStartupScene();
		SceneEntity player = scene.CreateEntity("Player");
		player.AddComponent(component);

		using PlaySession session = new(
			scene,
			componentBindings: assembly.Registration.Bindings,
			systems: assembly.Registration.Systems);

		RuntimeScene runtime = session.Start();
		RuntimeEntityInfo info = runtime.Find(player.Id)!;

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out PositionComponent before));

		// The generated component's default axis is +X and the game
		// reads horizontal input, so the entity moves along X while the
		// developer's system runs.
		session.Input.MoveRight = true;

		for (int step = 0; step < 60; step++)
		{
			session.Update(PlaySession.FixedDeltaSeconds);
		}

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out PositionComponent after));

		Assert.True(
			after.Value.X > before.Value.X,
			$"Expected the player to move along +X, but it went from " +
			$"{before.Value.X} to {after.Value.X}.");

		// Play Mode never writes back: the authored transform is intact.
		Assert.Equal(Vector3.Zero, player.Transform.Position);
		Assert.Equal(0f, player.Transform.EulerAngles.X);
	}

	[Fact]
	public void ABrokenGameFileReportsAPositionWithoutBreakingTheProject()
	{
		EditorProject project = EditorProject.Create(_root, "My Game");

		File.WriteAllText(
			Path.Combine(_root, "Scripts", "Game.cs"),
			"this is not valid C#;");

		GameplayBuildResult build = Build(project);

		Assert.False(build.Succeeded);
		Assert.NotEqual(0, build.ExitCode);

		Assert.NotEmpty(build.RelevantDiagnostics);

		// A reporter reports one entry per distinct error, not one per
		// target that compiled the file.
		GameplayBuildDiagnostic error = build.RelevantDiagnostics[0];

		Assert.All(
			build.RelevantDiagnostics,
			diagnostic => Assert.True(diagnostic.IsError));

		Assert.EndsWith("Game.cs", error.File, StringComparison.Ordinal);
		Assert.True(error.Line > 0);
		Assert.StartsWith("CS", error.Code, StringComparison.Ordinal);
		Assert.Equal(
			build.RelevantDiagnostics.Count,
			build.RelevantDiagnostics
				.Select(diagnostic => diagnostic.Describe())
				.Distinct(StringComparer.Ordinal)
				.Count());

		// A build failure leaves the project openable and its scene
		// intact: only the code needs fixing.
		EditorProject reopened = EditorProject.Open(_root);

		Assert.Equal(project.Id, reopened.Id);
		Assert.True(reopened.LoadStartupScene().EntityCount >= 2);
		Assert.False(reopened.TryResolveGameplayAssembly(out _));
	}

	private static GameplayBuildResult Build(EditorProject project)
	{
		string? projectFile = project.ResolveGameplayProjectPath();

		Assert.NotNull(projectFile);

		// The engine is this process: rebuilding its projects from a game
		// build would only recompile what is already loaded.
		return GameplayProjectBuilder.Build(
			projectFile!,
			buildReferencedProjects: false);
	}
}
