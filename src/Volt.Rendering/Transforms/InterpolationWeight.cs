namespace Volt.Rendering.Transforms;

/// <summary>
/// Chooses the interpolation weight a renderer should display, so a
/// paused or frozen simulation shows its latest committed pose
/// instead of the previous one.
/// </summary>
/// <remarks>
/// <para>
/// Ordinary interpolation at weight zero selects the previous pose,
/// which is correct while simulation advances: it removes the
/// one-step latency between fixed updates and display. It is wrong
/// when simulation stops, because the image would jump back one step
/// on the frame the pause is observed.
/// </para>
/// <para>
/// This policy therefore holds the committed (current) pose while
/// simulation is paused or scaled to zero, and keeps holding it after
/// simulation resumes until a fixed step completes. The first new
/// step shifts that same pose into the previous slot, so the
/// transition from held pose to ordinary interpolation is continuous
/// and never snaps backward.
/// </para>
/// <para>
/// The type is a small mutable value used by the render loop; it has
/// no managed references and allocates nothing.
/// </para>
/// </remarks>
public struct InterpolationWeight
{
	private bool _holdCommittedPose;

	/// <summary>
	/// Gets whether the committed pose is currently being held.
	/// </summary>
	public readonly bool IsHoldingCommittedPose => _holdCommittedPose;

	/// <summary>
	/// Resolves the weight to use for one rendered frame.
	/// </summary>
	/// <param name="alpha">
	/// The loop's interpolation alpha, in <c>[0, 1)</c>.
	/// </param>
	/// <param name="stepCompleted">
	/// Whether the frame executed at least one fixed step.
	/// </param>
	/// <param name="simulationFrozen">
	/// Whether simulation is paused or its time scale is zero.
	/// </param>
	/// <returns>
	/// <c>1</c> to display the committed pose, otherwise
	/// <paramref name="alpha"/> clamped to <c>[0, 1]</c>.
	/// </returns>
	/// <remarks>
	/// Call this once per rendered frame, in the order the frames are
	/// displayed. A non-finite alpha is treated as zero.
	/// </remarks>
	public float Resolve(
		float alpha,
		bool stepCompleted,
		bool simulationFrozen)
	{
		if (simulationFrozen)
		{
			_holdCommittedPose = true;

			return 1.0f;
		}

		if (stepCompleted)
		{
			_holdCommittedPose = false;
		}

		if (_holdCommittedPose)
		{
			return 1.0f;
		}

		return float.IsFinite(alpha)
			? Math.Clamp(alpha, 0.0f, 1.0f)
			: 0.0f;
	}

	/// <summary>
	/// Forgets any held pose.
	/// </summary>
	/// <remarks>
	/// Use after rebuilding the scene, when the history itself has
	/// been reset and there is no previous state to preserve.
	/// </remarks>
	public void Reset()
	{
		_holdCommittedPose = false;
	}
}
