using Volt.Editor.Core.Projects;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what a failed command tells the author.
/// </summary>
/// <remarks>
/// A refused save is the case that matters: the operation did not happen,
/// so the status line has to say why and what to do about it instead of
/// only naming the command that failed.
/// </remarks>
public sealed class SaveRefusalStatusTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-save-refusal-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SaveRefusalStatusTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Refusal");
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
	public void ARefusedSaveExplainsItselfInTheStatusLine()
	{
		EditorShell shell = BuildShell();

		_session.SaveScene();
		_session.CreateEntity("Cube");

		string path = Path.Combine(
			_session.Project!.RootPath,
			_session.ScenePath!.Replace('/', Path.DirectorySeparatorChar));

		File.WriteAllText(path, "{ \"name\": \"Written elsewhere\" }");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

		MenuItem save = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "File")
			.Children
			.Single(child => child.Label == "Save Scene");

		save.Action!.Invoke();

		Assert.True(
			_session.Status.Contains("changed on disk", StringComparison.Ordinal),
			$"status: '{_session.Status}', dirty {_session.IsSceneDirty}, " +
			$"file {new FileInfo(path).Length} bytes");
		Assert.Contains("Save Scene As", _session.Status, StringComparison.Ordinal);
		Assert.Equal(
			"{ \"name\": \"Written elsewhere\" }",
			File.ReadAllText(path));
	}

	private EditorShell BuildShell()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(0.0, new UiSize(1440f, 900f), UiPoint.Zero);
		_context.Draw();

		return shell;
	}
}
