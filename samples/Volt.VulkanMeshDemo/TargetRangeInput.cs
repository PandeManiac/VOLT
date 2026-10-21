namespace Volt.VulkanMeshDemo;

/// <summary>
/// Describes the gameplay actions requested for one frame.
/// </summary>
/// <remarks>
/// <para>
/// Real input and the deterministic script both produce this value, and
/// the gameplay controller consumes only this value. The interactive
/// path builds it from <see cref="CameraInputSnapshot"/>; the scripted
/// path builds it from its own step state. Neither path writes score,
/// completion flags, or components directly, so a scripted run
/// exercises exactly the code a player exercises.
/// </para>
/// <para>
/// Both flags describe one-shot edges, never held state, so repeating a
/// frame with the same command cannot fire twice or restart twice.
/// </para>
/// </remarks>
internal readonly record struct TargetRangeCommand(bool Fire, bool Restart)
{
	/// <summary>
	/// Gets a command that requests nothing.
	/// </summary>
	public static TargetRangeCommand None => default;

	/// <summary>
	/// Gets a command that fires once.
	/// </summary>
	public static TargetRangeCommand FireOnly => new(Fire: true, Restart: false);

	/// <summary>
	/// Gets a command that restarts the round.
	/// </summary>
	public static TargetRangeCommand RestartOnly =>
		new(Fire: false, Restart: true);

	/// <summary>
	/// Builds the command for one sampled input frame.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Keyboard focus is checked first: an unfocused window never fires
	/// and never restarts, even if a malformed or synthesized snapshot
	/// reports an edge. The sampler already suppresses both edges
	/// without focus, and this guard keeps that contract at the point
	/// where gameplay consumes input rather than trusting the producer.
	/// </para>
	/// <para>
	/// Restart wins when both edges land on the same frame: a restart
	/// followed by a shot at a target that no longer exists would make
	/// the frame's outcome depend on ordering. Dropping the shot is
	/// unambiguous and cannot score.
	/// </para>
	/// </remarks>
	public static TargetRangeCommand FromInput(in CameraInputSnapshot snapshot)
	{
		if (!snapshot.KeyboardFocused)
		{
			return None;
		}

		if (snapshot.RestartRequested)
		{
			return RestartOnly;
		}

		return snapshot.FireRequested
			? FireOnly
			: None;
	}
}
