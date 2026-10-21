using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

public sealed class GameLoopSettingsTests
{
	[Fact]
	public void Default_IsSixtyHertzWithBoundedCatchUp()
	{
		GameLoopSettings settings = GameLoopSettings.Default;

		Assert.Equal(60, settings.FixedUpdatesPerSecond);
		Assert.Equal(5, settings.MaxFixedStepsPerFrame);
		Assert.Equal(
			TimeSpan.FromMilliseconds(250),
			settings.MaximumFrameDelta);
	}

	[Theory]
	[InlineData(1, 1, 1)]
	[InlineData(60, 5, 250)]
	[InlineData(1000, 100, 10_000)]
	public void SupportedBoundaries_AreAccepted(
		int rate,
		int maxSteps,
		int deltaMilliseconds)
	{
		GameLoopSettings settings = new(
			rate,
			maxSteps,
			TimeSpan.FromMilliseconds(deltaMilliseconds));

		Assert.Equal(rate, settings.FixedUpdatesPerSecond);
		Assert.Equal(maxSteps, settings.MaxFixedStepsPerFrame);
		Assert.Equal(
			TimeSpan.FromMilliseconds(deltaMilliseconds),
			settings.MaximumFrameDelta);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(1001)]
	public void UnsupportedRates_AreRejected(int rate)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoopSettings(fixedUpdatesPerSecond: rate));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(101)]
	public void UnsupportedStepCaps_AreRejected(int maxSteps)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoopSettings(maxFixedStepsPerFrame: maxSteps));
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(-10.0)]
	[InlineData(10_001.0)]
	public void UnsupportedFrameDeltas_AreRejected(double milliseconds)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoopSettings(
				maximumFrameDelta: TimeSpan.FromMilliseconds(milliseconds)));
	}

	[Fact]
	public void DefaultSettingsValue_IsRejectedByTheLoop()
	{
		GameLoopSettings invalid = default;

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoop(invalid, new FakeMonotonicClock()));
	}

	[Fact]
	public void ClockWithAnInvalidRate_IsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => new GameLoop(
				GameLoopSettings.Default,
				new InvalidClock()));
	}

	[Fact]
	public void StopwatchStyleTickRate_IsConvertedExactly()
	{
		// Ten million ticks per second is the rate Windows uses for
		// Stopwatch.Frequency.
		FakeMonotonicClock clock = new(10_000_000);
		GameLoop loop = new(GameLoopSettings.Default, clock);

		clock.AdvanceTicks(1);
		Assert.Equal(0, loop.Advance().FixedSteps);

		clock.AdvanceTicks(166_667);
		FrameTiming frame = loop.Advance();

		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(
			TimeSpan.FromTicks(166_667),
			frame.RealDelta);
	}

	private sealed class InvalidClock : IMonotonicClock
	{
		public long Timestamp => 0;

		public long TicksPerSecond => 0;
	}
}
