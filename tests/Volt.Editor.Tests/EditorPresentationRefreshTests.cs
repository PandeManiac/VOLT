using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

public sealed class EditorPresentationRefreshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(), $"volt-presentation-refresh-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EditorPresentationRefreshTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Presentation");
	}

	[Fact]
	public void HierarchyRefreshesMeshIconWhenMeshPropertyChanges()
	{
		SceneEntity entity = MeshEntity();
		MeshRendererData renderer = entity.GetComponent<MeshRendererData>()!;
		renderer.Mesh = "builtin:cube";

		using HierarchyPanel hierarchy = new(_session);
		_context.Add(hierarchy);
		_context.BeginFrame(0, new UiSize(500, 700), UiPoint.Zero);
		TreeView tree = Descendants(hierarchy).OfType<TreeView>().Single();
		TreeViewItem item = FindItem(tree.Roots, entity.Id);
		Assert.Equal(UiIcon.Cube, item.Icon);

		SetMesh(entity, "builtin:sphere");

		TreeViewItem updated = FindItem(tree.Roots, entity.Id);
		Assert.Equal(UiIcon.Sphere, updated.Icon);
		Assert.NotSame(item, updated);
	}

	[Fact]
	public void InspectorRefreshesEntityIconWhenMeshPropertyChanges()
	{
		SceneEntity entity = MeshEntity();
		MeshRendererData renderer = entity.GetComponent<MeshRendererData>()!;
		renderer.Mesh = "builtin:cube";
		_session.Select(entity.Id);

		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(500, 700), UiPoint.Zero);
		IconView entityIcon = Descendants(inspector)
			.OfType<IconView>()
			.First();
		Assert.Equal(UiIcon.Cube, entityIcon.Icon);

		SetMesh(entity, "builtin:sphere");

		Assert.Equal(UiIcon.Sphere, entityIcon.Icon);
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	private SceneEntity MeshEntity()
	{
		SceneEntity entity = _session.Scene!.Entities.First(candidate =>
			candidate.GetComponent<MeshRendererData>() is not null);
		_session.Select(entity.Id);
		return entity;
	}

	private void SetMesh(SceneEntity entity, string mesh)
	{
		EditorComponentDescriptor component = ComponentCatalog.Default.Get(
			MeshRendererData.ComponentTypeId);
		EditorPropertyDescriptor property = component.Properties.Single(
			candidate => candidate.Name == nameof(MeshRendererData.Mesh));
		_session.SetComponentProperty(
			entity.Id,
			entity.GetComponent<MeshRendererData>()!,
			property,
			mesh);
	}

	private static TreeViewItem FindItem(
		IEnumerable<TreeViewItem> roots,
		Guid id)
	{
		foreach (TreeViewItem item in roots)
		{
			if (item.Tag is Guid itemId && itemId == id)
			{
				return item;
			}

			try
			{
				return FindItem(item.Children, id);
			}
			catch (InvalidOperationException)
			{
			}
		}

		throw new InvalidOperationException($"No hierarchy row exists for '{id}'.");
	}

	private static IEnumerable<Widget> Descendants(Widget root)
	{
		foreach (Widget child in root.Children)
		{
			yield return child;
			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}
}
