namespace Volt.Gameplay.Abilities;

/// <summary>
/// Classifies one ability state change.
/// </summary>
/// <remarks>
/// Ending and cancellation are separate kinds so a listener can tell a
/// normal completion from an interruption without tracking requests of
/// its own.
/// </remarks>
public enum GameplayAbilityChangeKind : byte
{
	/// <summary>
	/// An ability was granted to an entity.
	/// </summary>
	Granted = 0,

	/// <summary>
	/// A grant was removed from an entity.
	/// </summary>
	Revoked = 1,

	/// <summary>
	/// An ability became active.
	/// </summary>
	Activated = 2,

	/// <summary>
	/// An active ability ended normally, including the automatic end
	/// of an ability that ends on activation.
	/// </summary>
	Ended = 3,

	/// <summary>
	/// An active ability was cancelled, by a request, by another
	/// ability's cancel tags, or by its own cancel tags.
	/// </summary>
	Cancelled = 4
}
