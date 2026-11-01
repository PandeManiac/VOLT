namespace Volt.Editor.Core.Runtime;

/// <summary>The severity of a message a running game wrote.</summary>
public enum RuntimeLogLevel
{
	/// <summary>Ordinary progress a developer asked to see.</summary>
	Info = 0,

	/// <summary>Something the game recovered from but should notice.</summary>
	Warning = 1
}

/// <summary>One message a running game wrote.</summary>
/// <param name="Level">The severity.</param>
/// <param name="Message">The text, without an engine prefix.</param>
public readonly record struct RuntimeLogEntry(
	RuntimeLogLevel Level,
	string Message);

/// <summary>
/// Collects messages written by a project's game code while it runs.
/// </summary>
/// <remarks>
/// <para>
/// A game system receives the world it runs against, so
/// <c>scene.Log.Info("...")</c> is the whole contract: no static logger, no
/// editor reference, and nothing to configure before a world exists. The
/// log belongs to one world, so starting Play again begins empty.
/// </para>
/// <para>
/// The buffer is bounded. A system that logs every tick would otherwise
/// grow the editor's memory for as long as Play runs and bury every other
/// message, so past the limit the count of dropped messages is kept and
/// reported once instead.
/// </para>
/// </remarks>
public sealed class RuntimeLog
{
	/// <summary>
	/// The number of messages one world keeps.
	/// </summary>
	public const int MaximumEntries = 500;

	private readonly List<RuntimeLogEntry> _entries = [];
	private int _dropped;

	/// <summary>Gets the messages written so far, in order.</summary>
	public IReadOnlyList<RuntimeLogEntry> Entries => _entries;

	/// <summary>
	/// Gets how many messages were discarded after the buffer filled.
	/// </summary>
	public int DroppedCount => _dropped;

	/// <summary>Records an informational message.</summary>
	/// <param name="message">The text to record.</param>
	public void Info(string message)
	{
		Write(RuntimeLogLevel.Info, message);
	}

	/// <summary>Records a warning.</summary>
	/// <param name="message">The text to record.</param>
	public void Warning(string message)
	{
		Write(RuntimeLogLevel.Warning, message);
	}

	private void Write(RuntimeLogLevel level, string message)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(message);

		if (_entries.Count >= MaximumEntries)
		{
			_dropped++;
			return;
		}

		_entries.Add(new RuntimeLogEntry(level, message));
	}
}
