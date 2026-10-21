using System.Diagnostics;

namespace Volt.Core.Timing;

/// <summary>
/// Supplies the monotonic time a <see cref="GameLoop"/> uses to
/// advance simulation.
/// </summary>
/// <remarks>
/// <para>
/// A clock is a counter plus its rate, not a wall-clock date. Time is
/// therefore unaffected by time-zone changes, daylight-saving
/// transitions, or a system clock adjustment, and it never moves
/// backwards during normal operation.
/// </para>
/// <para>
/// The loop subtracts two <see cref="Timestamp"/> samples and converts
/// the difference with <see cref="TicksPerSecond"/>, so an
/// implementation never has to expose absolute time. A test clock only
/// has to advance <see cref="Timestamp"/> by a known number of ticks.
/// </para>
/// <para>
/// The loop reads the timestamp from the thread that calls
/// <see cref="GameLoop.Advance"/>. An implementation must not require a
/// window, a graphics device, or platform initialization.
/// </para>
/// </remarks>
public interface IMonotonicClock
{
	/// <summary>
	/// Gets the current counter value.
	/// </summary>
	long Timestamp { get; }

	/// <summary>
	/// Gets how many <see cref="Timestamp"/> units one second
	/// contains.
	/// </summary>
	long TicksPerSecond { get; }
}

/// <summary>
/// Provides a monotonic clock backed by <see cref="Stopwatch"/>.
/// </summary>
/// <remarks>
/// <see cref="Stopwatch.GetTimestamp"/> is the highest-resolution
/// monotonic counter .NET exposes through a portable API, and
/// <see cref="Stopwatch.Frequency"/> is its rate. The shared instance
/// is stateless, so every loop may use it.
/// </remarks>
public sealed class MonotonicClock : IMonotonicClock
{
	/// <summary>
	/// Gets the shared process-wide clock.
	/// </summary>
	public static MonotonicClock Shared { get; } = new();

	private MonotonicClock()
	{
	}

	/// <inheritdoc />
	public long Timestamp => Stopwatch.GetTimestamp();

	/// <inheritdoc />
	public long TicksPerSecond => Stopwatch.Frequency;
}
