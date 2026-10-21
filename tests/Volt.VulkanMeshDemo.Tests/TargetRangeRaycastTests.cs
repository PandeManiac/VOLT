using System.Numerics;
using Volt.Ecs;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeRaycastTests
{
	private const float MaximumDistance = 40.0f;

	private static readonly Entity First = new(1, 1, 1);
	private static readonly Entity Second = new(1, 2, 1);

	[Fact]
	public void Cast_DirectHit_ReturnsTheNearSurface()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(First, result.Entity);
		Assert.Equal(9.0f, result.Distance, 4);
		Assert.Equal(new Vector3(0.0f, 0.0f, -9.0f), result.Point);
	}

	[Fact]
	public void Cast_Miss_LeavesTheResultEmpty()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(5.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.False(result.Hit);
		Assert.Equal(default, result.Entity);
		Assert.Equal(0.0f, result.Distance);
		Assert.Equal(Vector3.Zero, result.Point);
	}

	[Fact]
	public void Cast_ChoosesTheNearestOverlappingTarget()
	{
		TargetSphere[] targets =
		[
			new(Second, new Vector3(0.0f, 0.0f, -12.0f), 2.0f, false),
			new(First, new Vector3(0.0f, 0.0f, -10.0f), 2.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(First, result.Entity);
		Assert.Equal(8.0f, result.Distance, 4);
	}

	[Fact]
	public void Cast_RejectsTargetsBehindTheOrigin()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, 10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.False(result.Hit);
	}

	[Fact]
	public void Cast_RejectsTargetsBeyondTheMaximumDistance()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -50.0f), 1.0f, false)
		];

		Assert.False(
			TargetRangeRaycast.Cast(
				Vector3.Zero,
				-Vector3.UnitZ,
				MaximumDistance,
				targets).Hit);

		Assert.True(
			TargetRangeRaycast.Cast(
				Vector3.Zero,
				-Vector3.UnitZ,
				60.0f,
				targets).Hit);
	}

	[Fact]
	public void Cast_FromInsideTheSphere_HitsOnTheWayOut()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			new Vector3(0.0f, 0.0f, -10.0f),
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(1.0f, result.Distance, 4);
		Assert.Equal(new Vector3(0.0f, 0.0f, -11.0f), result.Point);
	}

	[Fact]
	public void Cast_HandlesAnArbitraryCameraOrientation()
	{
		Vector3 direction = Vector3.Normalize(
			new Vector3(0.35f, -0.2f, -0.9f));

		Vector3 center = direction * 12.0f;

		TargetSphere[] targets =
		[
			new(First, center, 0.75f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			direction,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(11.25f, result.Distance, 3);
		Assert.Equal(0.75f, Vector3.Distance(result.Point, center), 3);
	}

	[Fact]
	public void Cast_IgnoresCompletedTargets()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -5.0f), 1.0f, true),
			new(Second, new Vector3(0.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(Second, result.Entity);
		Assert.Equal(9.0f, result.Distance, 4);
	}

	[Fact]
	public void Cast_WithOnlyCompletedTargets_Misses()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -5.0f), 1.0f, true)
		];

		Assert.False(
			TargetRangeRaycast.Cast(
				Vector3.Zero,
				-Vector3.UnitZ,
				MaximumDistance,
				targets).Hit);
	}

	[Fact]
	public void Cast_NormalizesTheDirectionBeforeMeasuring()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult unit = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		TargetRaycastResult scaled = TargetRangeRaycast.Cast(
			Vector3.Zero,
			new Vector3(0.0f, 0.0f, -7.0f),
			MaximumDistance,
			targets);

		Assert.Equal(unit.Distance, scaled.Distance, 4);
		Assert.Equal(unit.Point, scaled.Point);
	}

	[Theory]
	[InlineData(0.0f, 0.0f, 0.0f)]
	[InlineData(float.NaN, 0.0f, -1.0f)]
	[InlineData(0.0f, float.PositiveInfinity, -1.0f)]
	public void Cast_WithAnInvalidDirection_Misses(
		float x,
		float y,
		float z)
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -1.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			new Vector3(x, y, z),
			MaximumDistance,
			targets);

		Assert.False(result.Hit);
	}

	[Theory]
	[InlineData(0.0f)]
	[InlineData(-1.0f)]
	[InlineData(float.NaN)]
	[InlineData(float.PositiveInfinity)]
	public void Cast_WithAnInvalidMaximumDistance_Misses(float maximum)
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -1.0f), 1.0f, false)
		];

		Assert.False(
			TargetRangeRaycast.Cast(
				Vector3.Zero,
				-Vector3.UnitZ,
				maximum,
				targets).Hit);
	}

	[Fact]
	public void Cast_WithANonFiniteOrigin_Misses()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -1.0f), 1.0f, false)
		];

		Assert.False(
			TargetRangeRaycast.Cast(
				new Vector3(float.NaN, 0.0f, 0.0f),
				-Vector3.UnitZ,
				MaximumDistance,
				targets).Hit);
	}

	[Fact]
	public void Cast_WithNoTargets_Misses()
	{
		Assert.False(
			TargetRangeRaycast.Cast(
				Vector3.Zero,
				-Vector3.UnitZ,
				MaximumDistance,
				[]).Hit);
	}

	[Fact]
	public void Cast_SkipsDegenerateSpheres()
	{
		TargetSphere[] targets =
		[
			new(First, new Vector3(0.0f, 0.0f, -5.0f), 0.0f, false),
			new(First, new Vector3(0.0f, 0.0f, -6.0f), float.NaN, false),
			new(Second, new Vector3(0.0f, 0.0f, -10.0f), 1.0f, false)
		];

		TargetRaycastResult result = TargetRangeRaycast.Cast(
			Vector3.Zero,
			-Vector3.UnitZ,
			MaximumDistance,
			targets);

		Assert.True(result.Hit);
		Assert.Equal(Second, result.Entity);
	}
}
