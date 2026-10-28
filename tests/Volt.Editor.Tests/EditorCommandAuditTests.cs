using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the editor's editing commands as the user meets them: the
/// menu must only offer a command that will work, and the entity
/// clipboard must copy and paste real subtrees.
/// </summary>
public sealed class EditorCommandAuditTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-command-audit-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EditorCommandAuditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Audit");
	}

	public void Dispose()
	{
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
	public void MenuCommands_WithNoProject_AreDisabledRatherThanInert()
	{
		using EditorSession session = new();
		EditorShell shell = new(session);

		Assert.False(Find(shell, "File", "Save Scene").IsEnabled);
		Assert.False(Find(shell, "File", "Close Project").IsEnabled);
		Assert.False(Find(shell, "File", "New Scene").IsEnabled);
		Assert.False(Find(shell, "Assets", "Refresh Asset Catalog").IsEnabled);
		Assert.False(Find(shell, "Assets", "New Scene").IsEnabled);
		Assert.False(Find(shell, "GameObject", "Create Cube").IsEnabled);
		Assert.False(Find(shell, "GameObject", "Create Empty").IsEnabled);
		Assert.False(Find(shell, "GameObject", "Create Light").IsEnabled);
		Assert.False(Find(shell, "Edit", "Undo").IsEnabled);
		Assert.False(Find(shell, "Edit", "Redo").IsEnabled);
		Assert.False(Find(shell, "Edit", "Cut").IsEnabled);
		Assert.False(Find(shell, "Edit", "Copy").IsEnabled);
		Assert.False(Find(shell, "Edit", "Paste").IsEnabled);
		Assert.False(Find(shell, "Edit", "Delete Selection").IsEnabled);
		Assert.False(Find(shell, "Edit", "Select All").IsEnabled);
	}

	[Fact]
	public void CloseProjectCommand_ReturnsToTheStartScreen()
	{
		EditorShell shell = new(_session);
		MenuItem close = Find(shell, "File", "Close Project");

		Assert.True(close.IsEnabled);

		close.Action!.Invoke();

		Assert.Null(_session.Project);
		Assert.Null(_session.Scene);
		Assert.False(Find(shell, "File", "Close Project").IsEnabled);
	}

	[Fact]
	public void MenuCommands_FollowSelectionHistoryAndClipboard()
	{
		EditorShell shell = new(_session);

		Assert.True(Find(shell, "File", "Save Scene").IsEnabled);
		Assert.True(Find(shell, "GameObject", "Create Cube").IsEnabled);
		Assert.False(Find(shell, "Edit", "Undo").IsEnabled);
		Assert.False(Find(shell, "Edit", "Copy").IsEnabled);

		Find(shell, "GameObject", "Create Cube").Action!.Invoke();

		SceneEntity cube = _session.ActiveEntity!;

		Assert.True(Find(shell, "Edit", "Undo").IsEnabled);
		Assert.True(Find(shell, "Edit", "Copy").IsEnabled);
		Assert.True(Find(shell, "Edit", "Delete Selection").IsEnabled);
		Assert.False(Find(shell, "Edit", "Paste").IsEnabled);

		Find(shell, "Edit", "Copy").Action!.Invoke();

		Assert.True(Find(shell, "Edit", "Paste").IsEnabled);

		int before = _session.Scene!.EntityCount;

		Find(shell, "Edit", "Paste").Action!.Invoke();

		Assert.Equal(before + 1, _session.Scene!.EntityCount);
		Assert.NotEqual(cube.Id, _session.ActiveEntityId);

		Find(shell, "Edit", "Undo").Action!.Invoke();

		Assert.Equal(before, _session.Scene!.EntityCount);
		Assert.True(Find(shell, "Edit", "Redo").IsEnabled);
	}

	[Fact]
	public void ComponentCommands_RequireASelectionAndRejectDuplicates()
	{
		EditorShell shell = new(_session);

		MenuItem component = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "Component");

		MenuItem add = component.Children
			.First(item => item.Label.StartsWith("Add ", StringComparison.Ordinal));

		Assert.False(add.IsEnabled);

		Find(shell, "GameObject", "Create Cube").Action!.Invoke();

		SceneEntity cube = _session.ActiveEntity!;

		// A component the object already has cannot be added again, so
		// its entry must stay unavailable rather than fail on click.
		foreach (AuthoredComponent authored in cube.Components)
		{
			MenuItem present = shell.MenuBarView.Menus
				.Single(menu => menu.Label == "Component")
				.Children
				.Single(item => item.Label == $"Add {authored.DisplayName}");

			Assert.False(present.IsEnabled);
		}
	}

	[Fact]
	public void CopyPaste_CopiesWholeSubtreesWithFreshIdentities()
	{
		SceneDocument scene = _session.Scene!;

		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child")!;

		_session.ReparentEntities([child.Id], parent.Id);

		parent.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.SelectMany([parent.Id, child.Id], parent.Id);

		int before = scene.EntityCount;

		Assert.Equal(2, _session.CopySelection());
		Assert.Equal(2, _session.Clipboard.Count);
		Assert.Equal(1, _session.Clipboard.RootCount);

		IReadOnlyList<SceneEntity> pasted = _session.PasteClipboard();

		Assert.Equal(before + 2, scene.EntityCount);
		Assert.Single(pasted);

		SceneEntity pastedParent = pasted[0];
		SceneEntity pastedChild = scene.Entities.Single(
			entity => entity.ParentId == pastedParent.Id);

		Assert.NotEqual(parent.Id, pastedParent.Id);
		Assert.NotEqual(child.Id, pastedChild.Id);
		Assert.Equal("Parent", pastedParent.Name);
		Assert.Equal("Child", pastedChild.Name);
		Assert.Equal(new Vector3(1f, 2f, 3f), pastedParent.Transform.Position);

		// The paste becomes the selection so a follow-up edit acts on it.
		Assert.Contains(pastedParent.Id, _session.Selection);

		_session.UndoLast();

		Assert.Equal(before, scene.EntityCount);

		_session.RedoLast();

		Assert.Equal(before + 2, scene.EntityCount);
		Assert.NotNull(scene.Find(pastedParent.Id));
		Assert.NotNull(scene.Find(pastedChild.Id));
	}

	[Fact]
	public void Cut_RemovesTheSubtreeAndKeepsItPasteable()
	{
		SceneDocument scene = _session.Scene!;

		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child")!;

		_session.ReparentEntities([child.Id], parent.Id);
		_session.Select(parent.Id);

		int before = scene.EntityCount;

		Assert.Equal(2, _session.CutSelection());
		Assert.Equal(before - 2, scene.EntityCount);
		Assert.False(_session.Clipboard.IsEmpty);

		_session.UndoLast();

		Assert.Equal(before, scene.EntityCount);
		Assert.Equal(2, scene.Entities.Count(
			entity => entity.Name == "Parent" || entity.Name == "Child"));

		IReadOnlyList<SceneEntity> pasted = _session.PasteClipboard();

		Assert.Single(pasted);
		Assert.Equal(before + 2, scene.EntityCount);
		Assert.Single(scene.Entities, entity => entity.ParentId == pasted[0].Id);
	}

	[Fact]
	public void Copy_ReplacesTheClipboardInsteadOfAppending()
	{
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);
		Assert.Equal(1, _session.CopySelection());

		SceneEntity sphere = _session.CreateEntity("Sphere")!;

		_session.Select(sphere.Id);
		Assert.Equal(1, _session.CopySelection());

		Assert.Equal(1, _session.Clipboard.Count);
		Assert.Equal(1, _session.Clipboard.RootCount);
	}

	private static MenuItem Find(EditorShell shell, string menu, string label)
	{
		return shell.MenuBarView.Menus
			.Single(item => item.Label == menu)
			.Children
			.Single(item => item.Label == label);
	}
}
