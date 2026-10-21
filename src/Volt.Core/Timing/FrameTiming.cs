namespace Volt.Core.Timing;

/// <summary>
/// Describes the timing of one completed <see cref="GameLoop.Advance"/>.
/// </summary>
/// <remarks>
/// <para>
/// The value is returned by <see cref="GameLoop.Advance"/> and kept in
/// <see cref="GameLoop.LastFrame"/> so a renderer can interpolate
/// without allocating. Every field is a value type.
/// </para>
/// <para>
/// <see cref="RealDelta"/> is the elapsed monotonic interval the frame
/// actually observed, already clamped to
/// <see cref="GameLoopSettings.MaximumFrameDelta"/>; the part beyond
/// that clamp is reported by <see cref="DroppedTime"/>, together with
/// the simulation time discarded by the fixed-step cap.
/// </para>
/// <para>
/// <see cref="SimulationDeltaSeconds"/> is the simulation time that
/// real fixed steps represented, which can differ from the real delta
/// on a frame that executed zero or several steps.
/// </para>
/// </remarks>
public readonly record struct FrameTiming
{
	internal FrameTiming(
		long frameIndex,
		TimeSpan realDelta,
		TimeSpan droppedTime,
		int fixedSteps,
		long totalFixedSteps,
		double simulationDeltaSeconds,
		double accumulatedRemainderSeconds,
		float interpolationAlpha,
		bool isPaused)
	{
		FrameIndex = frameIndex;
		RealDelta = realDelta;
		DroppedTime = droppedTime;
		FixedSteps = fixedSteps;
		TotalFixedSteps = totalFixedSteps;
		SimulationDeltaSeconds = simulationDeltaSeconds;
		AccumulatedRemainderSeconds = accumulatedRemainderSeconds;
		InterpolationAlpha = interpolationAlpha;
		IsPaused = isPaused;
	}

	/// <summary>
	/// Gets the one-based index of the completed frame.
	/// </summary>
	public long FrameIndex { get; }

	/// <summary>
	/// Gets the observed elapsed interval, clamped to
	/// <see cref="GameLoopSettings.MaximumFrameDelta"/>.
	/// </summary>
	public TimeSpan RealDelta { get; }

	/// <summary>
	/// Gets the simulation time discarded by the elapsed-time clamp,
	/// the per-frame fixed-step cap, or a pause requested during the
	/// frame.
	/// </summary>
	/// <remarks>
	/// A frame that pauses part-way through reports the fixed steps
	/// that were planned but will never run, so the unexecuted debt is
	/// visible instead of silently disappearing.
	/// </remarks>
	public TimeSpan DroppedTime { get; }

	/// <summary>
	/// Gets the fixed steps executed by this frame.
	/// </summary>
	public int FixedSteps { get; }

	/// <summary>
	/// Gets the total fixed steps executed since the loop was created.
	/// </summary>
	public long TotalFixedSteps { get; }

	/// <summary>
	/// Gets the simulation time the executed fixed steps represented,
	/// in seconds.
	/// </summary>
	public double SimulationDeltaSeconds { get; }

	/// <summary>
	/// Gets the time accumulated toward the next fixed step, in
	/// seconds.
	/// </summary>
	public double AccumulatedRemainderSeconds { get; }

	/// <summary>
	/// Gets the interpolation fraction in <c>[0, 1)</c>.
	/// </summary>
	public float InterpolationAlpha { get; }

	/// <summary>
	/// Gets whether the loop was paused when this frame started.
	/// </summary>
	/// <remarks>
	/// A <see cref="GameLoop.Pause"/> requested by a callback takes
	/// effect for the remaining steps of this frame and is reported
	/// as <c>true</c> from the next frame.
	/// </remarks>
	public bool IsPaused { get; }
}
