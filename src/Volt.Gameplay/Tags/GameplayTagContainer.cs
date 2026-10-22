namespace Volt.Gameplay.Tags;

/// <summary>
/// A mutable set of gameplay tags with incrementally maintained
/// ancestors.
/// </summary>
/// <remarks>
/// <para>
/// This is the authoring and inspection form of a tag collection, and
/// it follows Unreal's <c>FGameplayTagContainer</c> semantics: adding
/// a tag also satisfies its ancestors under non-exact matching,
/// duplicate adds are ignored, removing a tag recomputes ancestors
/// from the remaining tags, and equality ignores order.
/// </para>
/// <para>
/// Storage grows geometrically and mutations reuse existing arrays, so
/// a warmed-up container with enough capacity adds, removes, and
/// queries without allocating. Recomputing ancestors after a removal
/// is O(tags × depth), which is the cost Unreal also pays to avoid
/// reference-counting errors when two tags share an ancestor.
/// </para>
/// <para>
/// Shared definitions should use the immutable
/// <see cref="GameplayTagSet"/> produced by <see cref="ToSet"/>
/// instead, so a runtime system cannot alter a definition it was
/// handed. This type is not thread-safe.
/// </para>
/// </remarks>
public sealed class GameplayTagContainer : IEquatable<GameplayTagContainer>
{
	private const int InitialCapacity = 8;

	private GameplayTag[] _tags = new GameplayTag[InitialCapacity];
	private GameplayTag[] _parents = new GameplayTag[InitialCapacity];

	private int _tagCount;
	private int _parentCount;

	/// <summary>
	/// Gets the number of explicitly added tags.
	/// </summary>
	public int Count => _tagCount;

	/// <summary>
	/// Gets whether no tag is explicitly present.
	/// </summary>
	public bool IsEmpty => _tagCount == 0;

	/// <summary>
	/// Gets the explicit tags in ascending identifier order.
	/// </summary>
	public ReadOnlySpan<GameplayTag> Tags => _tags.AsSpan(0, _tagCount);

	/// <summary>
	/// Gets the explicit tag at a position.
	/// </summary>
	/// <param name="index">The position in ascending identifier order.</param>
	public GameplayTag this[int index] => _tags[index];

	/// <summary>
	/// Creates an empty container.
	/// </summary>
	public GameplayTagContainer()
	{
	}

	/// <summary>
	/// Creates a container from tag values.
	/// </summary>
	/// <param name="tags">The tags to add. Duplicates are ignored.</param>
	/// <exception cref="ArgumentException">
	/// A tag is invalid.
	/// </exception>
	public GameplayTagContainer(params GameplayTag[] tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		AddRange(tags);
	}

	/// <summary>
	/// Parses a comma-separated list of registered tag names.
	/// </summary>
	/// <param name="text">Tag names separated by commas.</param>
	/// <exception cref="ArgumentException">
	/// A name is not registered or not a valid tag string.
	/// </exception>
	public static GameplayTagContainer Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		GameplayTagContainer container = new();

		string[] names = text.Split(
			',',
			StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		foreach (string name in names)
		{
			container.Add(GameplayTagRegistry.Shared.GetTag(name));
		}

		return container;
	}

	/// <summary>
	/// Adds a tag and all of its ancestors.
	/// </summary>
	/// <param name="tag">The tag to add.</param>
	/// <returns>
	/// <see langword="true"/> when the tag was not already explicit;
	/// <see langword="false"/> for a duplicate or the invalid tag.
	/// </returns>
	/// <remarks>
	/// The invalid tag is ignored rather than rejected, matching
	/// Unreal's <c>AddTag</c>. A caller that may hold an unresolved tag
	/// can therefore add it without branching, and the container stays
	/// well formed.
	/// </remarks>
	public bool Add(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			return false;
		}

		int index = TagSetOperations.IndexOf(Tags, tag);

		if (index >= 0)
		{
			return false;
		}

		int insertion = ~index;

		EnsureTagCapacity(_tagCount + 1);

		Array.Copy(
			_tags,
			insertion,
			_tags,
			insertion + 1,
			_tagCount - insertion);

		_tags[insertion] = tag;
		_tagCount++;

		foreach (GameplayTag ancestor in GameplayTagRegistry.Shared.GetAncestors(tag))
		{
			InsertParent(ancestor);
		}

		return true;
	}

	/// <summary>
	/// Adds several tags.
	/// </summary>
	/// <param name="tags">The tags to add. Duplicates are ignored.</param>
	public void AddRange(ReadOnlySpan<GameplayTag> tags)
	{
		foreach (GameplayTag tag in tags)
		{
			Add(tag);
		}
	}

	/// <summary>
	/// Adds every explicit tag of a set.
	/// </summary>
	/// <param name="tags">The set to copy.</param>
	public void AddRange(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		AddRange(tags.Tags);
	}

	/// <summary>
	/// Adds every explicit tag of another container.
	/// </summary>
	/// <param name="tags">The container whose tags are added.</param>
	public void Append(GameplayTagContainer tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		AddRange(tags.Tags);
	}

	/// <summary>
	/// Removes a tag and recomputes ancestors.
	/// </summary>
	/// <param name="tag">The tag to remove.</param>
	/// <returns>
	/// <see langword="true"/> when the tag was explicit and is now
	/// gone.
	/// </returns>
	/// <remarks>
	/// Ancestors are recomputed from scratch because another remaining
	/// tag may share the removed tag's parent. Unreal does the same in
	/// <c>FGameplayTagContainer::RemoveTag</c>.
	/// </remarks>
	public bool Remove(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			return false;
		}

		int previousCount = _tagCount;

		_tagCount = TagSetOperations.RemoveSorted(_tags, _tagCount, tag);

		if (_tagCount == previousCount)
		{
			return false;
		}

		RebuildParents();

		return true;
	}

	/// <summary>
	/// Removes every explicit tag of a set.
	/// </summary>
	/// <param name="tags">The tags to remove.</param>
	/// <returns>The number of tags that were explicit and are now gone.</returns>
	public int RemoveRange(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		int removed = 0;

		foreach (GameplayTag tag in tags.Tags)
		{
			int previousCount = _tagCount;

			_tagCount = TagSetOperations.RemoveSorted(_tags, _tagCount, tag);

			if (_tagCount != previousCount)
			{
				removed++;
			}
		}

		if (removed > 0)
		{
			RebuildParents();
		}

		return removed;
	}

	/// <summary>
	/// Removes every explicit tag.
	/// </summary>
	public void Clear()
	{
		Array.Clear(_tags, 0, _tagCount);
		Array.Clear(_parents, 0, _parentCount);

		_tagCount = 0;
		_parentCount = 0;
	}

	/// <summary>
	/// Determines whether a tag is explicitly present or is an
	/// ancestor of an explicitly present tag.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	public bool HasTag(GameplayTag tag)
	{
		return TagSetOperations.HasTag(Tags, Parents, tag);
	}

	/// <summary>
	/// Determines whether a tag is explicitly present.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	public bool HasTagExact(GameplayTag tag)
	{
		return tag.IsValid && TagSetOperations.Contains(Tags, tag);
	}

	/// <summary>
	/// Determines whether any of the other set's explicit tags is
	/// present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAny(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAny(Tags, Parents, other.Tags);
	}

	/// <summary>
	/// Determines whether any of the other container's explicit tags
	/// is present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAny(GameplayTagContainer other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAny(Tags, Parents, other.Tags);
	}

	/// <summary>
	/// Determines whether any of the other set's explicit tags is
	/// present exactly.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAnyExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAnyExact(Tags, other.Tags);
	}

	/// <summary>
	/// Determines whether every one of the other set's explicit tags
	/// is present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAll(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAll(Tags, Parents, other.Tags);
	}

	/// <summary>
	/// Determines whether every one of the other container's explicit
	/// tags is present, directly or as an inherited parent.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAll(GameplayTagContainer other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAll(Tags, Parents, other.Tags);
	}

	/// <summary>
	/// Determines whether every one of the other set's explicit tags
	/// is present exactly.
	/// </summary>
	/// <param name="other">The tags to test for.</param>
	public bool HasAllExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return TagSetOperations.HasAllExact(Tags, other.Tags);
	}

	/// <summary>
	/// Determines whether this container satisfies a tag query.
	/// </summary>
	/// <param name="query">The query to evaluate.</param>
	public bool Matches(GameplayTagQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);

		return query.Matches(Tags, Parents);
	}

	/// <summary>
	/// Returns the tags that match any tag of another set, expanding
	/// this container's ancestry.
	/// </summary>
	/// <param name="other">The filter.</param>
	public GameplayTagSet Filter(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return ToSet().Filter(other);
	}

	/// <summary>
	/// Returns the tags that match any tag of another container,
	/// expanding this container's ancestry.
	/// </summary>
	/// <param name="other">The filter.</param>
	public GameplayTagSet Filter(GameplayTagContainer other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return ToSet().Filter(other.ToSet());
	}

	/// <summary>
	/// Returns the tags that match exactly one tag of another set.
	/// </summary>
	/// <param name="other">The filter.</param>
	public GameplayTagSet FilterExact(GameplayTagSet other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return ToSet().FilterExact(other);
	}

	/// <summary>
	/// Returns a container whose explicit tags are this container's
	/// tags plus all of their ancestors.
	/// </summary>
	public GameplayTagContainer GetGameplayTagParents()
	{
		GameplayTagContainer result = new();

		result.Append(this);

		foreach (GameplayTag parent in Parents)
		{
			result.Add(parent);
		}

		return result;
	}

	/// <summary>
	/// Captures an immutable copy of the explicit tags.
	/// </summary>
	/// <remarks>
	/// The copy is independent of this container: later mutations do
	/// not affect it. Do this when handing tags to a shared
	/// definition.
	/// </remarks>
	public GameplayTagSet ToSet()
	{
		return _tagCount == 0
			? GameplayTagSet.Empty
			: GameplayTagSet.CreateCore(Tags);
	}

	/// <summary>
	/// Serializes the explicit tags as sorted comma-separated names.
	/// </summary>
	public string Serialize()
	{
		return ToSet().Serialize();
	}

	/// <summary>
	/// Determines whether two containers have the same explicit tags.
	/// </summary>
	public bool SetEquals(GameplayTagContainer? other)
	{
		return Equals(other);
	}

	/// <inheritdoc />
	public bool Equals(GameplayTagContainer? other)
	{
		if (ReferenceEquals(this, other))
		{
			return true;
		}

		return other is not null && Tags.SequenceEqual(other.Tags);
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayTagContainer other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		int hash = 17;

		foreach (GameplayTag tag in Tags)
		{
			hash = unchecked((hash * 31) + (int)tag.Id);
		}

		return hash;
	}

	/// <summary>
	/// Reports whether two containers have the same explicit tags.
	/// </summary>
	public static bool operator ==(
		GameplayTagContainer? left,
		GameplayTagContainer? right)
	{
		return Equals(left, right);
	}

	/// <summary>
	/// Reports whether two containers differ.
	/// </summary>
	public static bool operator !=(
		GameplayTagContainer? left,
		GameplayTagContainer? right)
	{
		return !Equals(left, right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return _tagCount == 0
			? "GameplayTagContainer { }"
			: $"GameplayTagContainer {{ {Serialize()} }}";
	}

	private ReadOnlySpan<GameplayTag> Parents => _parents.AsSpan(0, _parentCount);

	private void InsertParent(GameplayTag tag)
	{
		int index = TagSetOperations.IndexOf(Parents, tag);

		if (index >= 0)
		{
			return;
		}

		int insertion = ~index;

		EnsureParentCapacity(_parentCount + 1);

		Array.Copy(
			_parents,
			insertion,
			_parents,
			insertion + 1,
			_parentCount - insertion);

		_parents[insertion] = tag;
		_parentCount++;
	}

	private void RebuildParents()
	{
		Array.Clear(_parents, 0, _parentCount);

		_parentCount = 0;

		foreach (GameplayTag tag in Tags)
		{
			foreach (GameplayTag ancestor in GameplayTagRegistry.Shared.GetAncestors(tag))
			{
				InsertParent(ancestor);
			}
		}
	}

	private void EnsureTagCapacity(int required)
	{
		if (required <= _tags.Length)
		{
			return;
		}

		Array.Resize(ref _tags, Math.Max(required, _tags.Length * 2));
	}

	private void EnsureParentCapacity(int required)
	{
		if (required <= _parents.Length)
		{
			return;
		}

		Array.Resize(ref _parents, Math.Max(required, _parents.Length * 2));
	}
}
