namespace Volt.Editor;

public enum EditorLogLevel
{
	Info = 0,
	Warning = 1,
	Error = 2
}

/// <summary>
/// Where a Console message came from.
/// </summary>
/// <remarks>
/// Severity says how loud a message is; the source says who said it. A
/// project that fails validation and a running game writing to its log
/// both produce warnings, and reading them apart is what lets a developer
/// answer one question at a time.
/// </remarks>
public enum EditorLogSource
{
	/// <summary>The editor itself: actions, state, and faults.</summary>
	Editor = 0,

	/// <summary>The result of checking the project's references.</summary>
	Validation = 1,

	/// <summary>A message written by a project's game code.</summary>
	Game = 2
}

public readonly record struct EditorLogEntry(
	DateTime Timestamp,
	EditorLogLevel Level,
	string Message,
	EditorLogSource Source = EditorLogSource.Editor);
