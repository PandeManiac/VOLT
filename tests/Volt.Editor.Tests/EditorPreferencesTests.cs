using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the settings file itself: what is written for a project, and
/// when. Each project keeps its own preferences, so switching projects
/// must not silently drop the changes made in the one being left.
/// </summary>
public sealed class EditorPreferencesTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-preferences-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();

	public EditorPreferencesTests()
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
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void SwitchingProjectsSavesTheDepartingProjectsSettings()
	{
		string first = Path.Combine(_root, "First");
		string second = Path.Combine(_root, "Second");

		_session.NewProject(first, "First");

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		_session.TargetFrameRate = 144;
		_session.ConsoleShowsInfo = false;

		_session.NewProject(second, "Second");

		// The project that was opened takes its own settings, which for a
		// folder with no file yet are the defaults.
		Assert.Equal(
			EditorSettings.DefaultTargetFrameRate,
			_session.TargetFrameRate);
		Assert.True(_session.ConsoleShowsInfo);

		// The project that was left kept what the author changed.
		EditorSettings saved = EditorPreferences.Load(first);

		Assert.Equal(144, saved.TargetFrameRate);
		Assert.False(saved.ConsoleShowsInfo);
		Assert.True(saved.ConsoleShowsWarnings);
	}

	[Fact]
	public void LeavingAProjectSavesTheLiveWindowGeometry()
	{
		string first = Path.Combine(_root, "First");
		string second = Path.Combine(_root, "Second");

		_session.NewProject(first, "First");

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		preferences.WindowMetrics = () => (1024, 768, 12, 24);

		_session.NewProject(second, "Second");

		EditorSettings saved = EditorPreferences.Load(first);

		Assert.Equal(1024, saved.WindowWidth);
		Assert.Equal(768, saved.WindowHeight);
		Assert.Equal(12, saved.WindowX);
		Assert.Equal(24, saved.WindowY);
	}

	[Fact]
	public void LeavingAProjectKeepsSavedGeometryWhenTheWindowReportsNone()
	{
		string first = Path.Combine(_root, "First");
		string second = Path.Combine(_root, "Second");

		_session.NewProject(first, "First");

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		preferences.Save(1280, 720, 5, 6);
		preferences.WindowMetrics = () => null;

		_session.NewProject(second, "Second");

		EditorSettings saved = EditorPreferences.Load(first);

		Assert.Equal(1280, saved.WindowWidth);
		Assert.Equal(720, saved.WindowHeight);
		Assert.Equal(5, saved.WindowX);
		Assert.Equal(6, saved.WindowY);
	}

	[Fact]
	public void Save_WritesTheLiveValuesForTheOpenProject()
	{
		string project = Path.Combine(_root, "Only");

		_session.NewProject(project, "Only");

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		_session.ConsoleShowsWarnings = false;
		preferences.Save(1280, 720, 40, 60);

		EditorSettings saved = EditorPreferences.Load(project);

		Assert.False(saved.ConsoleShowsWarnings);
		Assert.Equal(1280, saved.WindowWidth);
		Assert.Equal(720, saved.WindowHeight);
		Assert.Equal(40, saved.WindowX);
		Assert.Equal(60, saved.WindowY);
	}
}
