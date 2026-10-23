namespace Volt.Gameplay.Tasks;

/// <summary>
/// Identifies the concrete behavior of a pending ability task.
/// </summary>
/// <remarks>
/// Tasks are a discriminated pool record rather than a class hierarchy,
/// so a pending task costs one compact slot regardless of kind. A new
/// kind adds fields to the record and a branch to the dispatcher; it
/// does not add a managed object per task or a virtual callback per
/// completion.
/// </remarks>
public enum GameplayAbilityTaskKind : byte
{
	/// <summary>
	/// Completes after a simulation duration elapses.
	/// </summary>
	Duration = 0,

	/// <summary>
	/// Completes when a matching gameplay event is delivered.
	/// </summary>
	GameplayEvent = 1,

	/// <summary>
	/// Completes when a tag condition holds, re-evaluated on tag
	/// changes rather than polled.
	/// </summary>
	TagCondition = 2,

	/// <summary>
	/// Completes when the first of several sources fires: a timeout, a
	/// watched gameplay event, or a watched tag condition.
	/// </summary>
	Any = 3,

	/// <summary>
	/// Completes when application code completes its handle.
	/// </summary>
	Custom = 4
}
