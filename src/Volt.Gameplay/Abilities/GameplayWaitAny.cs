using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Describes a wait that completes when the first of several sources
/// fires.
/// </summary>
/// <remarks>
/// <para>
/// A source is a timeout, one or more gameplay events, or a tag
/// condition. A wait with a timeout and an interruption event is the
/// common "animation event or timeout" race; a wait with several events
/// is "any of these hits"; a wait with a tag condition and a timeout is
/// "stunned for up to half a second".
/// </para>
/// <para>
/// The first source that fires wins. The losing sources are part of the
/// same task record, so completing or cancelling the wait releases the
/// deadline, the event subscription, and the tag link together. There
/// are no child tasks and no group bookkeeping.
/// </para>
/// <para>
/// Instances are immutable. The fluent methods return a new value, so a
/// wait can be shared by any number of plans without being copied per
/// activation. This is authoring code: it allocates while the wait is
/// built and allocates nothing when it runs.
/// </para>
/// </remarks>
public sealed class GameplayWaitAny
{
	/// <summary>
	/// The maximum number of watched event tags in one wait.
	/// </summary>
	public const int MaximumEventTagCount = 8;

	private readonly GameplayTag[] _eventTags;

	private GameplayWaitAny(
		float timeoutSeconds,
		GameplayTag[] eventTags,
		GameplayEventMatch match,
		GameplayTag tag,
		GameplayTagWaitMode tagMode,
		bool exact)
	{
		TimeoutSeconds = timeoutSeconds;
		_eventTags = eventTags;
		Match = match;
		Tag = tag;
		TagMode = tagMode;
		Exact = exact;
	}

	/// <summary>
	/// Gets the timeout in simulation seconds, or
	/// <see cref="float.NaN"/> when the wait has no timeout.
	/// </summary>
	public float TimeoutSeconds { get; }

	/// <summary>
	/// Gets whether the wait has a timeout source.
	/// </summary>
	public bool HasTimeout => !float.IsNaN(TimeoutSeconds);

	/// <summary>
	/// Gets the watched event tags.
	/// </summary>
	public ReadOnlySpan<GameplayTag> EventTags => _eventTags;

	/// <summary>
	/// Gets whether the wait has at least one event source.
	/// </summary>
	public bool HasEvents => _eventTags.Length > 0;

	/// <summary>
	/// Gets how event tags are matched.
	/// </summary>
	public GameplayEventMatch Match { get; }

	/// <summary>
	/// Gets the watched tag condition, or
	/// <see cref="GameplayTag.None"/> when the wait has no tag source.
	/// </summary>
	public GameplayTag Tag { get; }

	/// <summary>
	/// Gets whether the wait has a tag source.
	/// </summary>
	public bool HasTag => Tag.IsValid;

	/// <summary>
	/// Gets whether the tag condition waits for presence or absence.
	/// </summary>
	public GameplayTagWaitMode TagMode { get; }

	/// <summary>
	/// Gets whether the tag condition requires an explicit grant.
	/// </summary>
	public bool Exact { get; }

	/// <summary>
	/// Starts describing an empty wait.
	/// </summary>
	/// <remarks>
	/// At least one source must be added before the wait can be used;
	/// an empty wait would never complete.
	/// </remarks>
	public static GameplayWaitAny Create()
	{
		return new GameplayWaitAny(
			float.NaN,
			[],
			GameplayEventMatch.Exact,
			GameplayTag.None,
			GameplayTagWaitMode.Present,
			false);
	}

	/// <summary>
	/// Returns a wait with the given timeout.
	/// </summary>
	/// <param name="seconds">The non-negative timeout.</param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The timeout is negative or not finite.
	/// </exception>
	public GameplayWaitAny WithTimeout(float seconds)
	{
		if (!float.IsFinite(seconds) || seconds < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(seconds),
				seconds,
				"A wait timeout must be finite and not negative.");
		}

		return new GameplayWaitAny(
			seconds,
			_eventTags,
			Match,
			Tag,
			TagMode,
			Exact);
	}

	/// <summary>
	/// Returns a wait that also watches a gameplay event.
	/// </summary>
	/// <param name="tag">The event tag.</param>
	/// <param name="match">
	/// How the tag matches. A hierarchical match also accepts events
	/// with a descendant tag.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The match mode is unknown.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The wait already watches
	/// <see cref="MaximumEventTagCount"/> tags.
	/// </exception>
	public GameplayWaitAny OnEvent(
		GameplayTag tag,
		GameplayEventMatch match = GameplayEventMatch.Exact)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A wait event source needs a registered tag.",
				nameof(tag));
		}

		if (match is not (
			GameplayEventMatch.Exact or GameplayEventMatch.Hierarchical))
		{
			throw new ArgumentOutOfRangeException(
				nameof(match),
				match,
				"Unknown gameplay event match mode.");
		}

		if (_eventTags.Length >= MaximumEventTagCount)
		{
			throw new InvalidOperationException(
				$"A wait watches at most {MaximumEventTagCount} event " +
				"tags. Split the wait into several steps instead.");
		}

		GameplayTag[] eventTags = new GameplayTag[_eventTags.Length + 1];

		_eventTags.CopyTo(eventTags, 0);
		eventTags[^1] = tag;

		// One match mode covers the whole set. Hierarchical accepts
		// every event an exact match accepts, so a mixed wait widens
		// to hierarchical instead of keeping two filters.
		return new GameplayWaitAny(
			TimeoutSeconds,
			eventTags,
			match == GameplayEventMatch.Hierarchical
				? GameplayEventMatch.Hierarchical
				: Match,
			Tag,
			TagMode,
			Exact);
	}

	/// <summary>
	/// Returns a wait that also watches a tag condition.
	/// </summary>
	/// <param name="tag">The watched tag.</param>
	/// <param name="mode">
	/// Whether the wait is for presence or absence.
	/// </param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The wait mode is unknown.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The wait already has a tag source.
	/// </exception>
	public GameplayWaitAny OnTag(
		GameplayTag tag,
		GameplayTagWaitMode mode = GameplayTagWaitMode.Present,
		bool exact = false)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A wait tag source needs a registered tag.",
				nameof(tag));
		}

		if (mode is not (
			GameplayTagWaitMode.Present or GameplayTagWaitMode.Absent))
		{
			throw new ArgumentOutOfRangeException(
				nameof(mode),
				mode,
				"Unknown tag wait mode.");
		}

		if (Tag.IsValid)
		{
			throw new InvalidOperationException(
				"A wait has at most one tag source. Use a custom task " +
				"for a rule that combines several tag conditions.");
		}

		return new GameplayWaitAny(
			TimeoutSeconds,
			_eventTags,
			Match,
			tag,
			mode,
			exact);
	}

	/// <summary>
	/// Determines whether a published event satisfies the event
	/// sources of this wait.
	/// </summary>
	/// <param name="eventTag">The published event tag.</param>
	internal bool MatchesEvent(GameplayTag eventTag)
	{
		if (!eventTag.IsValid)
		{
			return false;
		}

		for (int index = 0; index < _eventTags.Length; index++)
		{
			GameplayTag watched = _eventTags[index];

			if (watched == eventTag)
			{
				return true;
			}

			if (Match == GameplayEventMatch.Hierarchical
				&& eventTag.IsDescendantOf(watched))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Validates the wait, throwing when it has no source.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The wait has no source.
	/// </exception>
	internal void Validate()
	{
		if (HasTimeout || HasEvents || HasTag)
		{
			return;
		}

		throw new InvalidOperationException(
			"A wait needs at least one source: a timeout, an event, or " +
			"a tag condition. An empty wait could never complete.");
	}

	/// <summary>
	/// Chooses the tag and mode used for the single event
	/// subscription.
	/// </summary>
	/// <returns>
	/// The subscription tag and match. The tag is
	/// <see cref="GameplayTag.None"/> when the watched tags have no
	/// common ancestor, which asks the router for every event; the
	/// task still filters by <see cref="MatchesEvent"/>.
	/// </returns>
	internal (GameplayTag Tag, GameplayEventMatch Match)
		GetSubscriptionFilter()
	{
		if (_eventTags.Length == 1)
		{
			return (_eventTags[0], Match);
		}

		GameplayTagRegistry registry = GameplayTagRegistry.Shared;
		GameplayTag first = _eventTags[0];

		// Walk the first tag's ancestry, nearest first, and accept the
		// first ancestor that covers every watched tag. A race
		// therefore wakes only for events under that ancestor instead
		// of for the whole event stream.
		foreach (GameplayTag ancestor in registry.GetAncestors(first))
		{
			if (CoversAll(ancestor))
			{
				return (ancestor, GameplayEventMatch.Hierarchical);
			}
		}

		if (CoversAll(first))
		{
			return (first, GameplayEventMatch.Hierarchical);
		}

		return (GameplayTag.None, GameplayEventMatch.Exact);
	}

	private bool CoversAll(GameplayTag ancestor)
	{
		for (int index = 0; index < _eventTags.Length; index++)
		{
			GameplayTag tag = _eventTags[index];

			if (tag != ancestor && !tag.IsDescendantOf(ancestor))
			{
				return false;
			}
		}

		return true;
	}
}
