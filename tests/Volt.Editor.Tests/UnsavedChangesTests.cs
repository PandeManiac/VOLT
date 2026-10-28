using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the guard around operations that would discard a dirty scene:
/// the session must defer them, and the shell must offer Save, Discard,
/// or Cancel before continuing.
/// </summary>
public sealed class UnsavedChangesTests : IDisposable
{
	private const string SecondScene = "Scenes/Second.volt.scene.json";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-unsaved-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public UnsavedChangesTests()
	{
		Directory.CreateDirectory(_root);

		_session.NewProject(Path.Combine(_root, "Game"), "Guard");
		_session.CreateScene(SecondScene, "Second");
		_session.OpenScene(EditorProject.DefaultStartupScene);
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
	public void Session_DefersSceneChangeUntilTheGuardRunsIt()
	{
		SceneDocument original = _session.Scene!;

		_session.CreateEntity("Cube");
		Assert.True(_session.IsSceneDirty);

		Action? pending = null;

		_session.UnsavedChangesGuard = continuation =>
		{
			pending = continuation;
			return false;
		};

		Assert.False(_session.OpenScene(SecondScene));
		Assert.Same(original, _session.Scene);
		Assert.True(_session.IsSceneDirty);
		Assert.NotNull(pending);

		pending!.Invoke();

		Assert.NotSame(original, _session.Scene);
		Assert.Equal("Second", _session.Scene!.Name);
		Assert.False(_session.IsSceneDirty);
	}

	[Fact]
	public void Session_DefersProjectChangeUntilTheGuardRunsIt()
	{
		string other = Path.Combine(_root, "Other");

		using (EditorSession creator = new())
		{
			creator.NewProject(other, "Other");
		}

		SceneDocument original = _session.Scene!;

		_session.CreateEntity("Cube");

		Action? pending = null;

		_session.UnsavedChangesGuard = continuation =>
		{
			pending = continuation;
			return false;
		};

		Assert.False(_session.OpenProject(other));
		Assert.Equal("Guard", _session.Project!.Name);
		Assert.Same(original, _session.Scene);
		Assert.NotNull(pending);

		pending!.Invoke();

		Assert.Equal("Other", _session.Project!.Name);
		Assert.False(_session.IsSceneDirty);
	}

	[Fact]
	public void Session_DoesNotAskForACleanScene()
	{
		bool guarded = false;

		_session.UnsavedChangesGuard = _ =>
		{
			guarded = true;
			return false;
		};

		Assert.True(_session.OpenScene(SecondScene));
		Assert.False(guarded);
		Assert.Equal("Second", _session.Scene!.Name);
	}

	[Fact]
	public void Shell_SavesBeforeOpeningAnotherScene()
	{
		EditorShell shell = BuildShell();
		SceneDocument original = _session.Scene!;

		_session.CreateEntity("Cube");

		Assert.False(_session.OpenScene(SecondScene));
		Assert.Same(original, _session.Scene);

		Layout();
		Click(FindButton(_context.Modal!, "Save"));

		Assert.Equal("Second", _session.Scene!.Name);
		Assert.False(_session.IsSceneDirty);
		Assert.False(_context.HasModal);

		string saved = File.ReadAllText(
			Path.Combine(
				_root,
				"Game",
				"Scenes",
				"Main.volt.scene.json"));

		Assert.Contains("Cube", saved);
	}

	[Fact]
	public void Shell_CancelLeavesTheDirtySceneAndAsksAgain()
	{
		EditorShell shell = BuildShell();
		SceneDocument original = _session.Scene!;

		_session.CreateEntity("Cube");

		Assert.False(_session.OpenScene(SecondScene));
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Cancel"));

		Assert.Same(original, _session.Scene);
		Assert.True(_session.IsSceneDirty);
		Assert.False(_context.HasModal);

		// A canceled request must not leave a stale continuation that a
		// later request silently reuses.
		Assert.False(_session.OpenScene(SecondScene));
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Discard"));

		Assert.Equal("Second", _session.Scene!.Name);
	}

	[Fact]
	public void Shell_AsksBeforeClosingAProject()
	{
		EditorShell shell = BuildShell();

		_session.CreateEntity("Cube");

		MenuItem close = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "File")
			.Children
			.Single(item => item.Label == "Close Project");

		close.Action!.Invoke();

		Assert.Equal("Guard", _session.Project!.Name);
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Discard"));

		Assert.Null(_session.Project);
		Assert.Null(_session.Scene);
		Assert.False(_context.HasModal);
	}

	[Fact]
	public void Shell_AsksBeforeExitAndExitsAfterDiscard()
	{
		EditorShell shell = BuildShell();
		bool exited = false;

		shell.ExitRequested += () => exited = true;
		_session.CreateEntity("Cube");

		shell.RequestExit();

		Assert.False(exited);
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Discard"));

		Assert.True(exited);
		Assert.False(_context.HasModal);
	}

	[Fact]
	public void Shell_ExitsImmediatelyWhenTheSceneIsClean()
	{
		EditorShell shell = BuildShell();
		bool exited = false;

		shell.ExitRequested += () => exited = true;
		shell.RequestExit();

		Assert.True(exited);
		Assert.False(_context.HasModal);
	}

	[Fact]
	public void ConfirmWindowClose_KeepsTheWindowForUnsavedChanges()
	{
		EditorShell shell = BuildShell();

		Assert.True(shell.ConfirmWindowClose());

		_session.CreateEntity("Cube");

		Assert.False(shell.ConfirmWindowClose());
		Assert.True(_context.HasModal);

		Layout();
		Click(FindButton(_context.Modal!, "Cancel"));

		Assert.True(_session.IsSceneDirty);
		Assert.False(_context.HasModal);
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
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

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
