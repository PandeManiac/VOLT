using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers duplicating a multi-selection: one copy of every selected
/// root, one undo step, stable ids across redo, and the copies left
/// selected.
/// </summary>
public sealed class SelectionDuplicateTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-selection-duplicate-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SelectionDuplicateTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Duplicate");
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
	public void DuplicateSelection_CopiesEverySelectedObjectInOneUndoStep()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		int entities = scene.Entities.Count;
		int history = _session.Undo.UndoCount;

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		IReadOnlyList<SceneEntity> copies = _session.DuplicateSelection();

		Assert.Equal(2, copies.Count);
		Assert.Equal(entities + 2, scene.Entities.Count);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		Assert.Equal(2, _session.Selection.Count);
		Assert.All(
			copies,
			copy => Assert.Contains(copy.Id, _session.Selection));

		Assert.DoesNotContain(cube.Id, _session.Selection);
		Assert.NotNull(_session.ActiveEntityId);
		Assert.Contains(
			_session.ActiveEntityId!.Value,
			copies.Select(copy => copy.Id));

		_session.UndoLast();

		Assert.Equal(entities, scene.Entities.Count);

		_session.RedoLast();

		Assert.Equal(entities + 2, scene.Entities.Count);
		Assert.NotNull(scene.Find(copies[0].Id));
	}

	[Fact]
	public void DuplicateSelection_CopiesANestedSelectionOnce()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		int entities = scene.Entities.Count;

		_session.SelectMany([parent.Id, child.Id], parent.Id);

		IReadOnlyList<SceneEntity> copies = _session.DuplicateSelection();

		Assert.Single(copies);
		Assert.Equal(entities + 2, scene.Entities.Count);

		SceneEntity childCopy = Assert.Single(
			scene.Entities,
			entity => entity.ParentId == copies[0].Id);

		Assert.NotEqual(child.Id, childCopy.Id);
		Assert.Equal(copies[0].Id, _session.ActiveEntityId);
	}

	[Fact]
	public void DuplicatedChild_KeepsItsOriginalParent()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		_session.Select(child.Id);

		IReadOnlyList<SceneEntity> copies = _session.DuplicateSelection();

		Assert.Single(copies);
		Assert.Equal(parent.Id, copies[0].ParentId);
	}

	[Fact]
	public void DuplicateSelection_WithoutASelectionChangesNothing()
	{
		SceneDocument scene = _session.Scene!;
		int entities = scene.Entities.Count;
		int history = _session.Undo.UndoCount;

		_session.ClearSelection();

		Assert.Empty(_session.DuplicateSelection());
		Assert.Equal(entities, scene.Entities.Count);
		Assert.Equal(history, _session.Undo.UndoCount);
	}

	[Fact]
	public void DuplicateKey_CopiesTheWholeSelection()
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
		int entities = scene.Entities.Count;
		int history = _session.Undo.UndoCount;

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		_context.KeyDown(UiKey.D, UiKeyModifiers.Control);

		Assert.Equal(entities + 2, scene.Entities.Count);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
	}

	private static SceneEntity FindMesh(SceneDocument scene)
	{
		return scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);
	}
}
