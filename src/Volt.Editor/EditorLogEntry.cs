namespace Volt.Editor;

public enum EditorLogLevel
{
	Info = 0,
	Warning = 1,
	Error = 2
}

public readonly record struct EditorLogEntry(
	DateTime Timestamp,
	EditorLogLevel Level,
	string Message);
