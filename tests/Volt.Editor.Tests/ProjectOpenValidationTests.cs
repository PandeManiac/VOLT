using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the project-wide check that runs when a project opens: a moved
/// file is reported before the developer starts working, and a project
/// that was just created does not produce noise about itself.
/// </summary>
public sealed class ProjectOpenValidationTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-open-validation-{Guid.NewGuid():N}");

	public ProjectOpenValidationTests()
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
	public void OpeningAProjectReportsWhatCannotBeDrawn()
	{
		string root = Path.Combine(_root, "Game");

		using (EditorSession creator = new())
		{
			creator.NewProject(root, "Game");

			SceneEntity cube = creator.Scene!.Entities.First(
				entity => entity.Name == "Cube");

			cube.GetComponent<MeshRendererData>()!.Mesh =
				"Assets/Meshes/gone.obj";

			creator.SaveScene();
		}

		using EditorSession opened = new();

		Assert.True(opened.OpenProject(root));

		Assert.Contains(
			opened.LogEntries,
			entry => entry.Level == EditorLogLevel.Error &&
				entry.Message.Contains(
					"gone.obj",
					StringComparison.Ordinal));

		Assert.Contains(
			opened.LogEntries,
			entry => entry.Message.Contains(
				"Project validation: 1 error",
				StringComparison.Ordinal));

		Assert.Contains("project error", opened.Status);
	}

	[Fact]
	public void OpeningASoundProjectReportsNoErrors()
	{
		string root = Path.Combine(_root, "Game");

		using (EditorSession creator = new())
		{
			creator.NewProject(root, "Game");
		}

		using EditorSession opened = new();

		Assert.True(opened.OpenProject(root));

		Assert.DoesNotContain(
			opened.LogEntries,
			entry => entry.Level == EditorLogLevel.Error);

		Assert.DoesNotContain("project error", opened.Status);
	}

	[Fact]
	public void CreatingAProjectSkipsTheProjectWideCheck()
	{
		using EditorSession session = new();

		session.NewProject(Path.Combine(_root, "Fresh"), "Fresh");

		Assert.DoesNotContain(
			session.LogEntries,
			entry => entry.Message.Contains(
				"Project validation:",
				StringComparison.Ordinal));
	}
}
