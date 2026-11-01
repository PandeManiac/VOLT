using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Gameplay;

/// <summary>
/// Covers loading a project's gameplay assembly.
/// </summary>
/// <remarks>
/// The catalog is process-wide and these tests register into its default
/// instance, so they share a collection with the project workflow tests
/// instead of running beside them.
/// </remarks>
[Collection("GameplayCatalog")]
public sealed class GameplayAssemblyTests
{
	[Fact]
	public void LoadingRegistersComponentsBindingsAndSystems()
	{
		ComponentCatalog catalog = new();

		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			catalog);

		Assert.True(result.Succeeded, result.Error);

		using GameplayAssembly assembly = result.Assembly!;
		GameplayRegistrar registration = assembly.Registration;

		EditorComponentDescriptor component = Assert.Single(
			registration.Components);

		Assert.Equal("volt.fixture.spin", component.TypeId);
		Assert.Equal("Fixture Spin", component.DisplayName);

		GameplayComponentBinding binding = Assert.Single(
			registration.Bindings,
			candidate => candidate.AuthoredTypeId == "volt.fixture.spin");

		Assert.Equal("FixtureSpinState", binding.ComponentTypeName);

		Assert.Contains(
			registration.Systems,
			system => system.GetType().Name == "FixtureSpinSystem");

		// The editor sees the project's component through the same
		// catalog it uses for built-in components.
		Assert.True(catalog.TryGet("volt.fixture.spin", out _));

		// A binding whose component is never registered is reported
		// rather than silently doing nothing.
		Assert.Contains(
			registration.Diagnostics,
			message => message.Contains(
				"FixtureUnboundComponent",
				StringComparison.Ordinal) &&
				message.Contains(
					"not registered with AddComponent",
					StringComparison.Ordinal));
	}

	[Fact]
	public void ASecondLoadReplacesTheCatalogEntryInsteadOfDuplicatingIt()
	{
		ComponentCatalog catalog = new();

		using GameplayAssembly first =
			GameplayAssembly.Load(GameplayFixtureAssembly.Path, catalog).Assembly!;

		using GameplayAssembly second =
			GameplayAssembly.Load(GameplayFixtureAssembly.Path, catalog).Assembly!;

		EditorComponentDescriptor[] matches =
		[
			.. catalog.All.Where(descriptor =>
				descriptor.TypeId == "volt.fixture.spin")
		];

		Assert.Single(matches);

		// The entry now describes the newer load's CLR type, so the
		// inspector adds a component the loaded systems understand.
		Assert.Same(
			second.Registration.Components[0].ComponentType,
			matches[0].ComponentType);

		Assert.NotSame(
			first.Registration.Components[0].ComponentType,
			matches[0].ComponentType);
	}

	[Fact]
	public void OneBrokenModuleDoesNotStopTheOthers()
	{
		ComponentCatalog catalog = new();

		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			catalog);

		Assert.True(result.Succeeded, result.Error);

		using GameplayAssembly assembly = result.Assembly!;

		Assert.Contains(
			assembly.Diagnostics,
			message => message.Contains(
				"ThrowingFixtureModule",
				StringComparison.Ordinal) &&
				message.Contains(
					"fixture module failure",
					StringComparison.Ordinal));

		Assert.Single(assembly.Registration.Components);
		Assert.Contains(
			assembly.Registration.Systems,
			system => system.GetType().Name == "FixtureSpinSystem");
	}

	[Fact]
	public void AMissingAssemblyFailsWithoutThrowing()
	{
		string missing = Path.Combine(
			Path.GetTempPath(),
			$"volt-missing-{Guid.NewGuid():N}.dll");

		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			missing,
			new ComponentCatalog());

		Assert.False(result.Succeeded);
		Assert.Null(result.Assembly);
		Assert.Contains(
			"does not exist",
			result.Error,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ACorruptAssemblyFailsWithoutThrowing()
	{
		string directory = Path.Combine(
			Path.GetTempPath(),
			$"volt-corrupt-{Guid.NewGuid():N}");

		Directory.CreateDirectory(directory);

		try
		{
			string path = Path.Combine(directory, "BrokenGameplay.dll");
			File.WriteAllText(path, "this is not a managed assembly");

			GameplayAssemblyLoadResult result = GameplayAssembly.Load(
				path,
				new ComponentCatalog());

			Assert.False(result.Succeeded);
			Assert.False(string.IsNullOrWhiteSpace(result.Error));
		}

		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public void AnAssemblyWithoutAModuleStillLoadsAndExplainsItself()
	{
		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			typeof(GameplayAssemblyTests).Assembly.Location,
			new ComponentCatalog());

		Assert.True(result.Succeeded, result.Error);

		using GameplayAssembly assembly = result.Assembly!;

		Assert.True(assembly.Registration.IsEmpty);
		Assert.Contains(
			assembly.Diagnostics,
			message => message.Contains(
				"declares no",
				StringComparison.Ordinal));
	}

	[Fact]
	public void UnloadReleasesTheLoadContext()
	{
		WeakReference context = LoadAndUnload(out bool unloaded);

		Assert.True(unloaded);

		for (int attempt = 0; attempt < 32 && context.IsAlive; attempt++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		Assert.False(context.IsAlive);
	}

	[Fact]
	public void ARegisteredProjectComponentRoundTripsThroughAScene()
	{
		// The serializer resolves component types through the default
		// catalog, which is exactly what an editor session uses.
		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			ComponentCatalog.Default);

		Assert.True(result.Succeeded, result.Error);

		using GameplayAssembly assembly = result.Assembly!;

		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.Get("volt.fixture.spin");

		SceneDocument scene = new("Fixture");
		SceneEntity entity = scene.CreateEntity("Spinner");
		entity.AddComponent(descriptor.Create());

		string json = SceneSerializer.Serialize(scene);
		SceneDocument reloaded = SceneSerializer.Deserialize(json);

		AuthoredComponent component = Assert.Single(
			Assert.Single(reloaded.Entities).Components);

		// Loaded as the project's own type, not preserved as unknown
		// data, which is what keeps it editable after a rebuild.
		Assert.IsNotType<UnknownComponentData>(component);
		Assert.Equal("volt.fixture.spin", component.TypeId);
		Assert.Equal("FixtureSpinComponent", component.GetType().Name);

		Assert.Equal(
			JsonValueKind.Object,
			JsonDocument.Parse(json).RootElement
				.GetProperty("entities")[0]
				.GetProperty("components")[0]
				.GetProperty("data")
				.ValueKind);
	}

	[Fact]
	public void AProjectComponentDrivesRealSystemExecution()
	{
		ComponentCatalog catalog = new();

		using GameplayAssembly assembly =
			GameplayAssembly.Load(GameplayFixtureAssembly.Path, catalog).Assembly!;

		EditorComponentDescriptor descriptor = catalog.Get("volt.fixture.spin");

		SceneDocument scene = new("Fixture");
		SceneEntity entity = scene.CreateEntity("Spinner");
		entity.AddComponent(descriptor.Create());

		using RuntimeScene runtime = new(
			scene,
			componentBindings: assembly.Registration.Bindings,
			systems: assembly.Registration.Systems);

		RuntimeEntityInfo info = Assert.Single(runtime.Entities);

		// Position, rotation, scale, and the binding's own component.
		Assert.True(runtime.World.TryGetSignature(info.Entity, out var signature));
		Assert.Equal(4, signature.Count);

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out RotationComponent before));

		runtime.Update(PlaySession.FixedDeltaSeconds);

		Assert.True(
			runtime.World.TryReadComponent(
				info.Entity,
				out RotationComponent after));

		Assert.NotEqual(before.Value, after.Value);
		Assert.Equal(1, runtime.TickCount);

		// The faulty fixture system throws every step; the simulation
		// keeps running and the failure is reported once.
		Assert.Contains(
			runtime.Diagnostics,
			message => message.Contains(
				"FixtureThrowingSystem",
				StringComparison.Ordinal));

		IGameplaySystem faulty = runtime.Systems.Single(
			system => system.GetType().Name == "FixtureThrowingSystem");

		Assert.False(runtime.IsSystemActive(faulty));

		IGameplaySystem spinSystem = runtime.Systems.Single(
			system => system.GetType().Name == "FixtureSpinSystem");

		Assert.True(runtime.IsSystemActive(spinSystem));
		Assert.Equal(1, ReadCounter(spinSystem, "StartCount"));
		Assert.Equal(1, ReadCounter(spinSystem, "UpdateCount"));

		Quaternion rotated = after.Value;

		runtime.Dispose();

		// Shutdown reaches the system once, and the world is gone.
		Assert.Equal(1, ReadCounter(spinSystem, "StopCount"));
	}

	[Fact]
	public void StoppingPlayDiscardsRuntimeStateAndKeepsAuthoredData()
	{
		// Play Mode snapshots the authored scene through the serializer,
		// which resolves component types from the default catalog, so
		// this mirrors what an editor session does.
		using GameplayAssembly assembly = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			ComponentCatalog.Default).Assembly!;

		SceneDocument scene = new("Fixture");
		SceneEntity entity = scene.CreateEntity("Spinner");
		entity.AddComponent(
			ComponentCatalog.Default.Get("volt.fixture.spin").Create());

		using PlaySession session = new(
			scene,
			componentBindings: assembly.Registration.Bindings,
			systems: assembly.Registration.Systems);

		RuntimeScene runtime = session.Start();

		for (int step = 0; step < 30; step++)
		{
			session.Update(PlaySession.FixedDeltaSeconds);
		}

		Assert.True(runtime.TickCount > 0);

		session.Stop();

		// The authored transform never carried the simulation.
		Assert.Equal(Vector3.Zero, scene.Entities[0].Transform.EulerAngles);
		Assert.Equal(PlayState.Stopped, session.State);
		Assert.Null(session.Runtime);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference LoadAndUnload(out bool unloaded)
	{
		GameplayAssemblyLoadResult result = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			new ComponentCatalog());

		Assert.True(result.Succeeded, result.Error);

		GameplayAssembly assembly = result.Assembly!;
		WeakReference reference = new(assembly.LoadContext!);

		unloaded = assembly.Unload();
		Assert.False(assembly.IsLoaded);

		return reference;
	}

	private static int ReadCounter(object system, string property)
	{
		return (int)(system.GetType().GetProperty(property)?.GetValue(system) ?? -1);
	}
}
