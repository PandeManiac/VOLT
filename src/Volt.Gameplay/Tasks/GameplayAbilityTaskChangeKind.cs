namespace Volt.Gameplay.Tasks;

/// <summary>
/// Describes the transition a task notification reports.
/// </summary>
/// <remarks>
/// A task slot is live only while its task is pending: completion and
/// cancellation release the slot before the notification is raised, so
/// its handle is already stale inside the handler. The notification is
/// therefore the authoritative record of the transition.
/// </remarks>
public enum GameplayAbilityTaskChangeKind : byte
{
	/// <summary>
	/// The task was linked, subscribed, or scheduled and is now
	/// pending.
	/// </summary>
	Started = 0,

	/// <summary>
	/// The task's condition was met.
	/// </summary>
	Completed = 1,

	/// <summary>
	/// The task was cancelled before its condition was met.
	/// </summary>
	Cancelled = 2
}
