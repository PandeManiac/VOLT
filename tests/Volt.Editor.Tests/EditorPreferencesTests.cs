namespace Volt.Editor.Tests;

/// <summary>
/// Covers editor settings that belong to the project that is open, not
/// to the project the executable happened to start with.
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
	public void OpeningProject_LoadsItsSavedSettings()
	{
		string projectRoot = Path.Combine(_root, "Game");

		_session.NewProject(projectRoot, "Game");
		WriteSettings(projectRoot, new EditorSettings
		{
			TargetFrameRate = 144,
			InterfaceScale = 1.25f,
			WindowWidth = 1280,
			WindowHeight = 720,
			WindowX = -120,
			WindowY = 80
		});

		_session.CloseProject();

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		Assert.Equal(60, _session.TargetFrameRate);
		Assert.Equal(1f, shell.UiScale, 3);

		_session.OpenProject(projectRoot);

		// The scale waits for input dispatch to finish, then rebuilds.
		shell.Update(0.016);

		Assert.Equal(144, _session.TargetFrameRate);
		Assert.Equal(1.25f, shell.UiScale, 3);
		Assert.Equal(1280, preferences.Current.WindowWidth);
		Assert.Equal(720, preferences.Current.WindowHeight);
		Assert.Equal(-120, preferences.Current.WindowX);
		Assert.Equal(80, preferences.Current.WindowY);
		Assert.Equal(1, preferences.ProjectRevision);
	}

	[Fact]
	public void OpeningAnotherProject_SwapsToThatProjectsSettings()
	{
		string first = Path.Combine(_root, "First");
		string second = Path.Combine(_root, "Second");

		_session.NewProject(first, "First");
		WriteSettings(first, new EditorSettings
		{
			TargetFrameRate = 144,
			InterfaceScale = 1.5f,
			WindowWidth = 1280,
			WindowHeight = 720
		});

		_session.NewProject(second, "Second");
		WriteSettings(second, new EditorSettings
		{
			TargetFrameRate = 30,
			InterfaceScale = 0.75f,
			WindowWidth = 800,
			WindowHeight = 600
		});

		_session.CloseProject();

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		_session.OpenProject(first);
		shell.Update(0.016);

		Assert.Equal(144, _session.TargetFrameRate);
		Assert.Equal(1.5f, shell.UiScale, 3);

		_session.OpenProject(second);
		shell.Update(0.016);

		Assert.Equal(30, _session.TargetFrameRate);
		Assert.Equal(0.75f, shell.UiScale, 3);
		Assert.Equal(800, preferences.Current.WindowWidth);
		Assert.Equal(600, preferences.Current.WindowHeight);
	}

	[Fact]
	public void Save_WritesTheOpenProjectRatherThanTheStartupState()
	{
		string projectRoot = Path.Combine(_root, "Game");

		_session.NewProject(projectRoot, "Game");
		WriteSettings(projectRoot, new EditorSettings
		{
			TargetFrameRate = 144,
			InterfaceScale = 1.25f,
			WindowWidth = 1280,
			WindowHeight = 720,
			WindowX = -120,
			WindowY = 80
		});

		_session.CloseProject();

		using EditorShell shell = new(_session);
		using EditorPreferences preferences = new(
			_session,
			shell,
			new EditorSettings());

		_session.OpenProject(projectRoot);
		shell.Update(0.016);

		_session.TargetFrameRate = 30;
		shell.ApplyUiScale(1.5f);

		preferences.Save(1000, 700, -60, 40);

		EditorSettings stored = EditorPreferences.Load(projectRoot);

		Assert.Equal(30, stored.TargetFrameRate);
		Assert.Equal(1.5f, stored.InterfaceScale, 3);
		Assert.Equal(1000, stored.WindowWidth);
		Assert.Equal(700, stored.WindowHeight);
		Assert.Equal(-60, stored.WindowX);
		Assert.Equal(40, stored.WindowY);
	}

	private static void WriteSettings(
		string projectRoot,
		EditorSettings settings)
	{
		string path = EditorPreferences.SettingsPath(projectRoot);

		Directory.CreateDirectory(
			Path.GetDirectoryName(path) ?? projectRoot);

		File.WriteAllText(path, settings.Serialize());
	}
}
