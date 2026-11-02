using Volt.Editor.Core.Projects;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the dirty mark following the history position, so a scene that
/// is undone back to what is on disk does not look unsaved.
/// </summary>
public sealed class UndoDirtyStateTests : IDisposable
{
	private const string SecondScene = "Scenes/Second.volt.scene.json";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-undo-dirty-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();

	public UndoDirtyStateTests()
	{
		Directory.CreateDirectory(_root);

		_session.NewProject(Path.Combine(_root, "Game"), "Dirty");
		_session.CreateScene(SecondScene, "Second");
		_session.OpenScene(EditorProject.DefaultStartupScene);
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
	public void UndoingBackToTheSavedSceneLeavesNothingToSave()
	{
		_session.CreateEntity("Cube");
		_session.SaveScene();
		_session.CreateEntity("Sphere");

		Assert.True(_session.IsSceneDirty);

		_session.UndoLast();

		Assert.False(_session.IsSceneDirty);

		// The guard asks only for a dirty scene, so switching now is
		// immediate rather than offering to save unchanged work.
		bool guarded = false;

		_session.UnsavedChangesGuard = _ =>
		{
			guarded = true;
			return false;
		};

		Assert.True(_session.OpenScene(SecondScene));
		Assert.False(guarded);
	}

	[Fact]
	public void RedoAfterUndoingToTheSavedSceneIsAChange()
	{
		_session.CreateEntity("Cube");
		_session.SaveScene();
		_session.CreateEntity("Sphere");
		_session.UndoLast();

		Assert.False(_session.IsSceneDirty);

		_session.RedoLast();

		Assert.True(_session.IsSceneDirty);
	}

	[Fact]
	public void ADifferentEditAtTheSavedDepthIsStillAChange()
	{
		_session.CreateEntity("Cube");
		_session.SaveScene();
		_session.CreateEntity("Sphere");
		_session.UndoLast();

		// Undo dropped back to the saved depth, but this edit is not the
		// one that was saved, so the history no longer marks a clean
		// state even though the depth matches again.
		_session.CreateEntity("Cylinder");

		Assert.True(_session.IsSceneDirty);

		_session.UndoLast();

		Assert.False(_session.IsSceneDirty);
	}

	[Fact]
	public void UndoingEveryEditOfAFreshSceneIsClean()
	{
		Assert.False(_session.IsSceneDirty);

		_session.CreateEntity("Cube");
		Assert.True(_session.IsSceneDirty);

		_session.UndoLast();

		Assert.False(_session.IsSceneDirty);
	}
}
