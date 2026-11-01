using Volt.Editor.Core.Projects;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies that game code authored outside Assets and Scenes reaches the
/// project catalog and opens in the user's own editor, so writing a system
/// does not end in an "is not openable yet" message.
/// </summary>
public sealed class ProjectScriptTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-scripts-{Guid.NewGuid():N}");

	public ProjectScriptTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
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
	public void GameCodeAppearsInTheBrowserAndOpensExternally()
	{
		string opened = string.Empty;
		using EditorSession session = new(
			externalFileOpener: path =>
			{
				opened = path;
				return true;
			});

		session.NewProject(Path.Combine(_root, "Game"), "Game");

		EditorProject project = Assert.IsType<EditorProject>(session.Project);
		string file = WriteScript(project, "EnemyBrain.cs");

		project.AssetCatalog.Refresh();

		ProjectAsset asset = Assert.Single(
			project.AssetCatalog.Assets,
			item => item.Id == "Scripts/EnemyBrain.cs");

		Assert.Equal(ProjectAssetKind.Script, asset.Kind);

		Assert.True(session.OpenProjectFile(asset.Id));
		Assert.Equal(Path.GetFullPath(file), opened);
		Assert.Contains("EnemyBrain.cs", session.Status);
	}

	[Fact]
	public void AFileOutsideTheProjectIsRefused()
	{
		bool invoked = false;
		using EditorSession session = new(
			externalFileOpener: _ =>
			{
				invoked = true;
				return true;
			});

		session.NewProject(Path.Combine(_root, "Game"), "Game");

		Assert.False(session.OpenProjectFile("../Outside.cs"));
		Assert.False(invoked);
		Assert.Contains(
			session.LogEntries,
			entry => entry.Message.Contains(
				"not part of this project",
				StringComparison.Ordinal));
	}

	[Fact]
	public void AFailedLaunchIsReportedWithoutThrowing()
	{
		using EditorSession session = new(externalFileOpener: _ => false);

		session.NewProject(Path.Combine(_root, "Game"), "Game");

		EditorProject project = Assert.IsType<EditorProject>(session.Project);
		WriteScript(project, "EnemyBrain.cs");
		project.AssetCatalog.Refresh();

		Assert.False(session.OpenProjectFile("Scripts/EnemyBrain.cs"));
		Assert.Contains(
			session.LogEntries,
			entry => entry.Level == EditorLogLevel.Warning &&
				entry.Message.Contains(
					"No application is registered",
					StringComparison.Ordinal));
		Assert.Equal("Could not open the file", session.Status);
	}

	[Fact]
	public void AFileWrittenAfterTheLastRefreshStillOpens()
	{
		using EditorSession session = new(externalFileOpener: _ => true);

		session.NewProject(Path.Combine(_root, "Game"), "Game");

		EditorProject project = Assert.IsType<EditorProject>(session.Project);
		WriteScript(project, "FreshSystem.cs");

		// No manual refresh: writing the file is enough.
		Assert.True(session.OpenProjectFile("Scripts/FreshSystem.cs"));
	}

	private static string WriteScript(EditorProject project, string name)
	{
		string file = Path.Combine(project.RootPath, "Scripts", name);
		Directory.CreateDirectory(Path.GetDirectoryName(file) ?? project.RootPath);
		File.WriteAllText(file, "// game code");
		return file;
	}
}
