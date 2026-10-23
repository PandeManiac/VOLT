using Xunit;

namespace Volt.Gameplay.Tests.Lifecycle.Stress;

/// <summary>
/// Executes the seeded cross-store state machine over a matrix of
/// seeds and destruction callback orders.
/// </summary>
public sealed class LifecycleStressTests
{
	public static IEnumerable<object[]> SeedMatrix()
	{
		for (int index = 1; index <= 128; index++)
		{
			ulong seed = (ulong)index * 0x9E3779B97F4A7C15UL
				^ 0xD1B54A32D192ED03UL;

			yield return [seed, true];
			yield return [seed, false];
		}
	}

	[Theory]
	[MemberData(nameof(SeedMatrix))]
	public void SeededRuns_PreserveCrossStoreInvariants(
		ulong seed,
		bool destroyCallbackBeforeStores)
	{
		StressOptions options = new()
		{
			Seed = seed,
			Operations = 500,
			DestroyCallbackBeforeStores = destroyCallbackBeforeStores
		};

		using LifecycleStressHarness harness = new(options);

		StressRunResult result = harness.Run();

		Assert.Equal(0, result.InjectedExceptions);
		Assert.NotEmpty(result.Trace);
	}

	[Fact]
	public void ReplayingTheSameSeed_ReproducesTheTraceAndFinalState()
	{
		StressOptions options = new()
		{
			Seed = 0xDEADBEEFCAFEBABEUL,
			Operations = 180,
			DestroyCallbackBeforeStores = true
		};

		string firstDigest;
		int firstCount;

		using (LifecycleStressHarness first = new(options))
		{
			StressRunResult result = first.Run();

			firstDigest = result.TraceDigest;
			firstCount = result.Trace.Count;
		}

		string secondDigest;
		int secondCount;

		using (LifecycleStressHarness second = new(options))
		{
			StressRunResult result = second.Run();

			secondDigest = result.TraceDigest;
			secondCount = result.Trace.Count;
		}

		Assert.Equal(firstDigest, secondDigest);
		Assert.Equal(firstCount, secondCount);
	}

	[Fact]
	public void AShorterOperationLimit_ReplaysTheExactPrefix()
	{
		StressOptions fullOptions = new()
		{
			Seed = 0x123456789ABCDEF0UL,
			Operations = 120,
			DestroyCallbackBeforeStores = false
		};

		StressTraceEntry[] full;

		using (LifecycleStressHarness harness = new(fullOptions))
		{
			full = harness.Run().Trace.ToArray();
		}

		StressOptions prefixOptions = fullOptions with
		{
			Operations = 45
		};

		StressTraceEntry[] prefix;

		using (LifecycleStressHarness harness = new(prefixOptions))
		{
			prefix = harness.Run().Trace.ToArray();
		}

		Assert.NotEmpty(prefix);
		Assert.True(prefix.Length <= full.Length);

		for (int index = 0; index < prefix.Length; index++)
		{
			Assert.Equal(full[index], prefix[index]);
		}

		StressResultSummary replay =
			LifecycleStressHarness.ReplayFailingPrefix(
				fullOptions,
				44);

		Assert.False(replay.Failed);
		Assert.Null(replay.Failure);
	}

	[Theory]
	[InlineData(StressNotificationBoundary.TagsChanged)]
	[InlineData(StressNotificationBoundary.AttributeChanged)]
	[InlineData(StressNotificationBoundary.EffectChanged)]
	[InlineData(StressNotificationBoundary.AbilityChanged)]
	public void NotificationExceptionProbe_PropagatesAndLeavesTheStoresConsistent(
		StressNotificationBoundary boundary)
	{
		StressOptions options = new()
		{
			Seed = 0xA5A5A5A5A5A5A5A5UL,
			Operations = 100,
			DestroyCallbackBeforeStores = true,
			ExceptionProbeOperation = 37,
			ExceptionBoundary = boundary
		};

		using LifecycleStressHarness harness = new(options);

		StressRunResult result = harness.Run();

		Assert.Equal(1, result.InjectedExceptions);
		Assert.NotEmpty(result.Trace);
	}
}
