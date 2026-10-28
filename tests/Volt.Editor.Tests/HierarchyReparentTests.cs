using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers re-parenting from the hierarchy: dragging a row onto
/// another, the drop highlight, the cycle refusal, and the single undo
/// step such a move produces.
/// </summary>
public sealed class HierarchyReparentTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-reparent-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyReparentTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Reparent");
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
	public void ReparentEntities_MovesSeveralObjectsInOneUndoStep()
	{
		SceneDocument scene = EmptyScene();
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;

		int history = _session.Undo.UndoCount;

		int moved = _session.ReparentEntities(
			[first.Id, second.Id],
			parent.Id);

		Assert.Equal(2, moved);
		Assert.Equal(parent.Id, scene.Find(first.Id)!.ParentId);
		Assert.Equal(parent.Id, scene.Find(second.Id)!.ParentId);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Null(scene.Find(first.Id)!.ParentId);
		Assert.Null(scene.Find(second.Id)!.ParentId);
	}

	[Fact]
	public void ReparentEntities_RefusesToCreateACycle()
	{
		SceneDocument scene = EmptyScene();
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		int history = _session.Undo.UndoCount;

		Assert.Equal(0, _session.ReparentEntities([parent.Id], child.Id));
		Assert.Null(scene.Find(parent.Id)!.ParentId);
		Assert.Equal(parent.Id, scene.Find(child.Id)!.ParentId);
		Assert.Equal(history, _session.Undo.UndoCount);
	}

	[Fact]
	public void DraggingARowOntoAnotherReparentsIt()
	{
		SceneDocument scene = EmptyScene();
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child")!;

		TreeView tree = BuildHierarchy();

		UiPoint from = RowCenter(tree.GlobalContentBounds, 1);
		UiPoint to = RowCenter(tree.GlobalContentBounds, 0);

		int history = _session.Undo.UndoCount;

		_context.PointerDown(UiMouseButton.Left, from);
		_context.PointerMove(to, Delta(from, to));
		_context.PointerUp(UiMouseButton.Left, to);

		Assert.Equal(parent.Id, scene.Find(child.Id)!.ParentId);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Null(scene.Find(child.Id)!.ParentId);
	}

	[Fact]
	public void DraggingHighlightsTheRowItWillDropOn()
	{
		EmptyScene();
		_session.CreateEntity("Parent");
		_session.CreateEntity("Child");

		TreeView tree = BuildHierarchy();

		UiRect targetRow = Row(tree.GlobalContentBounds, 0);
		UiPoint from = RowCenter(tree.GlobalContentBounds, 1);
		UiPoint to = targetRow.Center;

		_context.PointerDown(UiMouseButton.Left, from);
		_context.PointerMove(to, Delta(from, to));

		UiDrawList drawList = _context.Draw();

		bool highlighted = false;

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind != UiDrawCommandKind.Border)
			{
				continue;
			}

			highlighted |=
				command.Rect.Height >= targetRow.Height - 1f &&
				command.Rect.Y >= targetRow.Y - 1f &&
				command.Rect.Bottom <= targetRow.Bottom + 1f;
		}

		Assert.True(
			highlighted,
			"The row the drop will land on must be outlined while dragging.");

		_context.PointerUp(UiMouseButton.Left, to);
	}

	[Fact]
	public void DroppingBelowTheRowsMovesTheObjectToTheRoot()
	{
		SceneDocument scene = EmptyScene();
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		TreeView tree = BuildHierarchy();

		// The child is the only expanded row, so the empty space below
		// the rows is below the second row.
		UiPoint from = RowCenter(tree.GlobalContentBounds, 1);

		float rowHeight = RowHeight();
		UiPoint to = new(
			tree.GlobalContentBounds.Center.X,
			tree.GlobalContentBounds.Y + (3f * rowHeight));

		_context.PointerDown(UiMouseButton.Left, from);
		_context.PointerMove(to, Delta(from, to));
		_context.PointerUp(UiMouseButton.Left, to);

		Assert.Null(scene.Find(child.Id)!.ParentId);
	}

	private SceneDocument EmptyScene()
	{
		SceneDocument scene = _session.Scene!;

		_session.SelectAll();
		_session.DeleteSelection();

		return scene;
	}

	private TreeView BuildHierarchy()
	{
		HierarchyPanel hierarchy = new(_session);

		_context.Add(hierarchy);
		_context.BeginFrame(
			0.0,
			new UiSize(400f, 800f),
			UiPoint.Zero);

		return Descendants(hierarchy).OfType<TreeView>().Single();
	}

	private static float RowHeight()
	{
		return UiTheme.CreateDark().Metrics.RowHeight;
	}

	private static UiRect Row(UiRect content, int index)
	{
		float rowHeight = RowHeight();

		return new UiRect(
			content.X,
			content.Y + (index * rowHeight),
			content.Width,
			rowHeight);
	}

	private static UiPoint RowCenter(UiRect content, int index)
	{
		return Row(content, index).Center;
	}

	private static UiPoint Delta(UiPoint from, UiPoint to)
	{
		return new UiPoint(to.X - from.X, to.Y - from.Y);
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
