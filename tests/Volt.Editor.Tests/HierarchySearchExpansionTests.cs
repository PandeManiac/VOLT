using Volt.Editor.Panels;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

public sealed class HierarchySearchExpansionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-search-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchySearchExpansionTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Search");
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
	public void ClearingSearchRestoresExpansionStateFromBeforeFiltering()
	{
		_session.SelectAll();
		_session.DeleteSelection();
		var parent = _session.CreateEntity("Parent")!;
		_session.CreateEntity("Search Target", parent.Id);

		HierarchyPanel panel = new(_session);
		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(400f, 500f), UiPoint.Zero);

		TreeView tree = Descendants(panel).OfType<TreeView>().Single();
		TextField search = Descendants(panel).OfType<TextField>().Single();
		TreeViewItem parentRow = Assert.Single(tree.Roots);
		Assert.True(parentRow.IsExpanded);

		parentRow.IsExpanded = false;
		tree.Refresh();

		Assert.True(search.Focus());
		Assert.True(_context.TextInput("Search Target"));
		Assert.True(Assert.Single(tree.Roots).IsExpanded);

		search.Text = string.Empty;
		_session.Select(parent.Id);

		Assert.False(Assert.Single(tree.Roots).IsExpanded);
	}

	[Fact]
	public void SwitchingToCopiedSceneDoesNotReuseOtherScenesExpansionState()
	{
		_session.SelectAll();
		_session.DeleteSelection();
		var parent = _session.CreateEntity("Parent")!;
		_session.CreateEntity("Child", parent.Id);
		SceneDocument original = _session.Scene!;
		_session.Project!.SaveScene(
			original,
			"Scenes/Copy.volt.scene.json");

		HierarchyPanel panel = new(_session);
		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(400f, 500f), UiPoint.Zero);
		TreeView tree = Descendants(panel).OfType<TreeView>().Single();
		TreeViewItem parentRow = Assert.Single(tree.Roots);
		Assert.True(parentRow.IsExpanded);

		parentRow.IsExpanded = false;
		tree.Refresh();
		Assert.True(_session.OpenScene("Scenes/Copy.volt.scene.json"));

		parentRow = Assert.Single(tree.Roots);
		Assert.True(parentRow.IsExpanded);
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
