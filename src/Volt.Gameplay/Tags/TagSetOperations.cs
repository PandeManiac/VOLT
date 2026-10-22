namespace Volt.Gameplay.Tags;

/// <summary>
/// Implements the matching rules shared by immutable tag sets, mutable
/// containers, and query evaluation.
/// </summary>
/// <remarks>
/// <para>
/// Both explicit tags and cached ancestors are stored in ascending
/// <see cref="GameplayTag.Id"/> order, so every membership test is a
/// binary search and no method here allocates.
/// </para>
/// <para>
/// The rules mirror Unreal's <c>FGameplayTagContainer</c>:
/// non-exact matching treats a tag's ancestors as present, exact
/// matching does not, <c>HasAny</c> on an empty argument is false,
/// and <c>HasAll</c> on an empty argument is true.
/// </para>
/// </remarks>
internal static class TagSetOperations
{
	/// <summary>
	/// Finds a tag in a sorted array.
	/// </summary>
	/// <returns>
	/// The index of the tag, or the bitwise complement of the position
	/// where it would be inserted.
	/// </returns>
	public static int IndexOf(ReadOnlySpan<GameplayTag> sorted, GameplayTag tag)
	{
		int low = 0;
		int high = sorted.Length - 1;

		while (low <= high)
		{
			int middle = low + ((high - low) >> 1);
			uint middleId = sorted[middle].Id;
			uint id = tag.Id;

			if (middleId == id)
			{
				return middle;
			}

			if (middleId < id)
			{
				low = middle + 1;
			}
			else
			{
				high = middle - 1;
			}
		}

		return ~low;
	}

	/// <summary>
	/// Determines whether a sorted array contains a tag.
	/// </summary>
	public static bool Contains(ReadOnlySpan<GameplayTag> sorted, GameplayTag tag)
	{
		return IndexOf(sorted, tag) >= 0;
	}

	/// <summary>
	/// Determines whether a tag is explicitly present or is an
	/// ancestor of an explicitly present tag.
	/// </summary>
	public static bool HasTag(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> parents,
		GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			return false;
		}

		return Contains(tags, tag) || Contains(parents, tag);
	}

	/// <summary>
	/// Determines whether any of the other container's explicit tags
	/// is present, directly or through a cached ancestor.
	/// </summary>
	/// <remarks>
	/// Only the other side's explicit tags are probed, exactly as
	/// Unreal's <c>HasAny</c> does; a parent cached only on the other
	/// side is not a candidate.
	/// </remarks>
	public static bool HasAny(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> parents,
		ReadOnlySpan<GameplayTag> otherTags)
	{
		foreach (GameplayTag tag in otherTags)
		{
			if (HasTag(tags, parents, tag))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether every one of the other container's explicit
	/// tags is present, directly or through a cached ancestor.
	/// </summary>
	/// <remarks>
	/// The empty requirement is satisfied, matching Unreal: there are
	/// no failed checks.
	/// </remarks>
	public static bool HasAll(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> parents,
		ReadOnlySpan<GameplayTag> otherTags)
	{
		foreach (GameplayTag tag in otherTags)
		{
			if (!HasTag(tags, parents, tag))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Determines whether any of the other container's explicit tags
	/// is present exactly.
	/// </summary>
	public static bool HasAnyExact(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> otherTags)
	{
		foreach (GameplayTag tag in otherTags)
		{
			if (Contains(tags, tag))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Determines whether every one of the other container's explicit
	/// tags is present exactly.
	/// </summary>
	public static bool HasAllExact(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> otherTags)
	{
		foreach (GameplayTag tag in otherTags)
		{
			if (!Contains(tags, tag))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Determines whether one tag matches any explicit tag of a set,
	/// ignoring the set's cached ancestors.
	/// </summary>
	/// <remarks>
	/// This answers <c>tag.MatchesAny(other)</c>: the tag itself or
	/// one of its ancestors must be in the other set. It is not the
	/// same question as <c>other.HasTag(tag)</c>, which follows the
	/// ancestry of the other set instead.
	/// </remarks>
	public static bool TagMatchesAny(
		GameplayTag tag,
		ReadOnlySpan<GameplayTag> otherTags)
	{
		if (!tag.IsValid)
		{
			return false;
		}

		if (Contains(otherTags, tag))
		{
			return true;
		}

		ReadOnlySpan<GameplayTag> ancestors =
			GameplayTagRegistry.Shared.GetAncestors(tag);

		foreach (GameplayTag ancestor in ancestors)
		{
			if (Contains(otherTags, ancestor))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Inserts a tag into the sorted, counted prefix of an array
	/// without allocating.
	/// </summary>
	/// <returns>
	/// The new count. The array must have room for one more element.
	/// </returns>
	public static int InsertSorted(
		GameplayTag[] array,
		int count,
		GameplayTag tag)
	{
		int index = IndexOf(array.AsSpan(0, count), tag);

		if (index >= 0)
		{
			return count;
		}

		int insertion = ~index;

		Array.Copy(array, insertion, array, insertion + 1, count - insertion);

		array[insertion] = tag;

		return count + 1;
	}

	/// <summary>
	/// Removes a tag from the sorted, counted prefix of an array
	/// without allocating.
	/// </summary>
	/// <returns>
	/// The new count.
	/// </returns>
	public static int RemoveSorted(
		GameplayTag[] array,
		int count,
		GameplayTag tag)
	{
		int index = IndexOf(array.AsSpan(0, count), tag);

		if (index < 0)
		{
			return count;
		}

		int last = count - 1;

		Array.Copy(array, index + 1, array, index, last - index);
		array[last] = GameplayTag.None;

		return last;
	}
}
