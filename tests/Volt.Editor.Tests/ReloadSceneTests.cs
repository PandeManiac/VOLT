using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers reloading the open scene from disk.
/// </summary>
/// <remarks>
/// A save is refused when the file changed elsewhere, so the editor needs
/// a way to load that version without closing the project. Local edits
/// are still protected by the unsaved-changes guard.
/// </remarks>
public sealed class ReloadSceneTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-reload-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ReloadSceneTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Reload");
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
	public void TheFileMenuLoadsTheVersionOnDisk()
	{
		EditorShell shell = BuildShell();
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.SaveScene();

		string path = ScenePath();

		WriteFromDisk(path, cube.Id, "FromDisk");

		Invoke(shell, "Reload Scene from Disk");

		Assert.False(_context.HasModal);
		Assert.Equal("FromDisk", _session.Scene!.Find(cube.Id)!.Name);

		// The reload moved the file stamp forward, so editing and saving
		// work again instead of hitting the external-change refusal.
		_session.CreateEntity("Sphere");
		_session.SaveScene();

		Assert.Contains(
			"Sphere",
			File.ReadAllText(path),
			StringComparison.Ordinal);
	}

	[Fact]
	public void ReloadingADirtySceneAsksBeforeDroppingTheEdits()
	{
		EditorShell shell = BuildShell();
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.SaveScene();

		string path = ScenePath();

		WriteFromDisk(path, cube.Id, "FromDisk");
		_session.CreateEntity("Unsaved");

		Invoke(shell, "Reload Scene from Disk");

		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Discard"));

		Assert.Equal("FromDisk", _session.Scene!.Find(cube.Id)!.Name);
		Assert.DoesNotContain(
			_session.Scene.Entities,
			entity => entity.Name == "Unsaved");
	}

	[Fact]
	public void ReloadingWithoutAScenePathDoesNothing()
	{
		EditorShell shell = BuildShell();

		_session.CloseProject();

		Invoke(shell, "Reload Scene from Disk");

		Assert.Null(_session.Scene);
		Assert.False(_context.HasModal);
	}

	private void WriteFromDisk(string path, Guid entityId, string name)
	{
		SceneDocument disk = SceneSerializer.Load(path);

		disk.RenameEntity(entityId, name);
		SceneSerializer.Save(path, disk);

		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
	}

	private static void Invoke(EditorShell shell, string label)
	{
		MenuItem item = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "File")
			.Children
			.Single(child => child.Label == label);

		item.Action!.Invoke();
	}

	private EditorShell BuildShell()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		Layout();

		return shell;
	}

	private string ScenePath()
	{
		return Path.Combine(
			_session.Project!.RootPath,
			_session.ScenePath!.Replace('/', Path.DirectorySeparatorChar));
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
