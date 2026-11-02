using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the hierarchy keeping the scroll position the author chose
/// while the session reports changes that do not restructure the tree.
/// </summary>
/// <remarks>
/// Selection is mirrored into the tree on every session notification,
/// and revealing a row scrolls it into view. A scene edit must not
/// count as a request to bring the selection back, or scrolling away
/// from the selection to find another object would be undone by the
/// next property edit or gizmo drag.
/// </remarks>
public sealed class HierarchyScrollStabilityTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-scroll-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyScrollStabilityTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Scroll");

		_session.SelectAll();
		_session.DeleteSelection();
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
	public void EditingAPositionKeepsTheHierarchyScrollPosition()
	{
		TreeView tree = BuildHierarchy(rowCount: 60);
		SceneEntity first = _session.Scene!.GetChildren(null)[0];

		Assert.Equal(0f, tree.ScrollOffset);

		ScrollDown(tree);

		float scrolled = tree.ScrollOffset;
		Assert.True(scrolled > 0f, "The hierarchy must scroll away from the top.");

		TransformData moved = first.Transform.Clone();
		moved.Position = new System.Numerics.Vector3(1f, 2f, 3f);
		_session.SetTransform(first.Id, moved);

		Assert.Equal(scrolled, tree.ScrollOffset, 3);

		// The tree still mirrors the session, so the row the author
		// scrolled away from is not the row that stays selected while the
		// tree forgets the selection.
		Assert.Equal(
			_session.ActiveEntityId!.Value,
			Assert.Single(tree.SelectedItems).Tag);
	}

	[Fact]
	public void SelectingAnotherObjectScrollsItsRowIntoView()
	{
		TreeView tree = BuildHierarchy(rowCount: 60);
		SceneEntity target = _session.Scene!.GetChildren(null)[58];

		Assert.Equal(0f, tree.ScrollOffset);

		_session.Select(target.Id);

		Assert.True(
			tree.ScrollOffset > 0f,
			"Selecting an off-screen object must reveal its row.");
	}

	private TreeView BuildHierarchy(int rowCount)
	{
		for (int index = 0; index < rowCount; index++)
		{
			_session.CreateEntity($"Object {index}");
		}

		HierarchyPanel hierarchy = new(_session);

		_context.Add(hierarchy);
		_context.BeginFrame(
			0.0,
			new UiSize(400f, 320f),
			UiPoint.Zero);

		return Descendants(hierarchy).OfType<TreeView>().Single();
	}

	private void ScrollDown(TreeView tree)
	{
		_context.Wheel(
			new UiPoint(0f, -3f),
			tree.GlobalContentBounds.Center);
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
