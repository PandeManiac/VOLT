using Volt.Ecs;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Effects;

/// <summary>
/// A pair of tag sets that an entity's tags must satisfy: every
/// required tag must be present, and no ignored tag may be present.
/// </summary>
/// <remarks>
/// <para>
/// Matching is non-exact in both directions, exactly like Unreal's
/// <c>FGameplayTagRequirements::RequirementsMet</c>: a required
/// <c>Status</c> is satisfied by <c>Status.Stunned</c>, and an ignored
/// <c>Status</c> is violated by <c>Status.Stunned</c>. This is what
/// makes a "not stunned" requirement reject every kind of stun
/// without listing them.
/// </para>
/// <para>
/// The type is immutable and shared by definitions. Evaluating it is
/// two presence checks per tag with no allocation, because it reads
/// the entity tag store's cached counts.
/// </para>
/// </remarks>
public sealed class GameplayTagRequirements
{
	private readonly GameplayTagSet _required;
	private readonly GameplayTagSet _ignored;

	/// <summary>
	/// Creates requirements from two tag sets.
	/// </summary>
	/// <param name="required">
	/// Tags that must be present, or <see langword="null"/> for none.
	/// </param>
	/// <param name="ignored">
	/// Tags that must be absent, or <see langword="null"/> for none.
	/// </param>
	public GameplayTagRequirements(
		GameplayTagSet? required = null,
		GameplayTagSet? ignored = null)
	{
		_required = required ?? GameplayTagSet.Empty;
		_ignored = ignored ?? GameplayTagSet.Empty;
	}

	/// <summary>
	/// Gets the requirements that no tag satisfies and every tag set
	/// satisfies.
	/// </summary>
	public static GameplayTagRequirements Empty { get; } = new();

	/// <summary>
	/// Gets the tags that must be present.
	/// </summary>
	public GameplayTagSet Required => _required;

	/// <summary>
	/// Gets the tags that must be absent.
	/// </summary>
	public GameplayTagSet Ignored => _ignored;

	/// <summary>
	/// Gets whether these requirements accept every tag set.
	/// </summary>
	public bool IsEmpty => _required.IsEmpty && _ignored.IsEmpty;

	/// <summary>
	/// Determines whether an entity's tags satisfy these requirements.
	/// </summary>
	/// <param name="store">The entity tag store to read.</param>
	/// <param name="entity">The entity.</param>
	/// <returns>
	/// <see langword="true"/> when every required tag is present and
	/// no ignored tag is present. A dead entity has no tags, so only an
	/// empty requirement set accepts it.
	/// </returns>
	public bool IsSatisfiedBy(GameplayTagStore store, Entity entity)
	{
		ArgumentNullException.ThrowIfNull(store);

		return store.HasAll(entity, _required)
			&& !store.HasAny(entity, _ignored);
	}

	/// <summary>
	/// Determines whether a tag set satisfies these requirements.
	/// </summary>
	/// <param name="tags">The tags to test.</param>
	public bool IsSatisfiedBy(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return tags.HasAll(_required) && !tags.HasAny(_ignored);
	}

	/// <summary>
	/// Creates requirements that only require tags.
	/// </summary>
	/// <param name="required">The tags that must be present.</param>
	public static GameplayTagRequirements Require(GameplayTagSet required)
	{
		return new GameplayTagRequirements(required);
	}

	/// <summary>
	/// Creates requirements that only ignore tags.
	/// </summary>
	/// <param name="ignored">The tags that must be absent.</param>
	public static GameplayTagRequirements Ignore(GameplayTagSet ignored)
	{
		return new GameplayTagRequirements(ignored: ignored);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		if (IsEmpty)
		{
			return "GameplayTagRequirements(none)";
		}

		return $"GameplayTagRequirements(require {{{_required.Serialize()}}}, " +
			$"ignore {{{_ignored.Serialize()}}})";
	}
}
