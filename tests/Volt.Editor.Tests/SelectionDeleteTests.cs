using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers deleting a multi-selection: every selected object goes in
/// one undo step, a nested object is not deleted twice, and undo
/// brings the subtree back intact.
/// </summary>
public sealed class SelectionDeleteTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-selection-delete-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SelectionDeleteTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Delete");
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
	public void DeleteSelection_RemovesEverySelectedObjectInOneUndoStep()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;
		SceneEntity bystander = _session.CreateEntity("Bystander")!;

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		int history = _session.Undo.UndoCount;

		int deleted = _session.DeleteSelection();

		Assert.Equal(2, deleted);
		Assert.Null(scene.Find(cube.Id));
		Assert.Null(scene.Find(partner.Id));
		Assert.NotNull(scene.Find(bystander.Id));
		Assert.Empty(_session.Selection);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.NotNull(scene.Find(cube.Id));
		Assert.NotNull(scene.Find(partner.Id));
	}

	[Fact]
	public void DeleteSelection_RemovesANestedSelectionOnce()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		_session.SelectMany([parent.Id, child.Id], parent.Id);

		int history = _session.Undo.UndoCount;
		int deleted = _session.DeleteSelection();

		Assert.Equal(1, deleted);
		Assert.Null(scene.Find(parent.Id));
		Assert.Null(scene.Find(child.Id));
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.NotNull(scene.Find(parent.Id));
		Assert.NotNull(scene.Find(child.Id));
		Assert.Equal(parent.Id, scene.Find(child.Id)!.ParentId);
	}

	[Fact]
	public void DeleteSelection_WithoutASelectionChangesNothing()
	{
		SceneDocument scene = _session.Scene!;
		int entities = scene.Entities.Count;
		int history = _session.Undo.UndoCount;

		_session.ClearSelection();

		Assert.Equal(0, _session.DeleteSelection());
		Assert.Equal(entities, scene.Entities.Count);
		Assert.Equal(history, _session.Undo.UndoCount);
	}

	[Fact]
	public void DeleteKey_RemovesTheWholeSelection()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		int history = _session.Undo.UndoCount;

		_context.KeyDown(UiKey.Delete);

		Assert.Null(scene.Find(cube.Id));
		Assert.Null(scene.Find(partner.Id));
		Assert.Equal(history + 1, _session.Undo.UndoCount);
	}

	[Fact]
	public void HierarchyDeleteButton_RemovesTheWholeSelection()
	{
		HierarchyPanel hierarchy = new(_session);

		_context.Add(hierarchy);
		_context.BeginFrame(
			0.0,
			new UiSize(360f, 700f),
			UiPoint.Zero);

		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		_context.BeginFrame(
			0.1,
			new UiSize(360f, 700f),
			UiPoint.Zero);

		IconButton delete = Descendants(hierarchy)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Delete the selection");

		UiPoint center = delete.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);

		Assert.Null(scene.Find(cube.Id));
		Assert.Null(scene.Find(partner.Id));
	}

	private static SceneEntity FindMesh(SceneDocument scene)
	{
		return scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);
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
