using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the selection commands: Select All, Select None, and Invert
/// Selection, both through the session and through the shell's
/// shortcuts.
/// </summary>
public sealed class SelectionCommandsTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-selection-commands-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SelectionCommandsTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Selection");
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
	public void SelectAll_TakesEveryObjectAndKeepsTheActiveOne()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity first = scene.Entities.First();

		_session.Select(first.Id);

		int selected = _session.SelectAll();

		Assert.Equal(scene.Entities.Count, selected);
		Assert.Equal(scene.Entities.Count, _session.Selection.Count);
		Assert.Equal(first.Id, _session.ActiveEntityId);
	}

	[Fact]
	public void SelectNone_ClearsTheSelection()
	{
		_session.SelectAll();

		_session.SelectNone();

		Assert.Empty(_session.Selection);
		Assert.Null(_session.ActiveEntityId);
		Assert.Null(_session.ActiveEntity);
	}

	[Fact]
	public void InvertSelection_SwapsSelectedAndUnselected()
	{
		SceneDocument scene = _session.Scene!;
		Guid[] all = [.. scene.Entities.Select(entity => entity.Id)];

		Assert.True(all.Length > 1, "The starter scene has more than one object.");

		_session.Select(all[0]);

		int inverted = _session.InvertSelection();

		Assert.Equal(all.Length - 1, inverted);
		Assert.DoesNotContain(all[0], _session.Selection);
		Assert.Contains(all[^1], _session.Selection);
	}

	[Fact]
	public void InvertSelection_FromNothingSelectsEverything()
	{
		SceneDocument scene = _session.Scene!;

		_session.ClearSelection();

		Assert.Equal(scene.Entities.Count, _session.InvertSelection());
	}

	[Fact]
	public void SelectionShortcuts_WorkThroughTheShell()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		SceneDocument scene = _session.Scene!;

		_context.KeyDown(UiKey.A, UiKeyModifiers.Control);

		Assert.Equal(scene.Entities.Count, _session.Selection.Count);

		_context.KeyDown(
			UiKey.A,
			UiKeyModifiers.Control | UiKeyModifiers.Shift);

		Assert.Empty(_session.Selection);

		_context.KeyDown(UiKey.I, UiKeyModifiers.Control);

		Assert.Equal(scene.Entities.Count, _session.Selection.Count);
	}
}
