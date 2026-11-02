using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the hierarchy keeping its view state when the open scene file
/// is renamed.
/// </summary>
/// <remarks>
/// Renaming an asset changes the scene's path, not its document. Treating
/// that path change as a scene switch clears the filter and expansion the
/// author was working with and makes a routine asset action feel like the
/// editor left the scene.
/// </remarks>
public sealed class HierarchySceneRenameTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-rename-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchySceneRenameTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Rename");
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
	public void RenamingTheOpenSceneKeepsTheFilter()
	{
		using HierarchyPanel panel = new(_session);

		_context.Add(panel);
		Paint(0.0);

		TextField search = Descendants(panel)
			.OfType<TextField>()
			.Single();

		Assert.True(search.Focus());
		Assert.True(_context.TextInput("Cube"));
		Paint(0.1);

		Assert.Equal("Cube", search.Text);

		string renamed = _session.RenameAsset(
			_session.ScenePath!,
			"Renamed");

		Paint(0.2);

		Assert.Equal("Scenes/Renamed.volt.scene.json", renamed);
		Assert.Equal("Cube", search.Text);
	}

	[Fact]
	public void RenamingTheOpenSceneKeepsACollapsedRowCollapsed()
	{
		SceneEntity parent = _session.CreateEntity("Parent")!;

		_session.CreateEntity("Child", parent.Id);

		using HierarchyPanel panel = new(_session);

		_context.Add(panel);
		Paint(0.0);

		TreeView tree = Descendants(panel)
			.OfType<TreeView>()
			.Single();
		TreeViewItem row = tree.Roots.Single(item =>
			item.Tag is Guid id && id == parent.Id);

		Assert.True(row.IsExpanded);

		row.IsExpanded = false;
		tree.Refresh();
		Paint(0.1);

		_session.RenameAsset(_session.ScenePath!, "Renamed");
		Paint(0.2);

		TreeViewItem rebuilt = tree.Roots.Single(item =>
			item.Tag is Guid id && id == parent.Id);

		Assert.False(rebuilt.IsExpanded);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(400f, 500f), UiPoint.Zero);
		_context.Draw();
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
