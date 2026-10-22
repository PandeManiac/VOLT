
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanDeferredResourceReleasesTests
{
	[Fact]
	public void NewQueue_AllowsTransfers()
	{
		VulkanDeferredResourceReleases releases = new();
		releases.EnsureTransferAvailable();

		Assert.False(releases.HasUnresolvedTransfer);
		Assert.False(releases.IsLost);
		Assert.Equal(0, releases.PendingReleaseCount);
	}

	[Fact]
	public void HealthyQueue_ReleasesImmediately()
	{
		VulkanDeferredResourceReleases releases = new();
		int releaseCount = 0;

		releases.ReleaseOrDefer(() => releaseCount++);

		Assert.Equal(1, releaseCount);
		Assert.Equal(0, releases.PendingReleaseCount);

		releases.EnsureTransferAvailable();
	}

	[Fact]
	public void UnresolvedTransfer_DefersRelease()
	{
		VulkanDeferredResourceReleases releases = new();
		int releaseCount = 0;

		releases.MarkTransferUnresolved();
		releases.ReleaseOrDefer(() => releaseCount++);

		Assert.True(releases.HasUnresolvedTransfer);
		Assert.Equal(0, releaseCount);
		Assert.Equal(1, releases.PendingReleaseCount);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);
	}

	[Theory]
	[InlineData(Result.Timeout)]
	[InlineData(Result.NotReady)]
	[InlineData(Result.ErrorOutOfHostMemory)]
	[InlineData(Result.ErrorOutOfDeviceMemory)]
	[InlineData(Result.ErrorUnknown)]
	public void UnsuccessfulWait_PreservesDeferredReleases(
		Result result)
	{
		VulkanDeferredResourceReleases releases = new();
		int releaseCount = 0;

		releases.MarkTransferUnresolved();
		releases.ReleaseOrDefer(() => releaseCount++);

		Assert.Throws<InvalidOperationException>(() =>
			releases.Reclaim(result));

		Assert.True(releases.HasUnresolvedTransfer);
		Assert.Equal(0, releaseCount);
		Assert.Equal(1, releases.PendingReleaseCount);
	}

	[Fact]
	public void SuccessfulWait_ReleasesInInsertionOrder()
	{
		VulkanDeferredResourceReleases releases = new();
		List<int> releaseOrder = [];

		releases.MarkTransferUnresolved();
		releases.ReleaseOrDefer(() => releaseOrder.Add(1));
		releases.ReleaseOrDefer(() => releaseOrder.Add(2));
		releases.ReleaseOrDefer(() => releaseOrder.Add(3));
		releases.Reclaim(Result.Success);

		Assert.Equal(
			new[] { 1, 2, 3 },
			releaseOrder);

		Assert.Equal(0, releases.PendingReleaseCount);
		Assert.False(releases.HasUnresolvedTransfer);

		releases.EnsureTransferAvailable();
	}

	[Fact]
	public void DeviceLost_ReclaimsButPermanentlyBlocksTransfers()
	{
		VulkanDeferredResourceReleases releases = new();

		int releaseCount = 0;

		releases.MarkTransferUnresolved();
		releases.ReleaseOrDefer(() => releaseCount++);
		releases.Reclaim(Result.ErrorDeviceLost);

		Assert.True(releases.IsLost);
		Assert.False(releases.HasUnresolvedTransfer);
		Assert.Equal(1, releaseCount);
		Assert.Equal(0, releases.PendingReleaseCount);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);

		releases.Reclaim(Result.Success);

		Assert.True(releases.IsLost);
	}

	[Fact]
	public void MarkDeviceLost_BlocksTransfers()
	{
		VulkanDeferredResourceReleases releases = new();

		releases.MarkDeviceLost();
		Assert.True(releases.IsLost);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);
	}

	[Fact]
	public void FailedCallback_DoesNotDiscardOtherCallbacks()
	{
		VulkanDeferredResourceReleases releases = new();

		List<string> attempts = [];
		bool failFirstAttempt = true;

		releases.MarkTransferUnresolved();

		releases.ReleaseOrDefer(() =>
		{
			attempts.Add("first");

			if (failFirstAttempt)
			{
				failFirstAttempt = false;

				throw new InvalidOperationException(
					"Injected cleanup failure.");
			}
		});

		releases.ReleaseOrDefer(() =>
		{
			attempts.Add("second");
		});

		releases.ReleaseOrDefer(() =>
		{
			attempts.Add("third");
		});

		AggregateException exception =
			Assert.Throws<AggregateException>(() =>
				releases.Reclaim(Result.Success));

		Assert.Single(exception.InnerExceptions);

		Assert.Equal(
			new[] { "first", "second", "third" },
			attempts);

		Assert.False(releases.HasUnresolvedTransfer);
		Assert.Equal(1, releases.PendingReleaseCount);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);

		releases.Reclaim(Result.Success);

		Assert.Equal(
			new[] { "first", "second", "third", "first" },
			attempts);

		Assert.Equal(0, releases.PendingReleaseCount);

		releases.EnsureTransferAvailable();
	}

	[Fact]
	public void MultipleFailedCallbacks_AreAllRetained()
	{
		VulkanDeferredResourceReleases releases = new();

		int firstAttempts = 0;
		int secondAttempts = 0;

		releases.MarkTransferUnresolved();

		releases.ReleaseOrDefer(() =>
		{
			firstAttempts++;

			if (firstAttempts == 1)
			{
				throw new InvalidOperationException("First failure.");
			}
		});

		releases.ReleaseOrDefer(() =>
		{
			secondAttempts++;

			if (secondAttempts == 1)
			{
				throw new InvalidOperationException("Second failure.");
			}
		});

		AggregateException exception =
			Assert.Throws<AggregateException>(() =>
				releases.Reclaim(Result.Success));

		Assert.Equal(2, exception.InnerExceptions.Count);
		Assert.Equal(2, releases.PendingReleaseCount);

		releases.Reclaim(Result.Success);

		Assert.Equal(2, firstAttempts);
		Assert.Equal(2, secondAttempts);
		Assert.Equal(0, releases.PendingReleaseCount);
	}

	[Fact]
	public void ImmediateCleanupFailure_IsRetainedForRetry()
	{
		VulkanDeferredResourceReleases releases = new();

		int attempts = 0;

		Assert.Throws<InvalidOperationException>(() =>
			releases.ReleaseOrDefer(() =>
			{
				attempts++;

				if (attempts == 1)
				{
					throw new InvalidOperationException(
						"Injected immediate release failure.");
				}
			}));

		Assert.Equal(1, releases.PendingReleaseCount);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);

		releases.Reclaim(Result.Success);

		Assert.Equal(2, attempts);
		Assert.Equal(0, releases.PendingReleaseCount);

		releases.EnsureTransferAvailable();
	}

	[Fact]
	public void CallbackAddedDuringReclamation_IsRetained()
	{
		VulkanDeferredResourceReleases releases = new();
		List<string> releaseOrder = [];

		releases.MarkTransferUnresolved();

		releases.ReleaseOrDefer(() =>
		{
			releaseOrder.Add("outer");

			releases.ReleaseOrDefer(() =>
			{
				releaseOrder.Add("inner");
			});
		});

		releases.Reclaim(Result.Success);

		Assert.Equal(
			new[] { "outer" },
			releaseOrder);

		Assert.Equal(1, releases.PendingReleaseCount);

		Assert.Throws<InvalidOperationException>(
			releases.EnsureTransferAvailable);

		releases.Reclaim(Result.Success);

		Assert.Equal(
			new[] { "outer", "inner" },
			releaseOrder);

		Assert.Equal(0, releases.PendingReleaseCount);
	}

	[Fact]
	public void CompletionRestoresTransferAvailability()
	{
		VulkanDeferredResourceReleases releases = new();

		releases.MarkTransferUnresolved();
		releases.Reclaim(Result.Success);
		releases.EnsureTransferAvailable();

		Assert.False(releases.HasUnresolvedTransfer);
		Assert.False(releases.IsLost);
	}
}
