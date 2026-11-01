namespace Volt.Editor;

/// <summary>
/// Which Console entries the panel shows.
/// </summary>
/// <remarks>
/// The value lives on the session rather than in the panel so it survives
/// the interface rebuild a scale change causes, and so the preferences
/// coordinator can persist it without reaching into a widget.
/// </remarks>
public readonly record struct EditorConsoleFilter(
	bool ShowInfo = true,
	bool ShowWarnings = true,
	bool ShowErrors = true,
	EditorLogSource? Source = null)
{
	/// <summary>Gets the filter that shows everything.</summary>
	/// <remarks>
	/// The values are written out rather than relying on the parameter
	/// defaults: <c>new()</c> binds to a record struct's parameterless
	/// constructor, which leaves every field false and would hide the
	/// whole Console instead of showing it.
	/// </remarks>
	public static EditorConsoleFilter Default { get; } = new(
		ShowInfo: true,
		ShowWarnings: true,
		ShowErrors: true,
		Source: null);

	/// <summary>
	/// Gets whether an entry passes both the severity and source filters.
	/// </summary>
	/// <param name="level">The entry's severity.</param>
	/// <param name="source">Where the entry came from.</param>
	public bool Shows(EditorLogLevel level, EditorLogSource source)
	{
		if (Source is EditorLogSource required && source != required)
		{
			return false;
		}

		return level switch
		{
			EditorLogLevel.Error => ShowErrors,
			EditorLogLevel.Warning => ShowWarnings,
			_ => ShowInfo
		};
	}

	/// <summary>
	/// Gets the persisted spelling of the source filter.
	/// </summary>
	public string SourceSetting => Source switch
	{
		EditorLogSource.Editor => "editor",
		EditorLogSource.Validation => "validation",
		EditorLogSource.Game => "game",
		_ => "all"
	};

	/// <summary>
	/// Builds a filter from persisted settings.
	/// </summary>
	/// <param name="settings">The loaded editor settings.</param>
	/// <remarks>
	/// The settings type already normalizes an unknown source to
	/// <c>all</c>, so this only maps the known spellings.
	/// </remarks>
	public static EditorConsoleFilter FromSettings(EditorSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return new EditorConsoleFilter(
			settings.ConsoleShowInfo,
			settings.ConsoleShowWarnings,
			settings.ConsoleShowErrors,
			settings.ConsoleSource switch
			{
				"editor" => EditorLogSource.Editor,
				"validation" => EditorLogSource.Validation,
				"game" => EditorLogSource.Game,
				_ => null
			});
	}
}
