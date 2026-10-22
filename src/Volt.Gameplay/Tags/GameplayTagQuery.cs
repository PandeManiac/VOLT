using System.Text;

namespace Volt.Gameplay.Tags;

/// <summary>
/// Identifies the operation performed by one
/// <see cref="GameplayTagQuery"/> node.
/// </summary>
public enum GameplayTagQueryKind : byte
{
	/// <summary>At least one listed tag is present.</summary>
	AnyTagsMatch,

	/// <summary>Every listed tag is present.</summary>
	AllTagsMatch,

	/// <summary>No listed tag is present.</summary>
	NoTagsMatch,

	/// <summary>At least one listed tag is present exactly.</summary>
	AnyTagsExactMatch,

	/// <summary>Every listed tag is present exactly.</summary>
	AllTagsExactMatch,

	/// <summary>At least one child expression matches.</summary>
	AnyExpressionMatch,

	/// <summary>Every child expression matches.</summary>
	AllExpressionMatch,

	/// <summary>No child expression matches.</summary>
	NoExpressionMatch
}

/// <summary>
/// An immutable, nestable boolean expression over gameplay tags.
/// </summary>
/// <remarks>
/// <para>
/// Queries express arbitrary combinations of tag requirements, such
/// as <c>ALL(ANY(AllTags(A, B), AllTags(C)), NoTags(D))</c>, without
/// evaluating strings. This mirrors Unreal's
/// <c>FGameplayTagQuery</c> expression set, but the tree is built from
/// ordinary objects instead of a serialized token stream, so
/// construction is more direct and evaluation is an inlined switch.
/// </para>
/// <para>
/// Evaluation short-circuits and allocates nothing. All tags and child
/// expressions are resolved when the query is built, so a query can be
/// shared between abilities and effects and evaluated on every
/// activation.
/// </para>
/// <para>
/// Empty semantics follow Unreal's evaluator exactly: an <c>All</c>
/// over nothing is true, an <c>Any</c> over nothing is false, and a
/// <c>None</c> over nothing is true, because "no tag matched" is a
/// statement about absence. A query that was never given an expression
/// is a separate case: <see cref="Empty"/> matches nothing at all,
/// matching Unreal's empty <c>FGameplayTagQuery</c>.
/// </para>
/// </remarks>
public sealed class GameplayTagQuery
{
	/// <summary>
	/// The maximum nesting depth of child expressions.
	/// </summary>
	/// <remarks>
	/// Evaluation recurses, so an unbounded authored tree could
	/// exhaust the stack. Unreal has the same exposure through its
	/// token stream; VOLT rejects the tree when it is built, where the
	/// error is attributable to the author, instead of failing later
	/// during gameplay.
	/// </remarks>
	public const int MaximumNestingDepth = 32;

	private readonly GameplayTagSet? _tags;
	private readonly GameplayTagQuery[]? _children;
	private readonly int _depth;

	private GameplayTagQuery()
	{
		_isEmpty = true;
	}

	private GameplayTagQuery(
		GameplayTagQueryKind kind,
		GameplayTagSet tags,
		int depth)
	{
		Kind = kind;
		_tags = tags;
		_depth = depth;
	}

	private GameplayTagQuery(
		GameplayTagQueryKind kind,
		GameplayTagQuery[] children,
		int depth)
	{
		Kind = kind;
		_children = children;
		_depth = depth;
	}

	private readonly bool _isEmpty;

	/// <summary>
	/// Gets a query with no expression.
	/// </summary>
	/// <remarks>
	/// This query never matches, including against an empty tag set.
	/// It exists so a definition can represent "no requirement
	/// authored" without a nullable reference; callers that want
	/// "always satisfied" use <c>AllTags()</c> or <c>AllOf()</c>.
	/// </remarks>
	public static GameplayTagQuery Empty { get; } = new();

	/// <summary>
	/// Gets whether this query has no expression.
	/// </summary>
	public bool IsEmpty => _isEmpty;

	/// <summary>
	/// Gets the operation this query performs.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// This is <see cref="Empty"/>.
	/// </exception>
	public GameplayTagQueryKind Kind { get; }

	/// <summary>
	/// Gets the maximum nesting depth below this node.
	/// </summary>
	public int Depth => _depth;

	/// <summary>
	/// Creates a query that requires at least one of the tags.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AnyTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return new GameplayTagQuery(
			GameplayTagQueryKind.AnyTagsMatch,
			tags,
			depth: 1);
	}

	/// <summary>
	/// Creates a query that requires at least one of the tags.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AnyTags(params GameplayTag[] tags)
	{
		return AnyTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Creates a query that requires every specified tag.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AllTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return new GameplayTagQuery(
			GameplayTagQueryKind.AllTagsMatch,
			tags,
			depth: 1);
	}

	/// <summary>
	/// Creates a query that requires every specified tag.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AllTags(params GameplayTag[] tags)
	{
		return AllTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Creates a query that requires none of the tags.
	/// </summary>
	/// <param name="tags">The tags to reject.</param>
	public static GameplayTagQuery NoTags(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return new GameplayTagQuery(
			GameplayTagQueryKind.NoTagsMatch,
			tags,
			depth: 1);
	}

	/// <summary>
	/// Creates a query that requires none of the tags.
	/// </summary>
	/// <param name="tags">The tags to reject.</param>
	public static GameplayTagQuery NoTags(params GameplayTag[] tags)
	{
		return NoTags(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Creates a query that requires at least one tag to be explicit.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AnyTagsExact(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return new GameplayTagQuery(
			GameplayTagQueryKind.AnyTagsExactMatch,
			tags,
			depth: 1);
	}

	/// <summary>
	/// Creates a query that requires at least one tag to be explicit.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AnyTagsExact(params GameplayTag[] tags)
	{
		return AnyTagsExact(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Creates a query that requires every tag to be explicit.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AllTagsExact(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return new GameplayTagQuery(
			GameplayTagQueryKind.AllTagsExactMatch,
			tags,
			depth: 1);
	}

	/// <summary>
	/// Creates a query that requires every tag to be explicit.
	/// </summary>
	/// <param name="tags">The tags to test for.</param>
	public static GameplayTagQuery AllTagsExact(params GameplayTag[] tags)
	{
		return AllTagsExact(GameplayTagSet.Create(tags));
	}

	/// <summary>
	/// Creates a query that matches when any child matches.
	/// </summary>
	/// <param name="queries">The child expressions.</param>
	/// <exception cref="ArgumentException">
	/// A child is <see langword="null"/>, empty, or too deeply nested.
	/// </exception>
	public static GameplayTagQuery AnyOf(params GameplayTagQuery[] queries)
	{
		return Combine(
			GameplayTagQueryKind.AnyExpressionMatch,
			queries);
	}

	/// <summary>
	/// Creates a query that matches when every child matches.
	/// </summary>
	/// <param name="queries">The child expressions.</param>
	/// <exception cref="ArgumentException">
	/// A child is <see langword="null"/>, empty, or too deeply nested.
	/// </exception>
	public static GameplayTagQuery AllOf(params GameplayTagQuery[] queries)
	{
		return Combine(
			GameplayTagQueryKind.AllExpressionMatch,
			queries);
	}

	/// <summary>
	/// Creates a query that matches when no child matches.
	/// </summary>
	/// <param name="queries">The child expressions to negate.</param>
	/// <exception cref="ArgumentException">
	/// A child is <see langword="null"/>, empty, or too deeply nested.
	/// </exception>
	public static GameplayTagQuery NoneOf(params GameplayTagQuery[] queries)
	{
		return Combine(
			GameplayTagQueryKind.NoExpressionMatch,
			queries);
	}

	/// <summary>
	/// Evaluates the query against an immutable set.
	/// </summary>
	/// <param name="tags">The tags to test.</param>
	/// <returns>
	/// <see langword="false"/> when this is <see cref="Empty"/>.
	/// </returns>
	public bool Matches(GameplayTagSet tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return Matches(tags.Tags, tags.ParentTags);
	}

	/// <summary>
	/// Evaluates the query against a mutable container.
	/// </summary>
	/// <param name="tags">The tags to test.</param>
	/// <returns>
	/// <see langword="false"/> when this is <see cref="Empty"/>.
	/// </returns>
	public bool Matches(GameplayTagContainer tags)
	{
		ArgumentNullException.ThrowIfNull(tags);

		return tags.Matches(this);
	}

	/// <summary>
	/// Describes the expression tree for diagnostics.
	/// </summary>
	/// <remarks>
	/// The text allocates and is intended for logs, validation
	/// messages, and tooling.
	/// </remarks>
	public string Describe()
	{
		if (_isEmpty)
		{
			return "Empty()";
		}

		StringBuilder builder = new();

		Describe(builder);

		return builder.ToString();
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Describe();
	}

	internal bool Matches(
		ReadOnlySpan<GameplayTag> tags,
		ReadOnlySpan<GameplayTag> parents)
	{
		if (_isEmpty)
		{
			return false;
		}

		switch (Kind)
		{
			case GameplayTagQueryKind.AnyTagsMatch:
				return TagSetOperations.HasAny(tags, parents, _tags!.Tags);

			case GameplayTagQueryKind.AllTagsMatch:
				return TagSetOperations.HasAll(tags, parents, _tags!.Tags);

			case GameplayTagQueryKind.NoTagsMatch:
				return !TagSetOperations.HasAny(tags, parents, _tags!.Tags);

			case GameplayTagQueryKind.AnyTagsExactMatch:
				return TagSetOperations.HasAnyExact(tags, _tags!.Tags);

			case GameplayTagQueryKind.AllTagsExactMatch:
				return TagSetOperations.HasAllExact(tags, _tags!.Tags);

			case GameplayTagQueryKind.AnyExpressionMatch:
				foreach (GameplayTagQuery child in _children!)
				{
					if (child.Matches(tags, parents))
					{
						return true;
					}
				}

				return false;

			case GameplayTagQueryKind.AllExpressionMatch:
				foreach (GameplayTagQuery child in _children!)
				{
					if (!child.Matches(tags, parents))
					{
						return false;
					}
				}

				return true;

			case GameplayTagQueryKind.NoExpressionMatch:
				foreach (GameplayTagQuery child in _children!)
				{
					if (child.Matches(tags, parents))
					{
						return false;
					}
				}

				return true;

			default:
				throw new InvalidOperationException(
					$"Unknown query kind '{Kind}'.");
		}
	}

	private static GameplayTagQuery Combine(
		GameplayTagQueryKind kind,
		GameplayTagQuery[] queries)
	{
		ArgumentNullException.ThrowIfNull(queries);

		GameplayTagQuery[] children = new GameplayTagQuery[queries.Length];
		int depth = 1;

		for (int index = 0; index < queries.Length; index++)
		{
			GameplayTagQuery child = queries[index];

			if (child is null)
			{
				throw new ArgumentException(
					"A query expression cannot contain a null child.",
					nameof(queries));
			}

			if (child.IsEmpty)
			{
				throw new ArgumentException(
					"An empty query has no expression and cannot be " +
					"nested. Use AllTags() for a query that always " +
					"succeeds, or NoTags() to reject nothing.",
					nameof(queries));
			}

			children[index] = child;
			depth = Math.Max(depth, child._depth + 1);
		}

		if (depth > MaximumNestingDepth)
		{
			throw new ArgumentException(
				$"A tag query may not nest deeper than " +
				$"{MaximumNestingDepth} expressions. The tree would " +
				"recurse without a bound during evaluation.",
				nameof(queries));
		}

		return new GameplayTagQuery(kind, children, depth);
	}

	private void Describe(StringBuilder builder)
	{
		builder.Append(Kind);

		if (_tags is not null)
		{
			builder.Append('(');
			builder.Append(_tags.Serialize());
			builder.Append(')');

			return;
		}

		builder.Append('(');

		for (int index = 0; index < _children!.Length; index++)
		{
			if (index > 0)
			{
				builder.Append(", ");
			}

			_children[index].Describe(builder);
		}

		builder.Append(')');
	}
}
