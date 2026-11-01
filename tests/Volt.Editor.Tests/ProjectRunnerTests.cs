using Volt.Editor.Core.Projects;
using Volt.Editor.Validation;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers running a project's startup scene without the editor.
/// </summary>
/// <remarks>
/// A run simulates fixed ticks, so the counts and the messages are
/// reproducible. The project is scaffolded through the same editor call
/// a developer uses, which registers its component types process-wide;
/// that is why this class joins the gameplay collection.
/// </remarks>
[Collection(GameplayProjectCollection.Name)]
public sealed class ProjectRunnerTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		"volt-run-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A locked file only leaves a temporary folder behind.
		}
	}

	[Fact]
	public void ItRunsTheStartupSceneWithoutGameCode()
	{
		string root = CreateProject();

		ProjectRunResult result = ProjectRunner.Run(root, 30);

		Assert.True(result.Succeeded, result.Error);
		Assert.Equal(30, result.Ticks);
		Assert.True(result.EntityCount > 0);
		Assert.False(string.IsNullOrWhiteSpace(result.SceneName));
		Assert.Null(result.AssemblyName);
		Assert.Contains(
			result.Warnings,
			warning => warning.Contains(
				"no built game code",
				StringComparison.Ordinal));
	}

	[Fact]
	public void AMissingSceneStopsTheRunBeforeItStarts()
	{
		string root = CreateProject();
		EditorProject project = EditorProject.Open(root);

		File.Delete(Path.Combine(
			root,
			project.StartupScenePath.Replace(
				'/',
				Path.DirectorySeparatorChar)));

		ProjectRunResult result = ProjectRunner.Run(root, 10);

		Assert.False(result.Succeeded);
		Assert.False(string.IsNullOrWhiteSpace(result.Error));
		Assert.NotEmpty(result.Problems);
		Assert.Equal(0, result.Ticks);
	}

	[Fact]
	public void AFolderWithoutAManifestCannotRun()
	{
		ProjectRunResult result = ProjectRunner.Run(_root, 10);

		Assert.False(result.Succeeded);
		Assert.Contains(
			"could not be opened",
			result.Error,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ARunNeedsAtLeastOneTick()
	{
		string root = CreateProject();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => ProjectRunner.Run(root, 0));
	}

	private string CreateProject()
	{
		string root = Path.Combine(_root, "Game");

		using (EditorSession session = new())
		{
			Assert.True(session.NewProject(root, "Game"));
		}

		return root;
	}
}
