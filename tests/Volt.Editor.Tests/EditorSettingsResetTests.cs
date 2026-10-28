using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the Preferences reset: it restores editor settings and must
/// never disturb the project, the scene, the selection, or undo.
/// </summary>
public sealed class EditorSettingsResetTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-settings-reset-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public EditorSettingsResetTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Reset");
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
	public void Reset_RestoresDefaultsWithoutTouchingAuthoredState()
	{
		using EditorShell shell = new(_session);

		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);
		_session.TargetFrameRate = 144;
		shell.ApplyUiScale(1.5f);

		SceneDocument scene = _session.Scene!;
		Guid sceneId = scene.Id;
		int entityCount = scene.EntityCount;
		int undoCount = _session.Undo.UndoCount;

		shell.ResetEditorSettings();

		Assert.Equal(EditorSettings.DefaultTargetFrameRate, _session.TargetFrameRate);
		Assert.Equal(1f, shell.UiScale, 3);

		// The project, scene, selection, and history are authored state.
		Assert.Same(scene, _session.Scene);
		Assert.Equal(sceneId, scene.Id);
		Assert.Equal(entityCount, scene.EntityCount);
		Assert.Contains(cube.Id, _session.Selection);
		Assert.Equal(undoCount, _session.Undo.UndoCount);
	}

	[Fact]
	public void Reset_AtDefaults_ChangesNothing()
	{
		using EditorShell shell = new(_session);

		SceneDocument scene = _session.Scene!;

		shell.ResetEditorSettings();

		Assert.Equal(60, _session.TargetFrameRate);
		Assert.Equal(1f, shell.UiScale, 3);
		Assert.Same(scene, _session.Scene);
		Assert.Equal("Editor settings reset to defaults", _session.Status);
	}
}
