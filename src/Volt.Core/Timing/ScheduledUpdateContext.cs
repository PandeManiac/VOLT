namespace Volt.Core.Timing;

/// <summary>
/// Describes one invocation of scheduled simulation work.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StepIndex"/> is the fixed step on which the work ran,
/// never an application frame. Several scheduled callbacks can share
/// one step, but each executes at most once per step.
/// </para>
/// <para>
/// <see cref="SkippedInvocations"/> is nonzero only when simulation
/// time jumped past one or more intended deadlines, for example after
/// a long blocking call. The recurring schedule is then advanced to
/// the next deadline after the current step, so the missed work is
/// reported instead of being replayed in a burst.
/// </para>
/// </remarks>
public readonly struct ScheduledUpdateContext
{
	internal ScheduledUpdateContext(
		long stepIndex,
		double simulationTimeSeconds,
		long invocation,
		long skippedInvocations)
	{
		StepIndex = stepIndex;
		SimulationTimeSeconds = simulationTimeSeconds;
		Invocation = invocation;
		SkippedInvocations = skippedInvocations;
	}

	/// <summary>
	/// Gets the fixed step that ran the callback.
	/// </summary>
	public long StepIndex { get; }

	/// <summary>
	/// Gets the simulation time of that step, in seconds.
	/// </summary>
	public double SimulationTimeSeconds { get; }

	/// <summary>
	/// Gets the one-based invocation count for this registration.
	/// </summary>
	public long Invocation { get; }

	/// <summary>
	/// Gets how many intended occurrences this invocation skipped
	/// because simulation time had already passed their deadlines.
	/// </summary>
	public long SkippedInvocations { get; }
}

/// <summary>
/// Handles one occurrence of scheduled simulation work.
/// </summary>
/// <param name="context">The step, simulation time, and invocation count.</param>
/// <remarks>
/// The callback runs synchronously on the thread that called
/// <see cref="GameLoop.Advance"/>; the scheduler never starts a task,
/// timer, or background thread for it.
/// </remarks>
public delegate void ScheduledUpdateCallback(
	in ScheduledUpdateContext context);
