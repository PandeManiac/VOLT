namespace Volt.Gameplay.Tasks;

/// <summary>
/// Explains why a task reached a terminal state.
/// </summary>
public enum GameplayAbilityTaskCompletionReason : byte
{
	/// <summary>
	/// The task's condition was met: an event arrived, a tag condition
	/// held, or the task was completed explicitly.
	/// </summary>
	ConditionMet = 0,

	/// <summary>
	/// The simulation deadline elapsed.
	/// </summary>
	Elapsed = 1,

	/// <summary>
	/// <see cref="GameplayAbilityTaskSystem.Cancel"/> removed the task.
	/// </summary>
	Cancelled = 2,

	/// <summary>
	/// The owning ability activation ended, was cancelled, or was
	/// revoked.
	/// </summary>
	AbilityEnded = 3
}
