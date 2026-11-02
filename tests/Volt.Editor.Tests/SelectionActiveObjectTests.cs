using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers which object stays active while a multi-selection is toggled
/// one row at a time, since the inspector and the rename and property
/// commands all follow the active object.
/// </summary>
public sealed class SelectionActiveObjectTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-selection-active-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();

	public SelectionActiveObjectTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Selection");

		_session.SelectAll();
		_session.DeleteSelection();
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
	public void TogglingOffAnotherRowKeepsTheActiveObject()
	{
		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;
		SceneEntity third = _session.CreateEntity("Third")!;

		_session.SelectMany([first.Id, second.Id, third.Id], first.Id);

		Assert.Equal(first.Id, _session.ActiveEntityId);

		_session.Select(second.Id, additive: true);

		// The row that was toggled off is not the one being edited, so
		// the inspector must stay on the object the author is working
		// with instead of jumping to an arbitrary remaining row.
		Assert.Equal(first.Id, _session.ActiveEntityId);
		Assert.Equal(2, _session.Selection.Count);
		Assert.DoesNotContain(second.Id, _session.Selection);
	}

	[Fact]
	public void TogglingOffTheActiveRowHandsTheRoleToTheLastRemainingObject()
	{
		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;
		SceneEntity third = _session.CreateEntity("Third")!;

		_session.SelectMany([first.Id, second.Id, third.Id], second.Id);
		_session.Select(second.Id, additive: true);

		// Removing the active row has to pick a replacement
		// deterministically, so the same rows always produce the same
		// inspector. Scene order is what the hierarchy shows.
		Assert.Equal(third.Id, _session.ActiveEntityId);
	}

	[Fact]
	public void TogglingOffTheLastSelectedRowLeavesNoActiveObject()
	{
		SceneEntity only = _session.CreateEntity("Only")!;

		_session.Select(only.Id);
		_session.Select(only.Id, additive: true);

		Assert.Empty(_session.Selection);
		Assert.Null(_session.ActiveEntityId);
	}
}
