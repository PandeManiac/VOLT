using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

public sealed class GameplayTagQueryTests
{
	private static readonly GameplayTagSet Empty = GameplayTagSet.Empty;

	private static readonly GameplayTagSet NonEmpty =
		GameplayTagSet.Create(TagSchema.Effect);

	[Fact]
	public void EmptyQuery_NeverMatches()
	{
		Assert.False(GameplayTagQuery.Empty.Matches(Empty));
		Assert.False(GameplayTagQuery.Empty.Matches(NonEmpty));
		Assert.True(GameplayTagQuery.Empty.IsEmpty);
	}

	[Fact]
	public void EmptyAllExpression_MatchesEverything()
	{
		GameplayTagQuery query = GameplayTagQuery.AllOf();

		Assert.True(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void EmptyAnyExpression_MatchesNothing()
	{
		GameplayTagQuery query = GameplayTagQuery.AnyOf();

		Assert.False(query.Matches(Empty));
		Assert.False(query.Matches(NonEmpty));
	}

	[Fact]
	public void EmptyNoneExpression_MatchesEverything()
	{
		GameplayTagQuery query = GameplayTagQuery.NoneOf();

		Assert.True(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void EmptyAllTags_MatchesEverything()
	{
		GameplayTagQuery query = GameplayTagQuery.AllTags();

		Assert.True(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void EmptyAnyTags_MatchesNothing()
	{
		GameplayTagQuery query = GameplayTagQuery.AnyTags();

		Assert.False(query.Matches(Empty));
		Assert.False(query.Matches(NonEmpty));
	}

	[Fact]
	public void EmptyNoTags_MatchesEverything()
	{
		GameplayTagQuery query = GameplayTagQuery.NoTags();

		Assert.True(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void AllTags_RequiresTheTag()
	{
		GameplayTagQuery query = GameplayTagQuery.AllTags(TagSchema.Effect);

		Assert.False(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void AnyTags_RequiresTheTag()
	{
		GameplayTagQuery query = GameplayTagQuery.AnyTags(TagSchema.Effect);

		Assert.False(query.Matches(Empty));
		Assert.True(query.Matches(NonEmpty));
	}

	[Fact]
	public void NoTags_RejectsTheTag()
	{
		GameplayTagQuery query = GameplayTagQuery.NoTags(TagSchema.Effect);

		Assert.True(query.Matches(Empty));
		Assert.False(query.Matches(NonEmpty));
	}

	[Fact]
	public void ExactQueries_DoNotFollowAncestry()
	{
		GameplayTagSet inherited = GameplayTagSet.Create(
			TagSchema.EffectDamageType1);

		Assert.True(
			GameplayTagQuery
				.AllTags(TagSchema.EffectDamage)
				.Matches(inherited));

		Assert.False(
			GameplayTagQuery
				.AllTagsExact(TagSchema.EffectDamage)
				.Matches(inherited));

		Assert.False(
			GameplayTagQuery
				.AnyTagsExact(TagSchema.EffectDamage)
				.Matches(inherited));
	}

	[Fact]
	public void NestedQuery_EvaluatesBooleanCombination()
	{
		GameplayTag a = TagSchema.StatusStunned;
		GameplayTag b = TagSchema.Effect;
		GameplayTag c = TagSchema.AbilityDash;
		GameplayTag d = TagSchema.StatusBurning;

		// ((A && B) || C) && !D
		GameplayTagQuery query = GameplayTagQuery.AllOf(
			GameplayTagQuery.AnyOf(
				GameplayTagQuery.AllTags(a, b),
				GameplayTagQuery.AllTags(c)),
			GameplayTagQuery.NoTags(d));

		Assert.True(query.Matches(GameplayTagSet.Create(a, b)));
		Assert.True(query.Matches(GameplayTagSet.Create(c)));
		Assert.False(query.Matches(GameplayTagSet.Create(a)));
		Assert.False(query.Matches(GameplayTagSet.Create(a, b, d)));
		Assert.False(query.Matches(Empty));
	}

	[Fact]
	public void Query_IsReusableAndStateless()
	{
		GameplayTagQuery query = GameplayTagQuery.AnyTags(
			TagSchema.StatusStunned,
			TagSchema.StatusBurning);

		for (int index = 0; index < 5; index++)
		{
			Assert.True(query.Matches(
				GameplayTagSet.Create(TagSchema.StatusStunnedDeep)));

			Assert.False(query.Matches(
				GameplayTagSet.Create(TagSchema.Effect)));
		}
	}

	[Fact]
	public void Query_MatchesContainer()
	{
		GameplayTagContainer container = new(TagSchema.StatusBurning);

		Assert.True(
			GameplayTagQuery
				.AnyTags(TagSchema.StatusBurning)
				.Matches(container));
	}

	[Fact]
	public void Combine_RejectsNullChild()
	{
		Assert.Throws<ArgumentNullException>(
			() => GameplayTagQuery.AllOf(null!));
	}

	[Fact]
	public void Combine_RejectsEmptyChild()
	{
		Assert.Throws<ArgumentException>(
			() => GameplayTagQuery.AnyOf(GameplayTagQuery.Empty));
	}

	[Fact]
	public void Combine_RejectsExcessiveNesting()
	{
		GameplayTagQuery query = GameplayTagQuery.AllTags(TagSchema.Effect);

		for (int depth = 1; depth < GameplayTagQuery.MaximumNestingDepth; depth++)
		{
			query = GameplayTagQuery.AllOf(query);
		}

		Assert.Equal(GameplayTagQuery.MaximumNestingDepth, query.Depth);

		Assert.Throws<ArgumentException>(
			() => GameplayTagQuery.AllOf(query));
	}

	[Fact]
	public void Describe_IncludesTagNames()
	{
		GameplayTagQuery query = GameplayTagQuery.AllOf(
			GameplayTagQuery.AnyTags(TagSchema.StatusStunned),
			GameplayTagQuery.NoTags(TagSchema.StatusBurning));

		string description = query.Describe();

		Assert.Contains("AllExpressionMatch", description);
		Assert.Contains("VoltGameplayTests.Status.Stunned", description);
		Assert.Contains("VoltGameplayTests.Status.Burning", description);
	}

	[Fact]
	public void Evaluate_DoesNotAllocate()
	{
		GameplayTagQuery query = GameplayTagQuery.AllOf(
			GameplayTagQuery.AnyOf(
				GameplayTagQuery.AllTags(
					TagSchema.StatusStunned,
					TagSchema.Effect),
				GameplayTagQuery.AllTags(TagSchema.AbilityDash)),
			GameplayTagQuery.NoTags(TagSchema.StatusBurning));

		GameplayTagSet tags = GameplayTagSet.Create(
			TagSchema.StatusStunned,
			TagSchema.Effect);

		for (int index = 0; index < 16; index++)
		{
			_ = query.Matches(tags);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			_ = query.Matches(tags);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}
}
