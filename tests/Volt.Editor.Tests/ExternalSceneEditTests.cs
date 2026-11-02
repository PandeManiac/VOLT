using Volt.Editor.Core.Projects;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers a scene file changed on disk by something other than the
/// editor while it is open.
/// </summary>
/// <remarks>
/// The file on disk is the only copy of the other writer's changes, so a
/// save that would replace it has to stop and say so. Saving again after
/// the editor's own write, or writing to a new path, must stay possible.
/// </remarks>
public sealed class ExternalSceneEditTests : IDisposable
{
	private const string CopyPath = "Scenes/Copy.volt.scene.json";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-external-edit-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();

	public ExternalSceneEditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "External");
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
	public void SavingRefusesToOverwriteAFileChangedElsewhere()
	{
		_session.SaveScene();
		_session.CreateEntity("Cube");

		string path = ScenePath();
		string outside = "{ \"name\": \"Written elsewhere\" }";

		File.WriteAllText(path, outside);
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
			_session.SaveScene);

		Assert.Contains("changed on disk", failure.Message, StringComparison.Ordinal);
		Assert.Equal(outside, File.ReadAllText(path));
	}

	[Fact]
	public void SavingTwiceAfterTheEditorsOwnWriteIsAllowed()
	{
		_session.SaveScene();
		_session.CreateEntity("Cube");
		_session.SaveScene();

		// The second save moved the stamp forward; a third must not see
		// the editor's own write as a foreign change.
		_session.CreateEntity("Sphere");
		_session.SaveScene();

		Assert.Contains(
			"Sphere",
			File.ReadAllText(ScenePath()),
			StringComparison.Ordinal);
	}

	[Fact]
	public void SaveAsStillWritesAfterAnExternalEdit()
	{
		_session.SaveScene();
		_session.CreateEntity("Cube");

		string path = ScenePath();

		File.WriteAllText(path, "{ \"name\": \"Written elsewhere\" }");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

		_session.SaveSceneAs(CopyPath);

		string copy = Path.Combine(
			_session.Project!.RootPath,
			CopyPath.Replace('/', Path.DirectorySeparatorChar));

		Assert.True(File.Exists(copy));
		Assert.Contains("Cube", File.ReadAllText(copy), StringComparison.Ordinal);
	}

	private string ScenePath()
	{
		return Path.Combine(
			_session.Project!.RootPath,
			_session.ScenePath!.Replace('/', Path.DirectorySeparatorChar));
	}
}
