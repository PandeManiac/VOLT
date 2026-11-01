using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Gameplay;

/// <summary>
/// Verifies the shipped sample project's own game code through the public
/// workflow, with no editor involved.
/// </summary>
/// <remarks>
/// This is the standalone-execution acceptance test: open the sample
/// project, load its built assembly, read the scene, play it, and see the
/// project's system move the player. The assembly is built by the solution
/// and by this project's reference, so the test never depends on a leftover
/// file being current.
/// </remarks>
[Collection("GameplayCatalog")]
public sealed class SampleGameTests
{
	[Fact]
	public void TheSampleProjectRunsItsOwnGameCode()
	{
		string sampleRoot = FindSampleProject();

		EditorProject project = EditorProject.Open(sampleRoot);

		Assert.True(
			project.TryResolveGameplayAssembly(out string assemblyPath),
			"The sample's game code has not been built.");

		GameplayAssemblyLoadResult loaded = GameplayAssembly.Load(
			assemblyPath,
			ComponentCatalog.Default);

		Assert.True(loaded.Succeeded, loaded.Error);

		using GameplayAssembly assembly = loaded.Assembly!;

		Assert.Contains(
			assembly.Registration.Systems,
			system => system.GetType().Name == "PlayerControlSystem");

		// Reading the scene after the load is the actual workflow: the
		// component must come back as its real type, not unknown data.
		SceneDocument scene = project.LoadStartupScene();

		SceneEntity player = scene.Entities.First(
			entity => entity.Name == "Player");

		AuthoredComponent component =
			player.GetComponent("playdemo.playerControl") ??
				throw new InvalidOperationException(
					"The sample's player has no Player Control component.");

		Assert.IsNotType<UnknownComponentData>(component);
		Assert.Equal("PlayerControlComponent", component.GetType().Name);

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

		Vector3 authored = player.Transform.Position;

		session.Input.MoveRight = true;

		for (int step = 0; step < 30; step++)
		{
			session.Update(PlaySession.FixedDeltaSeconds);
		}

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out PositionComponent after));

		Assert.True(
			after.Value.X > before.Value.X,
			$"The sample's system should move the player along +X, but it " +
			$"went from {before.Value.X} to {after.Value.X}.");

		// Range clamps the player, and the authored scene is untouched.
		Assert.True(after.Value.X <= 5f);
		Assert.Equal(authored, player.Transform.Position);
	}

	private static string FindSampleProject()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Volt.slnx")))
			{
				return Path.Combine(
					directory.FullName,
					"samples",
					"Volt.PlayDemo",
					"SampleGame");
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			$"The repository root could not be found from " +
			$"'{AppContext.BaseDirectory}'.");
	}
}
