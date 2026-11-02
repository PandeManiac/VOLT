using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers a typed name edit outliving the scene it was typed in.
/// </summary>
/// <remarks>
/// The inspector's name field commits itself when the panel moves on. A
/// reload or a scene switch replaces the document, and entity ids can
/// repeat between scene versions, so committing on the id alone would
/// rename an unrelated object in the new scene — and clear the name the
/// author just loaded from disk.
/// </remarks>
public sealed class SceneReloadPendingEditTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-reload-pending-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SceneReloadPendingEditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ReloadPending");
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
	public void ReloadingKeepsTheTypedNameOutOfTheNewScene()
	{
		EditorShell shell = BuildShell();
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);
		Layout();

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();
		TextField name = Descendants(inspector)
			.OfType<TextField>()
			.First();

		Assert.True(name.Focus());
		name.SelectAll();
		_context.TextInput("Typed");
		Layout();

		_session.SaveScene();

		string path = Path.Combine(
			_session.Project!.RootPath,
			_session.ScenePath!.Replace('/', Path.DirectorySeparatorChar));

		SceneDocument disk = SceneSerializer.Load(path);

		disk.RenameEntity(cube.Id, "FromDisk");
		SceneSerializer.Save(path, disk);

		// The typed name is still pending, so opening the scene has to
		// ask about it before the document is replaced. Discarding drops
		// the edit with the old scene rather than renaming an object in
		// the freshly loaded one.
		Assert.False(_session.OpenScene(_session.ScenePath!));
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Discard"));

		Assert.Equal("FromDisk", _session.Scene!.Find(cube.Id)!.Name);
		Assert.Null(_session.Undo.UndoDescription);

		// The reload replaces the scene, so nothing is selected in it and
		// the typed name has nowhere left to go.
		Assert.Empty(_session.Selection);
	}

	private EditorShell BuildShell()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		Layout();

		return shell;
	}

	private void Layout()
	{
		_context.BeginFrame(0.0, new UiSize(1440f, 900f), UiPoint.Zero);
		_context.Draw();
	}

	private void Click(Button button)
	{
		UiPoint point = button.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, point);
		_context.PointerUp(UiMouseButton.Left, point);
	}

	private static Button FindButton(Widget root, string text)
	{
		return Descendants(root)
			.OfType<Button>()
			.Single(button => button.Text == text);
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
