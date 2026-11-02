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
	string Message)
{
	/// <summary>
	/// The order in which the session accepted the entry, or -1 when an
	/// entry was built outside a session.
	/// </summary>
	/// <remarks>
	/// The console renders a suffix of the retained log and drops rows
	/// when the session retires entries from the front. Arrival order
	/// lets it work out which rows are still wanted by arithmetic
	/// instead of searching the log for each new message.
	/// </remarks>
	public long Sequence { get; init; } = -1;
}
