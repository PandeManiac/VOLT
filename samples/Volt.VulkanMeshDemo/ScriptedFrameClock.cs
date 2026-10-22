using Volt.Core.Timing;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// A monotonic clock that advances by exactly one 60 Hz application
/// frame when the scripted timing demonstration says so.
/// </summary>
/// <remarks>
/// <para>
/// This clock exists only for the deterministic <c>--timing-script</c>
/// mode. The interactive sample uses
/// <see cref="MonotonicClock.Shared"/> and real elapsed time; no
/// production path is driven by presentation counts.
/// </para>
/// <para>
/// The injected delta is 16,666,667 nanoseconds, slightly more than
/// one 1/60 second. The accumulator carries the 20-unit excess, so
/// each accepted frame still executes exactly one fixed step and the
/// phase never doubles a step within the bounded run.
/// </para>
/// </remarks>
internal sealed class ScriptedFrameClock : IMonotonicClock
{
	/// <summary>
	/// Injected elapsed time per accepted presentation, in
	/// nanoseconds.
	/// </summary>
	public const long FrameTicks = 16_666_667;

	/// <summary>
	/// Injected elapsed time per accepted presentation, as a
	/// <see cref="TimeSpan"/>.
	/// </summary>
	public static TimeSpan FrameDelta =>
		TimeSpan.FromTicks(FrameTicks / 100);

	public long Timestamp { get; private set; }

	public long TicksPerSecond => 1_000_000_000;

	/// <summary>
	/// Injects one frame of elapsed time for the next
	/// <see cref="GameLoop.Advance"/>.
	/// </summary>
	public void AdvanceFrame()
	{
		Timestamp += FrameTicks;
	}

	/// <summary>
	/// Injects an exact elapsed interval for the next
	/// <see cref="GameLoop.Advance"/>.
	/// </summary>
	/// <param name="nanoseconds">
	/// The interval, in nanoseconds. It must not be negative.
	/// </param>
	/// <remarks>
	/// The interpolation demonstration uses this to place the
	/// accumulator at exact fractional alphas, including multiple
	/// steps in one application frame.
	/// </remarks>
	public void AdvanceNanoseconds(long nanoseconds)
	{
		if (nanoseconds < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(nanoseconds),
				nanoseconds,
				"Injected elapsed time cannot be negative.");
		}

		Timestamp += nanoseconds;
	}
}
