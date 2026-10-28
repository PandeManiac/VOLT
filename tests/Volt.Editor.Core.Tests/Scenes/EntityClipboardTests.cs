using System.Numerics;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Scenes;

/// <summary>
/// Covers the entity clipboard as scene data: it captures a subtree,
/// survives the source scene, and pastes fresh identities into any
/// scene with the same components and internal hierarchy.
/// </summary>
public sealed class EntityClipboardTests
{
	[Fact]
	public void Capture_KeepsNestedSelectionsFromBeingCopiedTwice()
	{
		SceneDocument scene = new("Source");

		SceneEntity parent = scene.CreateEntity("Parent");
		SceneEntity child = scene.CreateEntity("Child", parent.Id);

		EntityClipboard clipboard = new();

		Assert.Equal(2, clipboard.Capture(scene, [parent.Id, child.Id]));
		Assert.Equal(2, clipboard.Count);
		Assert.Equal(1, clipboard.RootCount);
	}

	[Fact]
	public void CreateEntities_ProducesFreshIdentitiesAndCopiedData()
	{
		SceneDocument scene = new("Source");

		SceneEntity parent = scene.CreateEntity("Parent");
		SceneEntity child = scene.CreateEntity("Child", parent.Id);

		parent.Transform.Position = new Vector3(4f, 5f, 6f);
		parent.Transform.EulerAngles = new Vector3(0f, 45f, 0f);

		child.AddComponent(new MeshRendererData
		{
			Mesh = "builtin:sphere",
			Material = "builtin:default"
		});

		EntityClipboard clipboard = new();

		Assert.Equal(2, clipboard.Capture(scene, [parent.Id]));

		IReadOnlyList<SceneEntity> created = clipboard.CreateEntities();

		Assert.Equal(2, created.Count);

		SceneEntity pastedParent = created[0];
		SceneEntity pastedChild = created[1];

		Assert.NotEqual(parent.Id, pastedParent.Id);
		Assert.NotEqual(child.Id, pastedChild.Id);
		Assert.NotEqual(pastedParent.Id, pastedChild.Id);

		Assert.Null(pastedParent.ParentId);
		Assert.Equal(pastedParent.Id, pastedChild.ParentId);

		Assert.Equal(new Vector3(4f, 5f, 6f), pastedParent.Transform.Position);
		Assert.Equal(new Vector3(0f, 45f, 0f), pastedParent.Transform.EulerAngles);
		Assert.Equal(
			"builtin:sphere",
			pastedChild.GetComponent<MeshRendererData>()?.Mesh);

		// Captured data is a snapshot: editing the source afterwards
		// must not change what a paste produces.
		parent.Transform.Position = Vector3.Zero;

		Assert.Equal(
			new Vector3(4f, 5f, 6f),
			clipboard.CreateEntities()[0].Transform.Position);
	}

	[Fact]
	public void CreateEntities_CanBeInsertedIntoADifferentScene()
	{
		SceneDocument source = new("Source");
		SceneDocument target = new("Target");

		SceneEntity root = source.CreateEntity("Root");
		SceneEntity child = source.CreateEntity("Child", root.Id);

		child.Transform.Scale = new Vector3(2f, 2f, 2f);

		EntityClipboard clipboard = new();
		clipboard.Capture(source, [root.Id]);

		foreach (SceneEntity entity in clipboard.CreateEntities())
		{
			target.AddEntity(entity);
		}

		Assert.Equal(2, target.EntityCount);

		SceneEntity pastedRoot = target.Entities.Single(
			entity => entity.ParentId is null);

		SceneEntity pastedChild = target.Entities.Single(
			entity => entity.ParentId == pastedRoot.Id);

		Assert.Equal("Child", pastedChild.Name);
		Assert.Equal(new Vector3(2f, 2f, 2f), pastedChild.Transform.Scale);

		// The source scene is untouched.
		Assert.Equal(2, source.EntityCount);
		Assert.NotNull(source.Find(root.Id));
		Assert.NotNull(source.Find(child.Id));
	}

	[Fact]
	public void Capture_WithNoObjects_EmptiesTheClipboard()
	{
		SceneDocument scene = new("Source");
		SceneEntity cube = scene.CreateEntity("Cube");

		EntityClipboard clipboard = new();

		Assert.Equal(1, clipboard.Capture(scene, [cube.Id]));
		Assert.Equal(0, clipboard.Capture(scene, []));
		Assert.True(clipboard.IsEmpty);
		Assert.Empty(clipboard.CreateEntities());
	}
}
