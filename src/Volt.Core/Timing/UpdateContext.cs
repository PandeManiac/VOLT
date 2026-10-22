namespace Volt.Core.Timing;

/// <summary>
/// Describes one application frame to an
/// <see cref="UpdateCallback"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="DeltaSeconds"/> is real elapsed time, not simulation
/// time, so interactive camera movement and menu animation stay
/// smooth even when the frame executed no fixed step. Use
/// <see cref="SimulationDeltaSeconds"/> when the value must reflect
/// simulation progression instead.
/// </para>
/// <para>
/// The context is a value type with no managed references, so passing
/// it to every registered callback allocates nothing.
/// </para>
/// </remarks>
public readonly struct UpdateContext
{
	internal UpdateContext(
		long frameIndex,
		TimeSpan realDelta,
		TimeSpan scaledDelta,
		TimeScale timeScale,
		int fixedSteps,
		double simulationDeltaSeconds,
		float interpolationAlpha,
		bool isPaused)
	{
		FrameIndex = frameIndex;
		RealDelta = realDelta;
		DeltaSeconds = (float)realDelta.TotalSeconds;
		ScaledDelta = scaledDelta;
		ScaledDeltaSeconds = (float)scaledDelta.TotalSeconds;
		TimeScale = timeScale;
		FixedSteps = fixedSteps;
		SimulationDeltaSeconds = simulationDeltaSeconds;
		InterpolationAlpha = interpolationAlpha;
		IsPaused = isPaused;
	}

	/// <summary>
	/// Gets the one-based frame index.
	/// </summary>
	public long FrameIndex { get; }

	/// <summary>
	/// Gets the real elapsed interval for this frame.
	/// </summary>
	public TimeSpan RealDelta { get; }

	/// <summary>
	/// Gets the real elapsed interval in seconds.
	/// </summary>
	public float DeltaSeconds { get; }

	/// <summary>
	/// Gets the simulation time offered to the fixed-step
	/// accumulator, after clamping and scaling.
	/// </summary>
	/// <remarks>
	/// Zero while paused or at a zero time scale. Part of this value
	/// can be discarded by the fixed-step cap; see
	/// <see cref="GameLoop.TotalDroppedSimulationTime"/>.
	/// </remarks>
	public TimeSpan ScaledDelta { get; }

	/// <summary>
	/// Gets <see cref="ScaledDelta"/> in seconds.
	/// </summary>
	public float ScaledDeltaSeconds { get; }

	/// <summary>
	/// Gets the time scale that was effective for this frame.
	/// </summary>
	/// <remarks>
	/// Changing <see cref="GameLoop.TimeScale"/> here affects the next
	/// frame, not the fixed steps already planned for this one.
	/// </remarks>
	public TimeScale TimeScale { get; }

	/// <summary>
	/// Gets the fixed steps this frame executed.
	/// </summary>
	public int FixedSteps { get; }

	/// <summary>
	/// Gets the simulation time those fixed steps represented, in
	/// seconds. Zero when no step was due.
	/// </summary>
	public double SimulationDeltaSeconds { get; }

	/// <summary>
	/// Gets the interpolation fraction the renderer should use for
	/// this frame, in <c>[0, 1)</c>.
	/// </summary>
	public float InterpolationAlpha { get; }

	/// <summary>
	/// Gets whether the simulation is paused. An update still runs
	/// while paused so input and menus remain responsive.
	/// </summary>
	public bool IsPaused { get; }
}

/// <summary>
/// Handles one application frame.
/// </summary>
/// <param name="context">The frame timing and pause state.</param>
/// <remarks>
/// The delegate is created once at registration, so an ordinary method
/// group costs nothing per frame. The context is passed by
/// <c>in</c> reference and has no managed references.
/// </remarks>
public delegate void UpdateCallback(in UpdateContext context);
