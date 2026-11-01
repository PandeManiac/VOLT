using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the source a Console entry carries: game messages, validation
/// results, and editor actions can be read apart because they are tagged
/// where they are written.
/// </summary>
[Collection(GameplayProjectCollection.Name)]
public sealed class ConsoleSourceTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-console-source-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public ConsoleSourceTests()
	{
		Directory.CreateDirectory(_root);
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
			// A leftover file must not fail a test that already ran.
		}
	}

	[Fact]
	public void GameMessagesAreTaggedAsGame()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");
		Assert.NotNull(_session.BuildGameCode());

		_session.StartPlay();
		_session.Update(PlaySession.FixedDeltaSeconds);

		EditorLogEntry entry = Assert.Single(
			_session.LogEntries,
			candidate => candidate.Message.Contains(
				"Game: Player motor ready",
				StringComparison.Ordinal));

		Assert.Equal(EditorLogSource.Game, entry.Source);

		_session.Stop();
	}

	[Fact]
	public void ValidationMessagesAreTaggedAsValidation()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		SceneEntity cube = _session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Mesh =
			"Assets/Meshes/missing.obj";

		_session.StartPlay();

		Assert.Contains(
			_session.LogEntries,
			entry => entry.Source == EditorLogSource.Validation &&
				entry.Message.Contains(
					"missing.obj",
					StringComparison.Ordinal));
	}

	[Fact]
	public void EditorMessagesDefaultToEditor()
	{
		_session.Log("A note");

		EditorLogEntry entry = Assert.Single(_session.LogEntries);

		Assert.Equal(EditorLogSource.Editor, entry.Source);
		Assert.Equal(EditorLogLevel.Info, entry.Level);
	}

	[Fact]
	public void AnUnbuiltProjectOpensWithAValidationWarningOnly()
	{
		string root = Path.Combine(_root, "Game");

		using (EditorSession creator = new())
		{
			creator.NewProject(root, "Game");
		}

		using EditorSession opened = new();

		Assert.True(opened.OpenProject(root));

		Assert.Contains(
			opened.LogEntries,
			entry => entry.Source == EditorLogSource.Validation &&
				entry.Level == EditorLogLevel.Warning &&
				entry.Message.Contains(
					"not been built",
					StringComparison.Ordinal));

		Assert.DoesNotContain(
			opened.LogEntries,
			entry => entry.Source == EditorLogSource.Validation &&
				entry.Level == EditorLogLevel.Error);
	}
}
