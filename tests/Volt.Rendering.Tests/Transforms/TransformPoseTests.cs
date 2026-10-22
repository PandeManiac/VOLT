using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Transforms;

public sealed class TransformPoseTests
{
	[Fact]
	public void Identity_HasNoDisplacement()
	{
		TransformPose pose = TransformPose.Identity;

		Assert.Equal(Vector3.Zero, pose.Position);
		Assert.Equal(Quaternion.Identity, pose.Rotation);
		Assert.Equal(Vector3.One, pose.Scale);
	}

	[Fact]
	public void Lerp_AtZeroReturnsThePreviousPose()
	{
		TransformPose previous = Create(1.0f);
		TransformPose current = Create(9.0f);

		TransformPose result = TransformPose.Lerp(previous, current, 0.0f);

		Assert.Equal(previous.Position, result.Position);
		Assert.Equal(previous.Rotation, result.Rotation);
		Assert.Equal(previous.Scale, result.Scale);
	}

	[Fact]
	public void Lerp_AtOneReturnsTheCurrentPose()
	{
		TransformPose previous = Create(1.0f);
		TransformPose current = Create(9.0f);

		TransformPose result = TransformPose.Lerp(previous, current, 1.0f);

		Assert.Equal(current.Position, result.Position);
		Assert.Equal(current.Rotation, result.Rotation);
		Assert.Equal(current.Scale, result.Scale);
	}

	[Fact]
	public void Lerp_ClampsOutOfRangeAlpha()
	{
		TransformPose previous = Create(1.0f);
		TransformPose current = Create(9.0f);

		Assert.Equal(
			previous.Position,
			TransformPose.Lerp(previous, current, -0.5f).Position);
		Assert.Equal(
			current.Position,
			TransformPose.Lerp(previous, current, 1.5f).Position);
		Assert.Equal(
			previous.Position,
			TransformPose.Lerp(previous, current, float.NaN).Position);
	}

	[Fact]
	public void Lerp_HalfwayInterpolatesPositionAndScale()
	{
		TransformPose previous = new(
			new Vector3(-2.0f, 0.0f, 4.0f),
			Quaternion.Identity,
			new Vector3(1.0f, 2.0f, 3.0f));

		TransformPose current = new(
			new Vector3(2.0f, 4.0f, -4.0f),
			Quaternion.Identity,
			new Vector3(3.0f, 4.0f, 5.0f));

		TransformPose result = TransformPose.Lerp(previous, current, 0.5f);

		Assert.Equal(new Vector3(0.0f, 2.0f, 0.0f), result.Position);
		Assert.Equal(new Vector3(2.0f, 3.0f, 4.0f), result.Scale);
	}

	[Fact]
	public void Lerp_HandlesVeryLargeCoordinates()
	{
		TransformPose previous = new(
			new Vector3(1e9f, -1e9f, 0.0f),
			Quaternion.Identity,
			Vector3.One);

		TransformPose current = new(
			new Vector3(1e9f + 2.0f, -1e9f + 2.0f, 0.0f),
			Quaternion.Identity,
			Vector3.One);

		TransformPose result = TransformPose.Lerp(previous, current, 0.5f);

		Assert.Equal(1e9f + 1.0f, result.Position.X);
		Assert.Equal(-1e9f + 1.0f, result.Position.Y);
	}

	[Fact]
	public void Lerp_UsesTheShortestRotationArc()
	{
		// 350 degrees to 10 degrees is a 20-degree turn through zero,
		// not a 340-degree turn the other way.
		TransformPose previous = new(
			Vector3.Zero,
			RotationAroundZ(350.0f),
			Vector3.One);

		TransformPose current = new(
			Vector3.Zero,
			RotationAroundZ(10.0f),
			Vector3.One);

		TransformPose result = TransformPose.Lerp(previous, current, 0.5f);

		AssertRotationNear(RotationAroundZ(0.0f), result.Rotation, 0.001f);
	}

	[Fact]
	public void Lerp_TreatsNegatedQuaternionsAsTheSameOrientation()
	{
		Quaternion rotation = RotationAroundZ(120.0f);

		TransformPose previous = new(Vector3.Zero, rotation, Vector3.One);
		TransformPose current = new(Vector3.Zero, -rotation, Vector3.One);

		foreach (float alpha in new[] { 0.1f, 0.5f, 0.9f })
		{
			Quaternion result =
				TransformPose.Lerp(previous, current, alpha).Rotation;

			AssertRotationNear(rotation, result, 0.001f);
		}
	}

	[Fact]
	public void Lerp_NegatedEndpointsStillUseTheShortArc()
	{
		TransformPose previous = new(
			Vector3.Zero,
			RotationAroundZ(30.0f),
			Vector3.One);

		TransformPose current = new(
			Vector3.Zero,
			-RotationAroundZ(90.0f),
			Vector3.One);

		TransformPose result = TransformPose.Lerp(previous, current, 0.5f);

		AssertRotationNear(RotationAroundZ(60.0f), result.Rotation, 0.001f);
	}

	[Fact]
	public void Lerp_KeepsRotationNormalized()
	{
		Random random = new(20261021);

		for (int index = 0; index < 1000; index++)
		{
			Quaternion from = Quaternion.Normalize(new Quaternion(
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f));

			Quaternion to = Quaternion.Normalize(new Quaternion(
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f,
				random.NextSingle() - 0.5f));

			float alpha = random.NextSingle();

			Quaternion result = TransformPose.Lerp(
				new TransformPose(Vector3.Zero, from, Vector3.One),
				new TransformPose(Vector3.Zero, to, Vector3.One),
				alpha).Rotation;

			float length = MathF.Sqrt(
				(result.X * result.X) +
				(result.Y * result.Y) +
				(result.Z * result.Z) +
				(result.W * result.W));

			Assert.InRange(length, 0.99999f, 1.00001f);
		}
	}

	[Fact]
	public void Lerp_IdentityToRotationGoesHalfway()
	{
		TransformPose result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, Quaternion.Identity, Vector3.One),
			new TransformPose(
				Vector3.Zero,
				RotationAroundZ(90.0f),
				Vector3.One),
			0.5f);

		AssertRotationNear(RotationAroundZ(45.0f), result.Rotation, 0.001f);
	}

	[Fact]
	public void Lerp_SmallAnglesAreStable()
	{
		Quaternion from = RotationAroundZ(0.01f);
		Quaternion to = RotationAroundZ(0.02f);

		Quaternion result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, from, Vector3.One),
			new TransformPose(Vector3.Zero, to, Vector3.One),
			0.5f).Rotation;

		AssertRotationNear(RotationAroundZ(0.015f), result, 0.0005f);
	}

	[Fact]
	public void Lerp_InvalidPreviousRotationUsesTheCurrentRotation()
	{
		Quaternion current = RotationAroundZ(40.0f);

		TransformPose result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, default, Vector3.One),
			new TransformPose(Vector3.Zero, current, Vector3.One),
			0.5f);

		AssertRotationNear(current, result.Rotation, 0.001f);
	}

	[Fact]
	public void Lerp_InvalidCurrentRotationUsesThePreviousRotation()
	{
		Quaternion previous = RotationAroundZ(40.0f);

		TransformPose result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, previous, Vector3.One),
			new TransformPose(
				Vector3.Zero,
				new Quaternion(float.NaN, 0.0f, 0.0f, 1.0f),
				Vector3.One),
			0.5f);

		AssertRotationNear(previous, result.Rotation, 0.001f);
	}

	[Fact]
	public void Lerp_BothInvalidRotationsFallBackToIdentity()
	{
		TransformPose result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, default, Vector3.One),
			new TransformPose(Vector3.Zero, default, Vector3.One),
			0.5f);

		Assert.Equal(Quaternion.Identity, result.Rotation);
	}

	[Fact]
	public void Lerp_AllowsScaleToPassThroughZero()
	{
		TransformPose result = TransformPose.Lerp(
			new TransformPose(Vector3.Zero, Quaternion.Identity, Vector3.One),
			new TransformPose(
				Vector3.Zero,
				Quaternion.Identity,
				new Vector3(-1.0f)),
			0.5f);

		// A temporarily degenerate transform is the authored result;
		// it is not replaced with an epsilon scale.
		Assert.Equal(Vector3.Zero, result.Scale);
	}

	[Fact]
	public void Lerp_SupportsNonuniformAndNegativeScale()
	{
		TransformPose result = TransformPose.Lerp(
			new TransformPose(
				Vector3.Zero,
				Quaternion.Identity,
				new Vector3(-2.0f, 1.0f, 4.0f)),
			new TransformPose(
				Vector3.Zero,
				Quaternion.Identity,
				new Vector3(2.0f, 3.0f, 0.0f)),
			0.25f);

		Assert.Equal(-1.0f, result.Scale.X);
		Assert.Equal(1.5f, result.Scale.Y);
		Assert.Equal(3.0f, result.Scale.Z);
	}

	[Fact]
	public void CreateModelMatrix_MatchesTheComponentPath()
	{
		TransformPose pose = new(
			new Vector3(1.0f, 2.0f, 3.0f),
			RotationAroundZ(30.0f),
			new Vector3(2.0f, 3.0f, 4.0f));

		Matrix4x4 expected = TransformMatrices.CreateModelMatrix(
			new PositionComponent(pose.Position),
			new RotationComponent(pose.Rotation),
			new ScaleComponent(pose.Scale));

		Assert.Equal(expected, pose.CreateModelMatrix());
		Assert.Equal(expected, TransformMatrices.CreateModelMatrix(pose));
	}

	private static TransformPose Create(float value)
	{
		return new TransformPose(
			new Vector3(value),
			RotationAroundZ(value * 10.0f),
			new Vector3(value + 1.0f));
	}

	private static Quaternion RotationAroundZ(float degrees)
	{
		return Quaternion.CreateFromAxisAngle(
			Vector3.UnitZ,
			degrees * (MathF.PI / 180.0f));
	}

	private static void AssertRotationNear(
		Quaternion expected,
		Quaternion actual,
		float tolerance)
	{
		float dot = MathF.Abs(
			(expected.X * actual.X) +
			(expected.Y * actual.Y) +
			(expected.Z * actual.Z) +
			(expected.W * actual.W));

		Assert.True(
			1.0f - dot <= tolerance,
			$"Expected {expected}, actual {actual}.");
	}
}
