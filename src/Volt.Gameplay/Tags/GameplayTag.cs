namespace Volt.Gameplay.Tags;

/// <summary>
/// Identifies one registered gameplay tag.
/// </summary>
/// <remarks>
/// <para>
/// A tag is a four-byte interned identifier, not a string. Names are
/// resolved once by <see cref="GameplayTagRegistry"/>, and every
/// matching operation afterwards compares integers. This is the same
/// approach the ECS takes for component types, and it is what keeps
/// tag tests out of warm execution paths.
/// </para>
/// <para>
/// Tags are created only by the registry, so any non-default value is
/// a registered tag of this process. <see cref="Id"/> values are
/// assigned in registration order and are <em>not</em> stable between
/// process executions or between deployments, exactly like component
/// type identifiers. Persist <see cref="Name"/>, resolve it again on
/// load, and never store a numeric id in a file or on the wire.
/// </para>
/// <para>
/// The hierarchy is part of tag identity. <c>Status.Stunned</c> has
/// <c>Status</c> as an ancestor, so it matches a requirement for
/// <c>Status</c> under non-exact matching. Matching walks a
/// precomputed ancestor array, so it allocates nothing and does not
/// repeat string work.
/// </para>
/// <para>
/// The default value is <see cref="None"/>. Like Unreal's empty
/// <c>FGameplayTag</c>, an invalid tag matches nothing, not even
/// itself; this is deliberate, because an invalid tag represents a
/// missing tag rather than a tag that happens to have no parents.
/// </para>
/// </remarks>
public readonly struct GameplayTag : IEquatable<GameplayTag>, IComparable<GameplayTag>
{
	internal GameplayTag(uint id)
	{
		Id = id;
	}

	/// <summary>
	/// Gets a tag that refers to no registered tag.
	/// </summary>
	public static GameplayTag None => default;

	/// <summary>
	/// Gets the interned identifier of this tag.
	/// </summary>
	/// <remarks>
	/// Zero is reserved for the invalid tag.
	/// </remarks>
	public uint Id { get; }

	/// <summary>
	/// Gets whether this tag was issued by the registry.
	/// </summary>
	public bool IsValid => Id != 0;

	/// <summary>
	/// Gets the canonical, dot-separated name of this tag.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// This is the invalid tag.
	/// </exception>
	public string Name => GameplayTagRegistry.Shared.GetName(this);

	/// <summary>
	/// Gets the number of dot-separated segments in the tag name.
	/// </summary>
	/// <remarks>
	/// The invalid tag has depth zero; a root tag such as
	/// <c>Status</c> has depth one.
	/// </remarks>
	public int Depth => GameplayTagRegistry.Shared.GetDepth(this);

	/// <summary>
	/// Gets the immediate parent tag, or <see cref="None"/> for a
	/// root tag.
	/// </summary>
	public GameplayTag DirectParent =>
		GameplayTagRegistry.Shared.GetDirectParent(this);

	/// <summary>
	/// Gets this tag together with every ancestor, as an immutable
	/// set.
	/// </summary>
	/// <remarks>
	/// This is the allocation-free equivalent of Unreal's
	/// <c>GetGameplayTagParents</c>: the returned set is built from
	/// the registry's cached ancestry and can be shared.
	/// </remarks>
	public GameplayTagSet Parents =>
		GameplayTagRegistry.Shared.GetParents(this);

	/// <summary>
	/// Determines whether this tag is the given tag or one of its
	/// descendants.
	/// </summary>
	/// <param name="other">The tag or ancestor to test for.</param>
	/// <remarks>
	/// <para>
	/// <c>Status.Stunned.MatchesTag(Status)</c> is <c>true</c> and
	/// <c>Status.MatchesTag(Status.Stunned)</c> is <c>false</c>,
	/// matching Unreal's direction of the relationship.
	/// </para>
	/// <para>
	/// An invalid tag on either side matches nothing, including the
	/// case where both sides are invalid.
	/// </para>
	/// </remarks>
	public bool MatchesTag(GameplayTag other)
	{
		return GameplayTagRegistry.Shared.Matches(this, other);
	}

	/// <summary>
	/// Determines whether this tag is exactly the given tag.
	/// </summary>
	/// <param name="other">The tag to compare against.</param>
	/// <remarks>
	/// Invalid tags never match, including two invalid tags.
	/// </remarks>
	public bool MatchesTagExact(GameplayTag other)
	{
		return IsValid && other.IsValid && Id == other.Id;
	}

	/// <summary>
	/// Determines whether this tag or any of its ancestors is
	/// present in a set.
	/// </summary>
	/// <param name="tags">The set to search.</param>
	/// <remarks>
	/// The direction matters. <c>Type1.MatchesAny({Damage})</c> is
	/// true because <c>Damage</c> is an ancestor of <c>Type1</c>,
	/// while <c>Damage.MatchesAny({Type1})</c> is false. Only the
	/// set's explicit tags are searched, so an ancestor cached by the
	/// set does not by itself satisfy the match.
	/// </remarks>
	public bool MatchesAny(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);
		return TagSetOperations.TagMatchesAny(this, tags.Tags);
	}

	/// <summary>
	/// Determines whether this tag or any of its ancestors is
	/// present in a mutable container.
	/// </summary>
	/// <param name="tags">The container to search.</param>
	public bool MatchesAny(GameplayTagContainer tags)
	{
		ArgumentNullException.ThrowIfNull(tags);
		return TagSetOperations.TagMatchesAny(this, tags.Tags);
	}

	/// <summary>
	/// Determines whether this tag is explicitly present in a set.
	/// </summary>
	/// <param name="tags">The set to search.</param>
	/// <remarks>
	/// Ancestors cached by the set do not count; only an explicit
	/// occurrence of this exact tag matches.
	/// </remarks>
	public bool MatchesAnyExact(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);
		return IsValid && tags.HasTagExact(this);
	}

	/// <summary>
	/// Determines whether this tag is explicitly present in a
	/// container.
	/// </summary>
	/// <param name="tags">The container to search.</param>
	public bool MatchesAnyExact(GameplayTagContainer tags)
	{
		ArgumentNullException.ThrowIfNull(tags);
		return IsValid && tags.HasTagExact(this);
	}

	/// <summary>
	/// Determines whether this tag is a strict descendant of the
	/// given tag.
	/// </summary>
	/// <param name="ancestor">The ancestor to test for.</param>
	/// <remarks>
	/// A tag is not a descendant of itself; use
	/// <see cref="MatchesTag"/> when equality should also match.
	/// </remarks>
	public bool IsDescendantOf(GameplayTag ancestor)
	{
		return GameplayTagRegistry.Shared.IsDescendantOf(this, ancestor);
	}

	/// <inheritdoc />
	public bool Equals(GameplayTag other)
	{
		return Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayTag other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return (int)Id;
	}

	/// <inheritdoc />
	public int CompareTo(GameplayTag other)
	{
		return Id.CompareTo(other.Id);
	}

	/// <summary>
	/// Reports whether two tags identify the same registered tag.
	/// </summary>
	public static bool operator ==(GameplayTag left, GameplayTag right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two tags identify different tags.
	/// </summary>
	public static bool operator !=(GameplayTag left, GameplayTag right)
	{
		return !left.Equals(right);
	}

	/// <summary>
	/// Reports whether one tag sorts before another.
	/// </summary>
	public static bool operator <(GameplayTag left, GameplayTag right)
	{
		return left.CompareTo(right) < 0;
	}

	/// <summary>
	/// Reports whether one tag sorts after another.
	/// </summary>
	public static bool operator >(GameplayTag left, GameplayTag right)
	{
		return left.CompareTo(right) > 0;
	}

	/// <summary>
	/// Reports whether one tag sorts before or equal to another.
	/// </summary>
	public static bool operator <=(GameplayTag left, GameplayTag right)
	{
		return left.CompareTo(right) <= 0;
	}

	/// <summary>
	/// Reports whether one tag sorts after or equal to another.
	/// </summary>
	public static bool operator >=(GameplayTag left, GameplayTag right)
	{
		return left.CompareTo(right) >= 0;
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsValid
			? GameplayTagRegistry.Shared.GetName(this)
			: "GameplayTag.None";
	}
}
