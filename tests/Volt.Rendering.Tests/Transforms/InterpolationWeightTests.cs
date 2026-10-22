using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Transforms;

public sealed class InterpolationWeightTests
{
	[Fact]
	public void RunningSimulation_PassesAlphaThrough()
	{
		InterpolationWeight weight = new();

		Assert.Equal(0.25f, weight.Resolve(0.25f, false, false));
		Assert.Equal(0.75f, weight.Resolve(0.75f, false, false));
		Assert.False(weight.IsHoldingCommittedPose);
	}

	[Fact]
	public void PausedSimulation_ShowsTheCommittedPose()
	{
		InterpolationWeight weight = new();

		Assert.Equal(1.0f, weight.Resolve(0.0f, false, true));
		Assert.True(weight.IsHoldingCommittedPose);

		// Repeated paused frames stay on the same pose.
		Assert.Equal(1.0f, weight.Resolve(0.4f, false, true));
		Assert.Equal(1.0f, weight.Resolve(0.9f, false, true));
	}

	[Fact]
	public void SingleStepWhilePaused_ShowsTheNewCommittedPose()
	{
		InterpolationWeight weight = new();

		weight.Resolve(0.0f, false, true);

		Assert.Equal(1.0f, weight.Resolve(0.0f, true, true));
		Assert.True(weight.IsHoldingCommittedPose);
	}

	[Fact]
	public void Resume_DoesNotSnapBackBeforeTheNextStep()
	{
		InterpolationWeight weight = new();

		weight.Resolve(0.2f, false, true);

		// Resumed, but the frame executed no fixed step: the last
		// displayed pose stays visible instead of rewinding one step.
		Assert.Equal(1.0f, weight.Resolve(0.0f, false, false));
		Assert.Equal(1.0f, weight.Resolve(0.35f, false, false));

		// The first new step shifts that pose into the previous
		// slot, so ordinary interpolation is continuous again.
		Assert.Equal(0.0f, weight.Resolve(0.0f, true, false));
		Assert.False(weight.IsHoldingCommittedPose);
	}

	[Fact]
	public void FrozenTimeScale_BehavesLikePause()
	{
		InterpolationWeight weight = new();

		Assert.Equal(1.0f, weight.Resolve(0.6f, false, true));

		// Returning to normal speed with no step yet still holds.
		Assert.Equal(1.0f, weight.Resolve(0.6f, false, false));

		Assert.Equal(0.6f, weight.Resolve(0.6f, true, false));
	}

	[Fact]
	public void Alpha_IsClampedAndNonFiniteValuesAreIgnored()
	{
		InterpolationWeight weight = new();

		Assert.Equal(0.0f, weight.Resolve(-0.5f, false, false));
		Assert.Equal(1.0f, weight.Resolve(2.0f, false, false));
		Assert.Equal(0.0f, weight.Resolve(float.NaN, false, false));
	}

	[Fact]
	public void Reset_ForgetsTheHeldPose()
	{
		InterpolationWeight weight = new();

		weight.Resolve(0.0f, false, true);
		weight.Reset();

		Assert.False(weight.IsHoldingCommittedPose);
	}
}
