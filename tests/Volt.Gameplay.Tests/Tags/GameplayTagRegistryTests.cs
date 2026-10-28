using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

public sealed class GameplayTagRegistryTests
{
	private static GameplayTagRegistry Registry =>
		GameplayTagRegistry.Shared;

	[Fact]
	public void Register_ReturnsValidTagWithCanonicalName()
	{
		GameplayTag tag = TagSchema.StatusStunned;

		Assert.True(tag.IsValid);
		Assert.Equal(
			"VoltGameplayTests.Status.Stunned",
			tag.Name);
	}

	[Fact]
	public void Register_IsIdempotent()
	{
		GameplayTag first = Registry.Register("VoltGameplayTests.Idempotent.Tag");
		GameplayTag second = Registry.Register("VoltGameplayTests.Idempotent.Tag");

		Assert.Equal(first, second);
		Assert.Equal(first.Id, second.Id);
	}

	[Fact]
	public void Register_CreatesImplicitAncestors()
	{
		GameplayTag leaf = Registry.Register(
			"VoltGameplayTests.Implicit.Parent.Leaf");

		Assert.True(Registry.TryGetTag(
			"VoltGameplayTests.Implicit",
			out GameplayTag root));

		Assert.True(Registry.TryGetTag(
			"VoltGameplayTests.Implicit.Parent",
			out GameplayTag parent));

		Assert.True(root.IsValid);
		Assert.True(parent.IsValid);
		Assert.Equal(parent, leaf.DirectParent);
		Assert.Equal(root, parent.DirectParent);
	}

	[Fact]
	public void Register_IsCaseInsensitive()
	{
		GameplayTag canonical = Registry.Register(
			"VoltGameplayTests.CaseSensitive.Tag");

		GameplayTag lower = Registry.Register(
			"voltgameplaytests.casesensitive.tag");

		Assert.Equal(canonical, lower);
		Assert.Equal(canonical.Id, lower.Id);
		Assert.Equal(
			"VoltGameplayTests.CaseSensitive.Tag",
			lower.Name);
	}

	[Fact]
	public void TryGetTag_UnknownName_DoesNotRegister()
	{
		GameplayTagRegistry registry = Registry;

		bool found = registry.TryGetTag(
			"VoltGameplayTests.NotRegistered.Anything",
			out GameplayTag tag);

		Assert.False(found);
		Assert.Equal(GameplayTag.None, tag);

		Assert.False(registry.TryGetTag(
			"VoltGameplayTests.NotRegistered.Anything",
			out _));
	}

	[Fact]
	public void GetTag_UnknownName_Throws()
	{
		ArgumentException exception = Assert.Throws<ArgumentException>(
			() => Registry.GetTag("VoltGameplayTests.Missing.Tag"));

		Assert.Contains("not registered", exception.Message);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("A.")]
	[InlineData(".A")]
	[InlineData("A..B")]
	[InlineData("A B")]
	[InlineData(" A")]
	[InlineData("A ")]
	[InlineData("A\tB")]
	[InlineData("A,B")]
	[InlineData("A\"B")]
	[InlineData("A'B")]
	[InlineData("A\nB")]
	public void Register_RejectsInvalidNames(string? name)
	{
		Assert.False(
			GameplayTagRegistry.IsValidGameplayTagString(name, out string? error));

		Assert.False(string.IsNullOrEmpty(error));

		if (name is null)
		{
			Assert.Throws<ArgumentNullException>(() => Registry.Register(name!));
		}
		else
		{
			Assert.Throws<ArgumentException>(() => Registry.Register(name));
		}
	}

	[Theory]
	[InlineData("A")]
	[InlineData("A.B")]
	[InlineData("Status.Stunned.Deep")]
	[InlineData("Unit_1.Type-2")]
	[InlineData("GameplayCue.Burning")]
	[InlineData("État.Stun")]
	public void IsValidGameplayTagString_AcceptsWellFormedNames(string name)
	{
		Assert.True(
			GameplayTagRegistry.IsValidGameplayTagString(name, out string? error));

		Assert.Null(error);
	}

	[Fact]
	public void DirectParent_OfRootTag_IsNone()
	{
		GameplayTag root = Registry.Register("VoltGameplayTestsRoot");

		Assert.Equal(GameplayTag.None, root.DirectParent);
	}

	[Fact]
	public void Depth_CountsSegments()
	{
		Assert.Equal(0, GameplayTag.None.Depth);
		Assert.Equal(2, TagSchema.Effect.Depth);
		Assert.Equal(4, TagSchema.EffectDamageType1.Depth);
	}

	[Fact]
	public void GetAncestors_ReturnsNearestFirst()
	{
		ReadOnlySpan<GameplayTag> ancestors =
			Registry.GetAncestors(TagSchema.EffectDamageType1);

		Assert.True(ancestors.Length >= 3);

		Assert.Equal(TagSchema.EffectDamage, ancestors[0]);
		Assert.Equal(TagSchema.Effect, ancestors[1]);
	}

	[Fact]
	public void MatchesTag_IsDirectional()
	{
		Assert.True(
			TagSchema.EffectDamageType1.MatchesTag(TagSchema.EffectDamage));

		Assert.True(
			TagSchema.EffectDamageType1.MatchesTag(TagSchema.Effect));

		Assert.False(
			TagSchema.EffectDamage.MatchesTag(TagSchema.EffectDamageType1));

		Assert.True(
			TagSchema.EffectDamageType1.MatchesTag(TagSchema.EffectDamageType1));
	}

	[Fact]
	public void MatchesTag_InvalidTagNeverMatches()
	{
		Assert.False(GameplayTag.None.MatchesTag(GameplayTag.None));
		Assert.False(GameplayTag.None.MatchesTag(TagSchema.Effect));
		Assert.False(TagSchema.Effect.MatchesTag(GameplayTag.None));
	}

	[Fact]
	public void MatchesTagExact_RequiresSameTag()
	{
		Assert.True(
			TagSchema.EffectDamageType1.MatchesTagExact(
				TagSchema.EffectDamageType1));

		Assert.False(
			TagSchema.EffectDamageType1.MatchesTagExact(TagSchema.EffectDamage));

		Assert.False(GameplayTag.None.MatchesTagExact(GameplayTag.None));
	}

	[Fact]
	public void IsDescendantOf_ExcludesIdentity()
	{
		Assert.True(
			TagSchema.EffectDamageType1.IsDescendantOf(TagSchema.Effect));

		Assert.False(
			TagSchema.Effect.IsDescendantOf(TagSchema.Effect));

		Assert.False(
			TagSchema.Effect.IsDescendantOf(TagSchema.EffectDamageType1));
	}

	[Fact]
	public void GetParents_ContainsTagAndAncestors()
	{
		GameplayTagSet parents = TagSchema.EffectDamageType1.Parents;

		Assert.True(parents.HasTagExact(TagSchema.EffectDamageType1));
		Assert.True(parents.HasTagExact(TagSchema.EffectDamage));
		Assert.True(parents.HasTagExact(TagSchema.Effect));
		Assert.Equal(parents, parents.GetGameplayTagParents());
	}

	[Fact]
	public void GetName_InvalidTag_Throws()
	{
		Assert.Throws<InvalidOperationException>(
			() => _ = GameplayTag.None.Name);
	}

	[Fact]
	public void EnumerateTags_YieldsResolvableTags()
	{
		// The shared registry does not seed itself: touching a schema
		// tag runs the static initializer that registers the built-in
		// tags. Without this the test only passes when another test
		// happened to initialize the schema first, which made it fail
		// when run on its own.
		_ = TagSchema.StatusStunned;

		int seen = 0;

		foreach (GameplayTag tag in Registry.EnumerateTags())
		{
			Assert.True(tag.IsValid);
			Assert.False(string.IsNullOrEmpty(Registry.GetName(tag)));

			seen++;
		}

		Assert.True(seen > 0);
		Assert.Equal(Registry.Count, seen);
	}

	[Fact]
	public void Register_ConcurrentSameName_ReturnsOneTag()
	{
		const string name = "VoltGameplayTests.Concurrent.Shared";
		const int workerCount = 32;

		GameplayTag[] results = new GameplayTag[workerCount];

		Parallel.For(
			0,
			workerCount,
			index => results[index] = Registry.Register(name));

		GameplayTag expected = results[0];

		foreach (GameplayTag result in results)
		{
			Assert.Equal(expected, result);
		}
	}

	[Fact]
	public void Register_ConcurrentDistinctNames_AssignsDistinctTags()
	{
		const int tagCount = 64;

		GameplayTag[] results = new GameplayTag[tagCount];

		Parallel.For(
			0,
			tagCount,
			index => results[index] = Registry.Register(
				$"VoltGameplayTests.Concurrent.Distinct.Tag{index}"));

		Assert.Equal(tagCount, results.Distinct().Count());

		foreach (GameplayTag tag in results)
		{
			Assert.True(tag.IsValid);
			Assert.StartsWith(
				"VoltGameplayTests.Concurrent.Distinct.",
				tag.Name,
				StringComparison.Ordinal);
		}
	}
}
