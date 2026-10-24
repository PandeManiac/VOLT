using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Events;

/// <summary>
/// Describes one gameplay event: its tag, the entities involved, and an
/// optional typed payload.
/// </summary>
/// <remarks>
/// <para>
/// A gameplay event is a value, not a managed object. Publishing copies
/// it onto the stack, so a high-frequency event such as a hit or a
/// status change does not create garbage.
/// </para>
/// <para>
/// The tag carries the meaning, exactly as it does for abilities and
/// effects. <c>Event.Damage.Received</c> can be subscribed to exactly,
/// or <c>Event.Damage</c> can be subscribed to hierarchically and
/// receive every damage event. The source and target are entity
/// identities, so a listener can filter without resolving a live
/// object.
/// </para>
/// </remarks>
public readonly struct GameplayEvent
{
	/// <summary>
	/// Creates an event with no participating entities or payload.
	/// </summary>
	/// <param name="tag">The registered event tag.</param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public GameplayEvent(GameplayTag tag)
		: this(tag, default, default, GameplayEventPayload.None)
	{
	}

	/// <summary>
	/// Creates an event with a source and a target.
	/// </summary>
	/// <param name="tag">The registered event tag.</param>
	/// <param name="source">The entity that caused the event.</param>
	/// <param name="target">The entity the event happened to.</param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public GameplayEvent(GameplayTag tag, Entity source, Entity target)
		: this(tag, source, target, GameplayEventPayload.None)
	{
	}

	/// <summary>
	/// Creates an event with a source, a target, and a payload.
	/// </summary>
	/// <param name="tag">The registered event tag.</param>
	/// <param name="source">The entity that caused the event.</param>
	/// <param name="target">The entity the event happened to.</param>
	/// <param name="payload">The typed payload.</param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public GameplayEvent(
		GameplayTag tag,
		Entity source,
		Entity target,
		GameplayEventPayload payload)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A gameplay event needs a registered tag.",
				nameof(tag));
		}

		Tag = tag;
		Source = source;
		Target = target;
		Payload = payload;
	}

	/// <summary>
	/// Gets the event tag.
	/// </summary>
	public GameplayTag Tag { get; }

	/// <summary>
	/// Gets the entity that caused the event, or
	/// <c>default(Entity)</c> when there is none.
	/// </summary>
	public Entity Source { get; }

	/// <summary>
	/// Gets the entity the event happened to, or
	/// <c>default(Entity)</c> when there is none.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the typed payload.
	/// </summary>
	public GameplayEventPayload Payload { get; }

	/// <summary>
	/// Gets whether this value carries a registered event tag.
	/// </summary>
	public bool IsValid => Tag.IsValid;

	/// <inheritdoc />
	public override string ToString()
	{
		if (!IsValid)
		{
			return "GameplayEvent(none)";
		}

		return $"GameplayEvent({Tag.Name}, {Source} -> {Target}, {Payload})";
	}
}
