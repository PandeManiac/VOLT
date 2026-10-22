namespace Volt.Core.Timing;

/// <summary>
/// Describes one completed fixed simulation step to a
/// <see cref="PostFixedUpdateCallback"/>.
/// </summary>
/// <remarks>
/// <para>
/// This callback runs after the step's fixed-update callbacks and
/// after every scheduled callback due on that step, so it observes
/// the complete state the step committed. A renderer uses it to
/// capture the transform history that interpolation reads.
/// </para>
/// <para>
/// The context is a value type with no managed references, so
/// invoking every registered callback allocates nothing.
/// </para>
/// </remarks>
public readonly struct PostFixedUpdateContext
{
	internal PostFixedUpdateContext(
		long stepIndex,
		double simulationTimeSeconds,
		bool isPaused)
	{
		StepIndex = stepIndex;
		SimulationTimeSeconds = simulationTimeSeconds;
		IsPaused = isPaused;
	}

	/// <summary>
	/// Gets the one-based index of the completed fixed step.
	/// </summary>
	public long StepIndex { get; }

	/// <summary>
	/// Gets the simulation time at the start of that step, in
	/// seconds.
	/// </summary>
	public double SimulationTimeSeconds { get; }

	/// <summary>
	/// Gets whether the step was requested by
	/// <see cref="GameLoop.StepOnce"/> while paused.
	/// </summary>
	public bool IsPaused { get; }
}

/// <summary>
/// Handles the end of one fixed simulation step.
/// </summary>
/// <param name="context">
/// The completed step and its pause state.
/// </param>
/// <remarks>
/// The callback runs synchronously inside
/// <see cref="GameLoop.Advance"/>, once per executed fixed step, on
/// the game thread. Registering or unregistering callbacks from
/// inside <see cref="GameLoop.Advance"/> is rejected, exactly like
/// the update and fixed-update registrations.
/// </remarks>
public delegate void PostFixedUpdateCallback(
	in PostFixedUpdateContext context);
