namespace Volt.Gameplay.Events;

/// <summary>
/// Selects how a subscription matches the tag of a published event.
/// </summary>
/// <remarks>
/// Unreal's generic gameplay event callbacks are keyed by the exact
/// event tag. Exact matching is therefore the default, and hierarchical
/// matching is an explicit opt-in.
/// </remarks>
public enum GameplayEventMatch : byte
{
	/// <summary>
	/// The subscription matches only its own tag.
	/// </summary>
	Exact = 0,

	/// <summary>
	/// The subscription matches its own tag and every descendant of it.
	/// </summary>
	Hierarchical = 1
}
