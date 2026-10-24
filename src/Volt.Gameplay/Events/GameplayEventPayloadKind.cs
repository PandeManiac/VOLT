namespace Volt.Gameplay.Events;

/// <summary>
/// Identifies which value a <see cref="GameplayEventPayload"/> holds.
/// </summary>
public enum GameplayEventPayloadKind : byte
{
	/// <summary>
	/// The payload holds no value.
	/// </summary>
	None = 0,

	/// <summary>
	/// The payload holds a finite number.
	/// </summary>
	Number = 1,

	/// <summary>
	/// The payload holds an entity reference.
	/// </summary>
	Entity = 2,

	/// <summary>
	/// The payload holds a gameplay tag.
	/// </summary>
	Tag = 3
}
