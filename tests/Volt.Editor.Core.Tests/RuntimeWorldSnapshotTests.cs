using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Motion;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests;

/// <summary>
/// Covers the capture behind the ECS inspector: it must report the
/// runtime world's own entities and component values, keep the authored
/// identity separate from the ECS handle, and survive the world being
/// disposed.
/// </summary>
public sealed class RuntimeWorldSnapshotTests
{
	[Fact]
	public void Capture_ReportsRuntimeEntitiesAndMeasuredArchetypes()
	{
		SceneDocument scene = new("Snapshot Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(1.5f, 2f, -3f);
		scene.CreateEntity("Floor");

		using RuntimeScene runtime = new(scene);

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Playing,
			player.Id);

		Assert.Equal("Play Session", snapshot.WorldName);
		Assert.Equal(PlayState.Playing, snapshot.State);
		Assert.Equal(2, snapshot.EntityCount);

		// Entities are created in the empty archetype and migrate out of
		// it, so the world keeps that (now empty) archetype allocated.
		// Both are real; the populated one is the one to measure.
		Assert.Contains(
			snapshot.Archetypes,
			archetype => archetype.Signature == "(no components)");

		RuntimeArchetypeSnapshot populated = Assert.Single(
			snapshot.Archetypes,
			archetype => archetype.EntityCount == 2);

		Assert.Equal(1, populated.ChunkCount);
		Assert.Contains("Position", populated.Signature);
		Assert.Contains("Rotation", populated.Signature);
		Assert.Contains("Scale", populated.Signature);
	}

	[Fact]
	public void Capture_KeepsTheAuthoredIdentityBesideTheRuntimeHandle()
	{
		SceneDocument scene = new("Snapshot Scene");
		SceneEntity player = scene.CreateEntity("Player");
		player.Transform.Position = new Vector3(4f, 5f, 6f);

		using RuntimeScene runtime = new(scene);

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Paused,
			player.Id);

		Assert.NotNull(snapshot.SelectedEntity);
		Assert.Equal(player.Id, snapshot.SelectedEntity!.AuthoredId);
		Assert.Equal("Player", snapshot.SelectedEntity.Name);
		Assert.Equal(
			runtime.Find(player.Id)!.Entity,
			snapshot.SelectedEntity.Handle);

		RuntimeComponentSnapshot position = Assert.Single(
			snapshot.SelectedEntity.Components,
			component => component.TypeName == nameof(PositionComponent));

		Assert.Equal("4", position.Fields.Single(field => field.Label == "X").Value);
		Assert.Equal("5", position.Fields.Single(field => field.Label == "Y").Value);
		Assert.Equal("6", position.Fields.Single(field => field.Label == "Z").Value);
	}

	[Fact]
	public void Capture_WithoutASelection_LeavesTheEntityEmpty()
	{
		SceneDocument scene = new("Snapshot Scene");
		scene.CreateEntity("Player");

		using RuntimeScene runtime = new(scene);

		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime,
			PlayState.Playing,
			null);

		Assert.Null(snapshot.SelectedEntity);
	}

	[Fact]
	public void Capture_AfterTheWorldIsGone_ReturnsTheEmptySnapshot()
	{
		RuntimeWorldSnapshot snapshot = RuntimeWorldSnapshot.Capture(
			runtime: null,
			PlayState.Stopped,
			Guid.NewGuid());

		Assert.Same(RuntimeWorldSnapshot.Empty, snapshot);
		Assert.Equal(0, snapshot.EntityCount);
		Assert.Empty(snapshot.Archetypes);
	}

	[Fact]
	public void RunQuery_UsesTheRealWorldQuery()
	{
		SceneDocument scene = new("Snapshot Scene");
		scene.CreateEntity("Player");
		scene.CreateEntity("Floor");

		using RuntimeScene runtime = new(scene);

		RuntimeQueryResult result = RuntimeWorldSnapshot.RunQuery(
			runtime,
			[ComponentRegistry.Get<PositionComponent>()]);

		Assert.Equal(2, result.EntityCount);
		Assert.Equal(1, result.ArchetypeCount);
		Assert.Single(result.Signatures);
	}

	[Fact]
	public void RunQuery_WithAnAbsentComponent_MatchesNothing()
	{
		SceneDocument scene = new("Snapshot Scene");
		scene.CreateEntity("Player");

		using RuntimeScene runtime = new(scene);

		RuntimeQueryResult result = RuntimeWorldSnapshot.RunQuery(
			runtime,
			[ComponentRegistry.Get<VelocityComponent>()]);

		Assert.Equal(0, result.EntityCount);
		Assert.Equal(0, result.ArchetypeCount);
		Assert.Empty(result.Signatures);
	}
}
