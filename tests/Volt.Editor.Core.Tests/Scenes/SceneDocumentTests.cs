using System.Numerics;
using Volt.Editor.Core.Editing;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Scenes;

public sealed class SceneDocumentTests
{
	[Fact]
	public void Reparent_RejectsCyclesAndForeignParents()
	{
		SceneDocument scene = new("Hierarchy");
		SceneEntity root = scene.CreateEntity("Root");
		SceneEntity child = scene.CreateEntity("Child", root.Id);
		SceneEntity grandchild = scene.CreateEntity("Grandchild", child.Id);

		Assert.False(scene.CanReparent(root.Id, grandchild.Id));
		Assert.Throws<InvalidOperationException>(
			() => scene.ReparentEntity(root.Id, grandchild.Id));

		Assert.Throws<ArgumentException>(
			() => scene.ReparentEntity(root.Id, Guid.NewGuid()));

		Assert.True(scene.ReparentEntity(grandchild.Id, root.Id));
		Assert.Equal(root.Id, grandchild.ParentId);
		Assert.Equal([child, grandchild], scene.GetChildren(root.Id));
	}

	[Fact]
	public void RemoveEntity_CascadesAndBatchRestoresOriginalOrder()
	{
		SceneDocument scene = new("Hierarchy");
		SceneEntity a = scene.CreateEntity("A");
		SceneEntity b = scene.CreateEntity("B", a.Id);
		SceneEntity c = scene.CreateEntity("C");

		RemovedEntityBatch removed = scene.RemoveEntity(a.Id);

		Assert.Equal(2, removed.Entities.Count);
		Assert.Same(c, Assert.Single(scene.Entities));
		Assert.Null(scene.Find(a.Id));

		scene.RestoreRemoved(removed);

		Assert.Equal([a, b, c], scene.Entities);
		Assert.Equal(a.Id, b.ParentId);
	}

	[Fact]
	public void DuplicateEntity_CopiesSubtreeWithNewIdentities()
	{
		SceneDocument scene = new("Duplicate");
		SceneEntity parent = scene.CreateEntity("Parent");
		SceneEntity child = scene.CreateEntity("Child", parent.Id);
		child.Transform.Position = new Vector3(1f, 2f, 3f);
		child.AddComponent(new MeshRendererData { Mesh = "mesh" });

		SceneEntity copy = scene.DuplicateEntity(parent.Id);
		SceneEntity? copiedChild = scene
			.GetChildren(copy.Id)
			.SingleOrDefault();

		Assert.NotEqual(parent.Id, copy.Id);
		Assert.NotNull(copiedChild);
		Assert.NotEqual(child.Id, copiedChild.Id);
		Assert.Equal(new Vector3(1f, 2f, 3f), copiedChild.Transform.Position);
		Assert.Equal("mesh", copiedChild.GetComponent<MeshRendererData>()?.Mesh);
		Assert.Equal("Parent (Copy)", copy.Name);
	}

	[Fact]
	public void HierarchyOrder_ParentsPrecedeChildren()
	{
		SceneDocument scene = new("Order");
		SceneEntity a = scene.CreateEntity("A");
		scene.CreateEntity("B", a.Id);
		SceneEntity c = scene.CreateEntity("C");

		IReadOnlyList<SceneEntity> order = scene.BuildHierarchyOrder();

		Assert.Equal(["A", "B", "C"], order.Select(entity => entity.Name));
		Assert.Same(c, order[2]);
	}

	[Fact]
	public void UndoStack_ReversesAddDeleteTransformAndProperty()
	{
		SceneDocument scene = new("Undo");
		UndoStack undo = new();
		SceneEntity entity = scene.CreateEntity("Object");
		entity.AddComponent(new SpinBehaviourData());

		SpinBehaviourData spin =
			entity.GetComponent<SpinBehaviourData>()!;

		EditorPropertyDescriptor speed = ComponentCatalog.Default
			.Get(SpinBehaviourData.ComponentTypeId)
			.Properties
			.Single(property => property.Name == nameof(SpinBehaviourData.DegreesPerSecond));

		TransformData updated = entity.Transform.Clone();
		updated.Position = new Vector3(4f, 5f, 6f);

		undo.Execute(new SetTransformCommand(scene, entity.Id, updated));
		undo.Execute(new ComponentPropertyCommand(
			scene,
			entity.Id,
			spin,
			speed,
			90f));

		Assert.Equal(new Vector3(4f, 5f, 6f), entity.Transform.Position);
		Assert.Equal(90f, spin.DegreesPerSecond);

		Assert.True(undo.Undo());
		Assert.Equal(45f, spin.DegreesPerSecond);
		Assert.Equal(new Vector3(4f, 5f, 6f), entity.Transform.Position);

		Assert.True(undo.Undo());
		Assert.Equal(Vector3.Zero, entity.Transform.Position);
		Assert.True(undo.Redo());
		Assert.Equal(new Vector3(4f, 5f, 6f), entity.Transform.Position);

		undo.Execute(new DeleteEntityCommand(scene, entity.Id));
		Assert.Null(scene.Find(entity.Id));

		Assert.True(undo.Undo());
		Assert.NotNull(scene.Find(entity.Id));
	}
}
