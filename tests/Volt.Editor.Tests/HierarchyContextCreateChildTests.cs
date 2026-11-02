using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers creating a child object from a hierarchy row's context menu.
/// </summary>
/// <remarks>
/// The new object is selected, and the panel opens the path to a new
/// selection. On a collapsed row that has to happen, or the object the
/// author just asked for is created out of sight.
/// </remarks>
public sealed class HierarchyContextCreateChildTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-child-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyContextCreateChildTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "CreateChild");
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
	public void CreatingAChildOpensTheRowItWentInto()
	{
		SceneDocument scene = _session.Scene!;

		_session.SelectAll();
		_session.DeleteSelection();

		SceneEntity parent = _session.CreateEntity("Parent")!;
		_session.CreateEntity("Existing", parent.Id);

		using HierarchyPanel panel = new(_session);

		_context.Add(panel);
		Paint(0.0);

		TreeView tree = Descendants(panel)
			.OfType<TreeView>()
			.First();

		TreeViewItem parentRow = tree.Roots
			.Single(item => item.Tag is Guid id && id == parent.Id);

		parentRow.IsExpanded = false;
		tree.Refresh();
		Paint(0.1);

		Assert.False(parentRow.IsExpanded);

		OpenContextMenu(tree.GlobalContentBounds, rowIndex: 0);

		MenuPopup popup = Assert.IsType<MenuPopup>(_context.ActivePopup);
		UiRect bounds = popup.GlobalVisualBounds;

		// The child command is the last entry in the row's menu, so it
		// sits just above the popup's bottom padding.
		Click(new UiPoint(bounds.Center.X, bounds.Bottom - 16f));
		Paint(0.2);

		Assert.Null(_context.ActivePopup);

		SceneEntity created = scene.Entities.Single(entity =>
			entity.Name == "Cube");

		Assert.Equal(parent.Id, created.ParentId);

		TreeViewItem rebuilt = tree.Roots
			.Single(item => item.Tag is Guid id && id == parent.Id);

		Assert.True(
			rebuilt.IsExpanded,
			"The row the child went into must open so the new object is visible.");

		Assert.Contains(
			rebuilt.Children,
			item => item.Tag is Guid id && id == created.Id);
	}

	private void Click(UiPoint point)
	{
		_context.PointerDown(UiMouseButton.Left, point);
		_context.PointerUp(UiMouseButton.Left, point);
	}

	private void OpenContextMenu(UiRect treeBounds, int rowIndex)
	{
		UiPoint row = new(
			treeBounds.X + 50f,
			treeBounds.Y + (rowIndex * 22f) + 5f);

		_context.PointerDown(UiMouseButton.Right, row);
		Paint(0.15);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(400f, 800f), UiPoint.Zero);
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
