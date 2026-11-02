using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;
using Volt.Ui.Widgets.Properties;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the property grid keeping the author's scroll position when
/// a component change rebuilds every row.
/// </summary>
public sealed class InspectorPropertyScrollTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-inspector-scroll-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public InspectorPropertyScrollTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "InspectorScroll");
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
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void RemovingAComponentKeepsThePropertyGridScrolled()
	{
		SceneEntity entity = _session.Scene!.Entities.First(candidate =>
			candidate.GetComponent<MeshRendererData>() is not null);

		_session.Select(entity.Id);

		foreach (EditorComponentDescriptor descriptor in
			ComponentCatalog.Default.All)
		{
			if (!entity.HasComponent(descriptor.TypeId))
			{
				_session.AddComponent(entity.Id, descriptor.TypeId);
			}
		}

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		_context.BeginFrame(0.0, new UiSize(800f, 260f), UiPoint.Zero);
		_context.Draw();

		PropertyGrid grid = Descendants(inspector)
			.OfType<PropertyGrid>()
			.Single();

		Assert.True(
			grid.MaximumScroll > 0f,
			"The inspector must overflow for this test to mean anything.");

		_context.Wheel(new UiPoint(0f, -3f), grid.GlobalContentBounds.Center);
		_context.BeginFrame(0.1, new UiSize(800f, 260f), UiPoint.Zero);
		_context.Draw();

		float scrolled = grid.ScrollOffset.Y;

		Assert.True(scrolled > 0f, "The property grid must scroll.");

		// Removing a component rebuilds every row in the grid.
		AuthoredComponent removed = entity.Components.First(
			component => component is not MeshRendererData);

		_session.RemoveComponent(entity.Id, removed);
		_context.BeginFrame(0.2, new UiSize(800f, 260f), UiPoint.Zero);
		_context.Draw();

		Assert.True(
			grid.ScrollOffset.Y > 0f,
			"A rebuilt property grid must not jump back to the top.");
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
