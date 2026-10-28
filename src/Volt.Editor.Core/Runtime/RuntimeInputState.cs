namespace Volt.Editor.Core.Runtime;

/// <summary>
/// Input sampled once per rendered frame and consumed by fixed
/// simulation ticks.
/// </summary>
/// <remarks>
/// The editor and an independent game can both feed this type. It
/// contains no platform types, so gameplay code is portable and
/// testable.
/// </remarks>
public sealed class RuntimeInputState
{
	public bool MoveLeft { get; set; }

	public bool MoveRight { get; set; }

	public bool MoveForward { get; set; }

	public bool MoveBackward { get; set; }

	public bool PrimaryAction { get; set; }

	public float Horizontal =>
		(MoveRight ? 1f : 0f) - (MoveLeft ? 1f : 0f);

	public float Vertical =>
		(MoveForward ? 1f : 0f) - (MoveBackward ? 1f : 0f);

	public void Clear()
	{
		MoveLeft = false;
		MoveRight = false;
		MoveForward = false;
		MoveBackward = false;
		PrimaryAction = false;
	}
}
