namespace Volt.Core.Timing;

/// <summary>
/// Describes one fixed simulation step to a
/// <see cref="FixedUpdateCallback"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FixedDeltaSeconds"/> is the same value on every fixed
/// step for a given <see cref="GameLoopSettings.FixedUpdatesPerSecond"/>,
/// regardless of how slowly the application is rendering. Simulation
/// code must integrate with it rather than with
/// <see cref="UpdateContext.DeltaSeconds"/>.
/// </para>
/// <para>
/// <see cref="FixedDelta"/> is the same interval rounded to
/// <see cref="System.TimeSpan"/> resolution, which cannot represent
/// 1/60 second exactly. Prefer <see cref="FixedDeltaSeconds"/> for
/// integration and <see cref="FixedDelta"/> only for reporting.
/// </para>
/// </remarks>
public readonly struct FixedUpdateContext
{
	internal FixedUpdateContext(
		long stepIndex,
		float fixedDeltaSeconds,
		TimeSpan fixedDelta,
		double simulationTimeSeconds,
		bool isPaused)
	{
		StepIndex = stepIndex;
		FixedDeltaSeconds = fixedDeltaSeconds;
		FixedDelta = fixedDelta;
		SimulationTimeSeconds = simulationTimeSeconds;
		IsPaused = isPaused;
	}

	/// <summary>
	/// Gets the one-based index of this fixed step.
	/// </summary>
	public long StepIndex { get; }

	/// <summary>
	/// Gets the fixed step duration, in seconds.
	/// </summary>
	public float FixedDeltaSeconds { get; }

	/// <summary>
	/// Gets the fixed step duration rounded to
	/// <see cref="System.TimeSpan"/> resolution.
	/// </summary>
	public TimeSpan FixedDelta { get; }

	/// <summary>
	/// Gets the simulation time at the start of this step, in
	/// seconds.
	/// </summary>
	/// <remarks>
	/// The value is <c>StepIndex / FixedUpdatesPerSecond</c> computed
	/// from the exact step count, so it never accumulates rounding
	/// error.
	/// </remarks>
	public double SimulationTimeSeconds { get; }

	/// <summary>
	/// Gets whether this step was requested by
	/// <see cref="GameLoop.StepOnce"/> while paused.
	/// </summary>
	public bool IsPaused { get; }
}

/// <summary>
/// Handles one fixed simulation step.
/// </summary>
/// <param name="context">The step index, fixed delta, and pause state.</param>
public delegate void FixedUpdateCallback(in FixedUpdateContext context);
