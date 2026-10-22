namespace Volt.Gameplay.Tags;

/// <summary>
/// An immutable set of gameplay tags with cached ancestors.
/// </summary>
/// <remarks>
/// <para>
/// This is the tag container that shared definitions hold. It is
/// built once, usually during authoring or registration, and from then
/// on every lookup, set operation, and query is a binary search over
/// interned integers that allocates nothing. Unreal's
/// <c>FGameplayTagContainer</c> is mutable and copied by value; VOLT
/// separates the mutable authoring form
/// (<see cref="GameplayTagContainer"/>) from this immutable form so a
/// definition cannot be changed by whoever happens to hold it.
/// </para>
/// <para>
/// <see cref="Tags"/> lists the explicitly added tags in ascending
/// identifier order. Ancestors are cached separately: adding
/// <c>Status.Stunned</c> makes <c>Status</c> match non-exactly but
/// does not make it explicit, so <c>HasTagExact(Status)</c> stays
/// false and the set still compares unequal to one that contains both
/// tags explicitly.
/// </para>
/// <para>
/// Equality is set equality. Construction removes duplicates and sorts
/// by identifier, so two sets built from the same tags in different
/// orders are equal and hash identically.
/// </para>
/// </remarks>
public sealed class GameplayTagSet : IEquatable<GameplayTagSet>
{
	private readonly GameplayTag[] _tags;
	private readonly GameplayTag[] _parents;
	private readonly int _hashCode;

	private GameplayTagSet(GameplayTag[] tags, GameplayTag[] parents)
	{
		_tags = tags;
		_parents = parents;
		_hashCode = ComputeHashCode(tags);
	}

	/// <summary>
	/// Gets the set that contains no tags.
	/// </summary>
	public static GameplayTagSet Empty { get; } = new([], []);

	/// <summary>
	/// Gets the explicitly added tags in ascending identifier order.
	/// </summary>
	public ReadOnlySpan<GameplayTag> Tags => _tags;

	/// <summary>
	/// Gets the cached ancestors in ascending identifier order.
	/// </summary>
	internal ReadOnlySpan<GameplayTag> ParentTags => _parents;

	/// <summary>
	/// Gets the number of explicitly added tags.
	/// </summary>
	/// <remarks>
	/// Cached ancestors do not count, matching Unreal's
	/// <c>FGameplayTagContainer.Num()</c>.
	/// </remarks>
	public int Count => _tags.Length;

	/// <summary>
	/// Gets whether the set contains no explicit tags.
	/// </summary>
	public bool IsEmpty => _tags.Length == 0;

	/// <summary>
	/// Gets the explicit tag at a position.
	/// </summary>
	/// <param name="index">The position in ascending identifier order.</param>
	public GameplayTag this[int index] => _tags[index];

	/// <summary>
	/// Creates a set from tag values.
	/// </summary>
	/// <param name="tags">
	/// The explicit tags. Duplicates are ignored.
	/// </param>
	/// <exception cref="ArgumentException">
	/// A tag is invalid.
	/// </exception>
	/// <remarks>
	/// Ancestors of the supplied tags are resolved through
	/// <see cref="GameplayTagRegistry.Shared"/> and cached, so a set
	/// matches its tags' parents without any further work.
	/// </remarks>
	public static GameplayTagSet Create(params GameplayTag[] tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return CreateCore(tags);
	}

	/// <summary>
	/// Parses a comma-separated list of registered tag names.
	/// </summary>
	/// <param name="text">
	/// Tag names separated by commas. Whitespace around names is
	/// ignored, and an empty string produces the empty set.
	/// </param>
	/// <exception cref="ArgumentException">
	/// A name is not registered or not a valid tag string.
	/// </exception>
	/// <remarks>
	/// Parsing is an authoring and loading operation. It resolves
	/// strings and therefore allocates.
	/// </remarks>
	public static GameplayTagSet Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		string[] names = text.Split(
			',',
			StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		if (names.Length == 0)
		{
			return Empty;
		}

		GameplayTag[] tags = new GameplayTag[names.Length];

		for (int index = 0; index < names.Length; index++)
		{
			tags[index] = GameplayTagRegistry.Shared.GetTag(names[index]);
		}

		return CreateCore(tags);
	}

	/// <summary>
	/// Determines whether a tag is explicitly present or is an
	/// ancestor of an explicitly present tag.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <returns>
	/// <see langword="false"/> for the invalid tag.
	/// </returns>
	/// <remarks>
	/// A set containing <c>Status.Stunned</c> has the tag
	/// <c>Status</c>, because <c>Status</c> is cached as an ancestor.
	/// </remarks>
	public bool HasTag(GameplayTag tag)
	{
		return TagSetOperations.HasTag(_tags, _parents, tag);
	}

	/// <summary>
	/// Determines whether a tag is explicitly present.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <returns>
	/// <see langword="false"/> for the invalid tag.
	/// </returns>
	public bool HasTagExact(GameplayTag tag)
	{
		return tag.IsValid && TagSetOperations.Contains(_tags, tag);
	}

	/// <summary>
	/// Determines whether any of the other set's explicit tags is
	/// present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	/// <returns>
	/// <see langword="false"/> when <paramref name="other"/> is empty.
	/// </returns>
	public bool HasAny(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAny(_tags, _parents, other._tags);
	}

	/// <summary>
	/// Determines whether any of the other set's explicit tags is
	/// present exactly.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	/// <returns>
	/// <see langword="false"/> when <paramref name="other"/> is empty.
	/// </returns>
	public bool HasAnyExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAnyExact(_tags, other._tags);
	}

	/// <summary>
	/// Determines whether every one of the other set's explicit tags
	/// is present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="other"/> is empty,
	/// because no check fails.
	/// </returns>
	public bool HasAll(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAll(_tags, _parents, other._tags);
	}

	/// <summary>
	/// Determines whether every one of the other set's explicit tags
	/// is present exactly.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	/// <returns>
	/// <see langword="true"/> when <paramref name="other"/> is empty,
	/// because no check fails.
	/// </returns>
	public bool HasAllExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAllExact(_tags, other._tags);
	}

	/// <summary>
	/// Determines whether this set satisfies a tag query.
	/// </summary>
	/// <param name="query">The query to evaluate.</param>
	/// <returns>
	/// <see langword="false"/> when the query has no expression.
	/// </returns>
	public bool Matches(GameplayTagQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);

		return query.Matches(_tags, _parents);
	}

	/// <summary>
	/// Returns a new set containing the tags that match any tag of
	/// another set, expanding this set's ancestry.
	/// </summary>
	/// <param name="other">The filter.</param>
	/// <remarks>
	/// A tag is kept when the other set explicitly contains that tag
	/// or one of its ancestors. Only the tags of this set are kept;
	/// their ancestors are cached again, not made explicit.
	/// </remarks>
	public GameplayTagSet Filter(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return FilterCore(other, exact: false);
	}

	/// <summary>
	/// Returns a new set containing the tags that match exactly one
	/// tag of another set.
	/// </summary>
	/// <param name="other">The filter.</param>
	public GameplayTagSet FilterExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return FilterCore(other, exact: true);
	}

	/// <summary>
	/// Returns a set whose explicit tags are this set's tags plus all
	/// of their ancestors.
	/// </summary>
	/// <remarks>
	/// The result is a fixed point: requesting its parents again
	/// returns the same explicit tags.
	/// </remarks>
	public GameplayTagSet GetGameplayTagParents()
	{
		if (_tags.Length == 0 || _parents.Length == 0)
		{
			return this;
		}

		GameplayTag[] combined = new GameplayTag[_tags.Length + _parents.Length];

		int tagIndex = 0;
		int parentIndex = 0;
		int destination = 0;

		while (tagIndex < _tags.Length && parentIndex < _parents.Length)
		{
			uint tagId = _tags[tagIndex].Id;
			uint parentId = _parents[parentIndex].Id;

			if (tagId < parentId)
			{
				combined[destination++] = _tags[tagIndex++];
			}
			else if (parentId < tagId)
			{
				combined[destination++] = _parents[parentIndex++];
			}
			else
			{
				combined[destination++] = _tags[tagIndex++];
				parentIndex++;
			}
		}

		while (tagIndex < _tags.Length)
		{
			combined[destination++] = _tags[tagIndex++];
		}

		while (parentIndex < _parents.Length)
		{
			combined[destination++] = _parents[parentIndex++];
		}

		if (destination != combined.Length)
		{
			Array.Resize(ref combined, destination);
		}

		return new GameplayTagSet(combined, _parents);
	}

	/// <summary>
	/// Serializes the set as sorted comma-separated tag names.
	/// </summary>
	/// <remarks>
	/// Names are sorted ordinally so the text is identical between
	/// processes, unlike identifiers, which are assigned in
	/// registration order. Use <see cref="Parse"/> to reconstruct the
	/// set after the tags have been registered.
	/// </remarks>
	public string Serialize()
	{
		if (_tags.Length == 0)
		{
			return string.Empty;
		}

		string[] names = new string[_tags.Length];

		for (int index = 0; index < _tags.Length; index++)
		{
			names[index] = GameplayTagRegistry.Shared.GetName(_tags[index]);
		}

		Array.Sort(names, StringComparer.Ordinal);

		return string.Join(", ", names);
	}

	/// <summary>
	/// Determines whether two sets contain the same explicit tags.
	/// </summary>
	public bool SetEquals(GameplayTagSet? other)
	{
		return Equals(other);
	}

	/// <inheritdoc />
	public bool Equals(GameplayTagSet? other)
	{
		if (ReferenceEquals(this, other))
		{
			return true;
		}

		if (other is null || _hashCode != other._hashCode)
		{
			return false;
		}

		return _tags.AsSpan().SequenceEqual(other._tags);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayTagSet other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return _hashCode;
	}

	/// <summary>
	/// Reports whether two sets contain the same explicit tags.
	/// </summary>
	public static bool operator ==(GameplayTagSet? left, GameplayTagSet? right)
	{
		return Equals(left, right);
	}

	/// <summary>
	/// Reports whether two sets differ.
	/// </summary>
	public static bool operator !=(GameplayTagSet? left, GameplayTagSet? right)
	{
		return !Equals(left, right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		if (_tags.Length == 0)
		{
			return "GameplayTagSet { }";
		}

		return $"GameplayTagSet {{ {Serialize()} }}";
	}

	internal static GameplayTagSet CreateCore(ReadOnlySpan<GameplayTag> tags)
	{
		if (tags.Length == 0)
		{
			return Empty;
		}

		GameplayTag[] canonical = new GameplayTag[tags.Length];

		for (int index = 0; index < tags.Length; index++)
		{
			GameplayTag tag = tags[index];

			if (!tag.IsValid)
			{
				throw new ArgumentException(
					"A tag set cannot contain the invalid tag. Resolve " +
					"tags through the registry before adding them.",
					nameof(tags));
			}

			canonical[index] = tag;
		}

		Array.Sort(canonical);
		int unique = Deduplicate(canonical);

		if (unique != canonical.Length)
		{
			Array.Resize(ref canonical, unique);
		}

		return new GameplayTagSet(canonical, CollectParents(canonical));
	}

	/// <summary>
	/// Creates the cached set that lists a tag and its ancestors as
	/// explicit tags.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <param name="ancestors">Its ancestors, ordered nearest-first.</param>
	internal static GameplayTagSet CreateWithParents(
		GameplayTag tag,
		ReadOnlySpan<GameplayTag> ancestors)
	{
		if (ancestors.Length == 0)
		{
			return new GameplayTagSet([tag], []);
		}

		GameplayTag[] tags = new GameplayTag[ancestors.Length + 1];

		tags[0] = tag;
		ancestors.CopyTo(tags.AsSpan(1));

		Array.Sort(tags);

		GameplayTag[] parents = ancestors.ToArray();

		Array.Sort(parents);

		return new GameplayTagSet(tags, parents);
	}

	private static int Deduplicate(GameplayTag[] sorted)
	{
		if (sorted.Length < 2)
		{
			return sorted.Length;
		}

		int unique = 1;

		for (int index = 1; index < sorted.Length; index++)
		{
			if (sorted[index] != sorted[unique - 1])
			{
				sorted[unique++] = sorted[index];
			}
		}

		return unique;
	}

	private static GameplayTag[] CollectParents(ReadOnlySpan<GameplayTag> tags)
	{
		GameplayTagRegistry registry = GameplayTagRegistry.Shared;

		int capacity = 0;

		foreach (GameplayTag tag in tags)
		{
			capacity += registry.GetAncestors(tag).Length;
		}

		if (capacity == 0)
		{
			return [];
		}

		GameplayTag[] parents = new GameplayTag[capacity];
		int count = 0;

		foreach (GameplayTag tag in tags)
		{
			foreach (GameplayTag ancestor in registry.GetAncestors(tag))
			{
				parents[count++] = ancestor;
			}
		}

		Array.Sort(parents);

		int unique = Deduplicate(parents);

		if (unique != parents.Length)
		{
			Array.Resize(ref parents, unique);
		}

		return parents;
	}

	private GameplayTagSet FilterCore(GameplayTagSet other, bool exact)
	{
		GameplayTag[] kept = new GameplayTag[_tags.Length];
		int count = 0;

		foreach (GameplayTag tag in _tags)
		{
			bool matches = exact
				? TagSetOperations.Contains(other._tags, tag)
				: TagSetOperations.TagMatchesAny(tag, other._tags);

			if (matches)
			{
				kept[count++] = tag;
			}
		}

		if (count == 0)
		{
			return Empty;
		}

		if (count != kept.Length)
		{
			Array.Resize(ref kept, count);
		}

		return new GameplayTagSet(kept, CollectParents(kept));
	}

	private static int ComputeHashCode(ReadOnlySpan<GameplayTag> tags)
	{
		const uint offsetBasis = 2166136261;
		const uint prime = 16777619;

		uint hash = offsetBasis;

		foreach (GameplayTag tag in tags)
		{
			hash ^= tag.Id;
			hash *= prime;
		}

		hash ^= (uint)tags.Length;
		hash *= prime;

		return unchecked((int)hash);
	}
}
