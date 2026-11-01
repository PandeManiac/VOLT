using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Gameplay;

/// <summary>
/// Covers how a project's unmanaged component becomes readable gameplay
/// state in the runtime inspector instead of an anonymous size.
/// </summary>
[Collection("GameplayCatalog")]
public sealed class RuntimeComponentFormattingTests : IDisposable
{
	private struct UndescribedState
	{
		public int Value;
	}

	private struct ThrowingState
	{
		public int Value;
	}

	public void Dispose()
	{
		RuntimeComponentFormatters.Unregister<ThrowingState>();
		RuntimeComponentFormatters.Unregister<UndescribedState>();
	}

	[Fact]
	public void AProjectComponentShowsItsLiveValues()
	{
		ComponentCatalog catalog = new();

		using GameplayAssembly assembly = GameplayAssembly.Load(
			GameplayFixtureAssembly.Path,
			catalog).Assembly!;

		IRuntimeComponentFormatter description = Assert.Single(
			assembly.Registration.Descriptions);

		Assert.Equal("Fixture Spin State", description.DisplayName);
		Assert.Equal("FixtureSpinState", description.TypeName);

		SceneDocument scene = new("Fixture");
		SceneEntity entity = scene.CreateEntity("Spinner");
		entity.AddComponent(catalog.Get("volt.fixture.spin").Create());

		using RuntimeScene runtime = new(
			scene,
			componentBindings: assembly.Registration.Bindings,
			systems: assembly.Registration.Systems);

		RuntimeEntityInfo info = Assert.Single(runtime.Entities);

		runtime.Update(PlaySession.FixedDeltaSeconds);

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Playing,
			entity.Id);

		RuntimeComponentSnapshot component = Assert.Single(
			snapshot.SelectedEntity!.Components,
			candidate => candidate.TypeName == "FixtureSpinState");

		// The description supplies the name and the values, so the panel
		// shows real gameplay state rather than a size.
		Assert.Equal("Fixture Spin State", component.DisplayName);
		Assert.Null(component.Note);

		RuntimeFieldSnapshot field = Assert.Single(component.Fields);

		Assert.Equal("Radians", field.Label);
		Assert.True(
			float.Parse(field.Value) > 0f,
			$"The system advanced the state, so the displayed value " +
			$"should have advanced too, but it read '{field.Value}'.");
	}

	[Fact]
	public void AThrowingFormatterBecomesANoteInsteadOfBreakingTheCapture()
	{
		RuntimeComponentFormatters.Register<ThrowingState>(
			"Throwing",
			_ => throw new InvalidOperationException("formatter failure"));

		SceneDocument scene = new("Throwing");
		SceneEntity entity = scene.CreateEntity("Entity");

		using RuntimeScene runtime = new(scene);
		RuntimeEntityInfo info = Assert.Single(runtime.Entities);

		runtime.World.AddComponent(info.Entity, new ThrowingState { Value = 7 });

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Playing,
			entity.Id);

		RuntimeComponentSnapshot component = Assert.Single(
			snapshot.SelectedEntity!.Components,
			candidate => candidate.TypeName == "ThrowingState");

		Assert.Empty(component.Fields);
		Assert.Contains(
			"formatter failure",
			component.Note,
			StringComparison.Ordinal);

		// The rest of the entity is still described.
		Assert.Contains(
			snapshot.SelectedEntity!.Components,
			candidate => candidate.TypeName == "PositionComponent");
	}

	[Fact]
	public void AComponentWithoutADescriptionKeepsTheSizeLine()
	{
		SceneDocument scene = new("Undescribed");
		SceneEntity entity = scene.CreateEntity("Entity");

		using RuntimeScene runtime = new(scene);
		RuntimeEntityInfo info = Assert.Single(runtime.Entities);

		runtime.World.AddComponent(
			info.Entity,
			new UndescribedState { Value = 3 });

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Playing,
			entity.Id);

		RuntimeComponentSnapshot component = Assert.Single(
			snapshot.SelectedEntity!.Components,
			candidate => candidate.TypeName == "UndescribedState");

		Assert.Empty(component.Fields);
		Assert.Contains(
			"No inspector formatter",
			component.Note,
			StringComparison.Ordinal);
	}

	[Fact]
	public void NumbersFormatInvariantlyWithAtMostThreeDecimals()
	{
		Assert.Equal(
			"2.5",
			RuntimeFieldSnapshot.Number("Speed", 2.5f).Value);

		Assert.Equal(
			"0.333",
			RuntimeFieldSnapshot.Number("Third", 1f / 3f).Value);

		Assert.Equal(
			"12",
			RuntimeFieldSnapshot.Number("Twelve", 12f).Value);
	}
}
