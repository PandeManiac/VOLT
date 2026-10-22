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
/// <see cref="GameLoopSettings.MaximumFrameDelta"/>. <see cref="ScaledDelta"/>
/// is that interval multiplied by the frame's effective
/// <see cref="TimeScale"/>; it is the simulation time offered to the
/// fixed-step accumulator, before the per-frame cap.
/// </para>
/// <para>
/// Two dropped-time values are reported because the clamp and the cap
/// are measured on different clocks once scaling is active.
/// <see cref="DroppedTime"/> is real time and can be subtracted from
/// the monotonic clock; <see cref="DroppedSimulationTime"/> is the
/// simulation-time equivalent. At <c>1.0x</c> they are equal.
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
		TimeSpan scaledDelta,
		TimeSpan droppedTime,
		TimeSpan droppedSimulationTime,
		int fixedSteps,
		long totalFixedSteps,
		double simulationDeltaSeconds,
		double accumulatedRemainderSeconds,
		float interpolationAlpha,
		TimeScale timeScale,
		bool isPaused)
	{
		FrameIndex = frameIndex;
		RealDelta = realDelta;
		ScaledDelta = scaledDelta;
		DroppedTime = droppedTime;
		DroppedSimulationTime = droppedSimulationTime;
		FixedSteps = fixedSteps;
		TotalFixedSteps = totalFixedSteps;
		SimulationDeltaSeconds = simulationDeltaSeconds;
		AccumulatedRemainderSeconds = accumulatedRemainderSeconds;
		InterpolationAlpha = interpolationAlpha;
		TimeScale = timeScale;
		IsPaused = isPaused;
	}

	/// <summary>
	/// Gets the one-based index of the completed frame.
	/// </summary>
	public long FrameIndex { get; }

	/// <summary>
	/// Gets the observed real elapsed interval, clamped to
	/// <see cref="GameLoopSettings.MaximumFrameDelta"/>.
	/// </summary>
	public TimeSpan RealDelta { get; }

	/// <summary>
	/// Gets the simulation time offered to the accumulator: the
	/// clamped real delta multiplied by <see cref="TimeScale"/>.
	/// </summary>
	/// <remarks>
	/// The value is zero while paused and when the time scale is zero.
	/// It includes time that the fixed-step cap later dropped; compare
	/// it with <see cref="SimulationDeltaSeconds"/> and
	/// <see cref="DroppedSimulationTime"/>.
	/// </remarks>
	public TimeSpan ScaledDelta { get; }

	/// <summary>
	/// Gets the real time discarded by the elapsed-time clamp, the
	/// per-frame fixed-step cap, or a pause requested during the
	/// frame.
	/// </summary>
	/// <remarks>
	/// A frame that pauses part-way through reports the fixed steps
	/// that were planned but will never run, so the unexecuted debt is
	/// visible instead of silently disappearing. Time discarded by
	/// the cap is converted back to real time through the frame's
	/// scale.
	/// </remarks>
	public TimeSpan DroppedTime { get; }

	/// <summary>
	/// Gets the simulation time discarded by the elapsed-time clamp,
	/// the per-frame fixed-step cap, or a pause requested during the
	/// frame.
	/// </summary>
	/// <remarks>
	/// This is the value to compare with
	/// <see cref="SimulationDeltaSeconds"/> when reasoning about
	/// simulation progress. At <c>1.0x</c> it equals
	/// <see cref="DroppedTime"/>.
	/// </remarks>
	public TimeSpan DroppedSimulationTime { get; }

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
	/// Gets the simulation time accumulated toward the next fixed
	/// step, in seconds.
	/// </summary>
	public double AccumulatedRemainderSeconds { get; }

	/// <summary>
	/// Gets the interpolation fraction in <c>[0, 1)</c>.
	/// </summary>
	public float InterpolationAlpha { get; }

	/// <summary>
	/// Gets the time scale that was effective for this frame.
	/// </summary>
	/// <remarks>
	/// A change requested by a callback is reported here from the
	/// next frame onward.
	/// </remarks>
	public TimeScale TimeScale { get; }

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
