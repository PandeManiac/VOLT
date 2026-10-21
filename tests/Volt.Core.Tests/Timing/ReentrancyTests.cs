using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Covers the reentrancy guard: a frame must finish before another
/// <see cref="GameLoop.Advance"/> may start.
/// </summary>
public sealed class ReentrancyTests
{
	[Fact]
	public void NestedAdvance_FromUpdate_IsRejectedWithoutCorruptingTheFrame()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		Exception? nested = null;
		Exception? guard = null;
		bool attempted = false;

		loop.RegisterUpdate((in UpdateContext _) =>
		{
			if (attempted)
			{
				return;
			}

			attempted = true;

			try
			{
				loop.Advance();
			}
			catch (InvalidOperationException exception)
			{
				nested = exception;
			}

			try
			{
				loop.RegisterUpdate(
					static (in UpdateContext _) => { });
			}
			catch (InvalidOperationException exception)
			{
				guard = exception;
			}
		});

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.NotNull(nested);
		Assert.Contains("Advance", nested!.Message);
		Assert.NotNull(guard);
		Assert.Equal(1, frame.FrameIndex);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.LastFrame.FrameIndex);
		Assert.Equal(1, loop.TotalFixedSteps);

		// A nested attempt must not consume the next frame's delta.
		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming next = loop.Advance();

		Assert.Equal(2, next.FrameIndex);
		Assert.Equal(1, next.FixedSteps);
		Assert.Equal(2, loop.TotalFixedSteps);
	}

	[Fact]
	public void NestedAdvance_FromFixedUpdate_IsRejected()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		Exception? nested = null;

		loop.RegisterFixedUpdate((in FixedUpdateContext _) =>
		{
			try
			{
				loop.Advance();
			}
			catch (InvalidOperationException exception)
			{
				nested = exception;
			}
		});

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.NotNull(nested);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.TotalFixedSteps);
		Assert.Equal(1, loop.LastFrame.FrameIndex);

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming next = loop.Advance();

		Assert.Equal(2, next.FrameIndex);
		Assert.Equal(2, loop.TotalFixedSteps);
	}

	[Fact]
	public void NestedAdvance_FromScheduledCallback_IsRejected()
	{
		GameLoop loop = GameLoopLifecycleTests.CreateLoop(
			out FakeMonotonicClock clock);
		Exception? nested = null;

		loop.Schedule.After(
			TimeSpan.Zero,
			(in ScheduledUpdateContext _) =>
			{
				try
				{
					loop.Advance();
				}
				catch (InvalidOperationException exception)
				{
					nested = exception;
				}
			});

		clock.AdvanceNanoseconds(16_666_667);
		FrameTiming frame = loop.Advance();

		Assert.NotNull(nested);
		Assert.Equal(1, frame.FixedSteps);
		Assert.Equal(1, loop.Schedule.TotalInvocations);
		Assert.Equal(1, loop.TotalFixedSteps);

		clock.AdvanceNanoseconds(16_666_667);
		Assert.Equal(2, loop.Advance().FrameIndex);
	}
}
