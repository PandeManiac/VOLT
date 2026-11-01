using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what Play does with a scene the renderer cannot fully draw: it
/// reports the reference and stays in authoring rather than running a
/// scene with a silent hole in it, while warnings alone never block.
/// </summary>
public sealed class PlayValidationTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-play-validation-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();

	public PlayValidationTests()
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
	public void PlayRefusesAMeshThatCannotBeDrawn()
	{
		OpenProject();

		SceneEntity cube = _session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Mesh =
			"Assets/Meshes/missing.obj";

		_session.StartPlay();

		Assert.Equal(PlayState.Stopped, _session.PlayState);
		Assert.Null(_session.Play);

		Assert.Contains(
			_session.LogEntries,
			entry => entry.Level == EditorLogLevel.Error &&
				entry.Message.Contains(
					"Assets/Meshes/missing.obj",
					StringComparison.Ordinal));

		Assert.Contains("Play refused", _session.Status);

		// Authoring is untouched, so the reference can be fixed.
		Assert.NotNull(_session.Scene.Find(cube.Id));
	}

	[Fact]
	public void PlayStartsWhenOnlyWarningsAreFound()
	{
		OpenProject();

		SceneEntity cube = _session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		cube.GetComponent<MeshRendererData>()!.Material =
			"Assets/Materials/Missing.material";

		_session.StartPlay();

		Assert.Equal(PlayState.Playing, _session.PlayState);
		Assert.NotNull(_session.Play);

		_session.Stop();
	}

	[Fact]
	public void PlayStartsWithBuiltInMeshes()
	{
		OpenProject();

		_session.StartPlay();

		Assert.Equal(PlayState.Playing, _session.PlayState);

		_session.Stop();
		Assert.Equal(PlayState.Stopped, _session.PlayState);
	}

	[Fact]
	public void TheRefusedReferenceCanBeFixedAndPlayed()
	{
		EditorProject project = OpenProject();

		SceneEntity cube = _session.Scene!.Entities.First(
			entity => entity.Name == "Cube");

		MeshRendererData renderer =
			cube.GetComponent<MeshRendererData>()!;

		renderer.Mesh = "Assets/Meshes/missing.obj";

		_session.StartPlay();
		Assert.Equal(PlayState.Stopped, _session.PlayState);

		string path = Path.Combine(
			project.AssetsPath,
			"Meshes",
			"missing.obj");

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(
			path,
			"""
			v 0 0 0
			v 1 0 0
			v 0 1 0
			f 1 2 3
			""");

		project.AssetCatalog.Refresh();
		_session.StartPlay();

		Assert.Equal(PlayState.Playing, _session.PlayState);

		_session.Stop();
	}

	private EditorProject OpenProject()
	{
		_session.NewProject(Path.Combine(_root, "Game"), "Game");

		return Assert.IsType<EditorProject>(_session.Project);
	}
}
