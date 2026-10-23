using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Events;

/// <summary>
/// Describes what a gameplay event carries besides its tag and the two
/// participating entities.
/// </summary>
/// <remarks>
/// <para>
/// A payload is a discriminated value, not a managed object. Dispatching
/// an event therefore never boxes a number, allocates an event object,
/// or captures a closure. The three shapes cover the common cases:
/// a magnitude such as damage, an entity reference such as an
/// instigator, and a secondary gameplay tag such as an element.
/// </para>
/// <para>
/// An event with no payload has <see cref="Kind"/> equal to
/// <see cref="GameplayEventPayloadKind.None"/>. Reading a shape the
/// payload does not hold throws, so a misread cannot silently produce
/// zero and look like valid data.
/// </para>
/// </remarks>
public readonly struct GameplayEventPayload
{
	private readonly GameplayEventPayloadKind _kind;
	private readonly double _number;
	private readonly Entity _entity;
	private readonly GameplayTag _tag;

	private GameplayEventPayload(
		GameplayEventPayloadKind kind,
		double number,
		Entity entity,
		GameplayTag tag)
	{
		_kind = kind;
		_number = number;
		_entity = entity;
		_tag = tag;
	}

	/// <summary>
	/// Gets a payload with no value.
	/// </summary>
	public static GameplayEventPayload None => default;

	/// <summary>
	/// Gets which value the payload holds.
	/// </summary>
	public GameplayEventPayloadKind Kind => _kind;

	/// <summary>
	/// Gets whether the payload holds no value.
	/// </summary>
	public bool IsEmpty => _kind == GameplayEventPayloadKind.None;

	/// <summary>
	/// Gets the numeric value.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The payload does not hold a number.
	/// </exception>
	public double Number =>
		_kind == GameplayEventPayloadKind.Number
			? _number
			: throw new InvalidOperationException(
				$"The event payload holds {_kind}, not a number.");

	/// <summary>
	/// Gets the entity value.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The payload does not hold an entity.
	/// </exception>
	public Entity Entity =>
		_kind == GameplayEventPayloadKind.Entity
			? _entity
			: throw new InvalidOperationException(
				$"The event payload holds {_kind}, not an entity.");

	/// <summary>
	/// Gets the tag value.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The payload does not hold a tag.
	/// </exception>
	public GameplayTag Tag =>
		_kind == GameplayEventPayloadKind.Tag
			? _tag
			: throw new InvalidOperationException(
				$"The event payload holds {_kind}, not a tag.");

	/// <summary>
	/// Creates a numeric payload, such as a damage amount.
	/// </summary>
	/// <param name="value">The finite numeric value.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The value is not finite.
	/// </exception>
	public static GameplayEventPayload FromNumber(double value)
	{
		if (!double.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"A gameplay event payload must be finite.");
		}

		return new GameplayEventPayload(
			GameplayEventPayloadKind.Number,
			value,
			default,
			GameplayTag.None);
	}

	/// <summary>
	/// Creates an entity payload, such as an instigator.
	/// </summary>
	/// <param name="entity">The entity value.</param>
	public static GameplayEventPayload FromEntity(Entity entity)
	{
		return new GameplayEventPayload(
			GameplayEventPayloadKind.Entity,
			0d,
			entity,
			GameplayTag.None);
	}

	/// <summary>
	/// Creates a tag payload, such as an element or a hit location.
	/// </summary>
	/// <param name="tag">The valid tag value.</param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public static GameplayEventPayload FromTag(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A gameplay event payload tag must be registered.",
				nameof(tag));
		}

		return new GameplayEventPayload(
			GameplayEventPayloadKind.Tag,
			0d,
			default,
			tag);
	}

	/// <summary>
	/// Reads the numeric value when the payload holds one.
	/// </summary>
	/// <param name="value">The numeric value.</param>
	/// <returns>
	/// <see langword="true"/> when the payload holds a number.
	/// </returns>
	public bool TryGetNumber(out double value)
	{
		value = _number;

		return _kind == GameplayEventPayloadKind.Number;
	}

	/// <summary>
	/// Reads the entity value when the payload holds one.
	/// </summary>
	/// <param name="value">The entity value.</param>
	/// <returns>
	/// <see langword="true"/> when the payload holds an entity.
	/// </returns>
	public bool TryGetEntity(out Entity value)
	{
		value = _entity;

		return _kind == GameplayEventPayloadKind.Entity;
	}

	/// <summary>
	/// Reads the tag value when the payload holds one.
	/// </summary>
	/// <param name="value">The tag value.</param>
	/// <returns>
	/// <see langword="true"/> when the payload holds a tag.
	/// </returns>
	public bool TryGetTag(out GameplayTag value)
	{
		value = _tag;

		return _kind == GameplayEventPayloadKind.Tag;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return _kind switch
		{
			GameplayEventPayloadKind.Number => $"Number({_number})",
			GameplayEventPayloadKind.Entity => $"Entity({_entity})",
			GameplayEventPayloadKind.Tag => $"Tag({_tag})",
			_ => "None"
		};
	}
}
