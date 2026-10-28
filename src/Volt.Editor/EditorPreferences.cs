namespace Volt.Editor;

/// <summary>
/// Keeps the editor's persisted settings in step with the open project.
/// </summary>
/// <remarks>
/// <para>
/// The executable loads a project before the shell exists, but the
/// start screen can open a different project later. Every project has
/// its own settings file, so this coordinator reloads that project's
/// settings when the project changes and applies the values the live
/// editor can show. Saving uses the project that is open at exit, not
/// the one that was open at startup.
/// </para>
/// <para>
/// The interface scale is requested for the next frame rather than
/// applied immediately: a theme change rebuilds the chrome, and the
/// project change is raised from a widget's own click handler. Waiting
/// until input dispatch finishes keeps that handler's widget alive.
/// </para>
/// </remarks>
internal sealed class EditorPreferences : IDisposable
{
	private const string WorkspaceFolderName = ".volt";
	private const string SettingsFileName = "settings.json";

	private readonly EditorSession _session;
	private readonly EditorShell _shell;
	private bool _disposed;

	public EditorPreferences(
		EditorSession session,
		EditorShell shell,
		EditorSettings initial)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(shell);
		ArgumentNullException.ThrowIfNull(initial);

		_session = session;
		_shell = shell;
		Current = initial.Normalize();

		_session.ProjectChanged += OnProjectChanged;
	}

	/// <summary>
	/// Gets the settings of the project that is currently open.
	/// </summary>
	public EditorSettings Current { get; private set; }

	/// <summary>
	/// Gets how many projects have been adopted since startup.
	/// </summary>
	/// <remarks>
	/// The window's size is captured when this changes, so a saved
	/// window size is preserved for a project that could not resize the
	/// already-created window.
	/// </remarks>
	public int ProjectRevision { get; private set; }

	/// <summary>
	/// Gets the settings file path for a project.
	/// </summary>
	internal static string SettingsPath(string projectRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

		return Path.Combine(
			projectRoot,
			WorkspaceFolderName,
			SettingsFileName);
	}

	/// <summary>
	/// Reads a project's settings, or returns defaults when there is no
	/// readable file.
	/// </summary>
	internal static EditorSettings Load(string? projectRoot)
	{
		if (projectRoot is null)
		{
			return new EditorSettings();
		}

		string path = SettingsPath(projectRoot);

		if (!File.Exists(path))
		{
			return new EditorSettings();
		}

		try
		{
			return EditorSettings.Deserialize(File.ReadAllText(path));
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Ignoring unreadable editor settings: {exception.Message}");

			return new EditorSettings();
		}
	}

	/// <summary>
	/// Writes the open project's settings, including the window size it
	/// should restore at the next launch.
	/// </summary>
	public void Save(int windowWidth, int windowHeight)
	{
		if (_session.Project is not { } project)
		{
			return;
		}

		EditorSettings settings = new()
		{
			TargetFrameRate = _session.TargetFrameRate,
			InterfaceScale = _shell.UiScale,
			WindowWidth = windowWidth,
			WindowHeight = windowHeight
		};

		Write(project.RootPath, settings);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_session.ProjectChanged -= OnProjectChanged;
	}

	private void OnProjectChanged(EditorSession session)
	{
		if (session.Project is not { } project)
		{
			// Closing a project raises this before another one opens.
			// There is no settings file to adopt in that moment.
			return;
		}

		Current = Load(project.RootPath);
		ProjectRevision++;

		_session.TargetFrameRate = Current.TargetFrameRate;
		_shell.RequestUiScale(Current.InterfaceScale);
	}

	private static void Write(string projectRoot, EditorSettings settings)
	{
		try
		{
			string path = SettingsPath(projectRoot);
			string directory = Path.GetDirectoryName(path)
				?? projectRoot;

			Directory.CreateDirectory(directory);

			string temporary = path + ".tmp";

			File.WriteAllText(temporary, settings.Serialize());
			File.Move(temporary, path, overwrite: true);
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Could not save editor settings: {exception.Message}");
		}
	}
}
