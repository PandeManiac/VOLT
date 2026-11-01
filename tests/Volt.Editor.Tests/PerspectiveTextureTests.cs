using System.Numerics;
using Volt.Editor.Rendering;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the texture coordinate interpolation the software renderer
/// uses. Screen-space weights alone make a receding surface's texture
/// swim, so the values are weighted by reciprocal clip W.
/// </summary>
public sealed class PerspectiveTextureTests
{
	[Fact]
	public void ASurfaceRecedingInDepthInterpolatesTowardTheFarCorner()
	{
		// The classic case: on screen the sample sits exactly halfway
		// between two corners, but the nearer one is three times closer
		// in clip space. Affine weights would report 0.5; the point that
		// is halfway in 3D is at 0.25 of the way along the coordinate.
		Vector2 uv = SoftwareSceneRasterizer.InterpolateUv(
			Vector2.Zero,
			1f,
			Vector2.UnitX,
			1f / 3f,
			Vector2.Zero,
			1f,
			0.5f,
			0.5f,
			0f);

		Assert.Equal(0.25f, uv.X, 4);
	}

	[Fact]
	public void EqualDepthsReduceToTheScreenSpaceWeight()
	{
		Vector2 uv = SoftwareSceneRasterizer.InterpolateUv(
			new Vector2(0.2f, 0.4f),
			0.5f,
			new Vector2(0.8f, 0.6f),
			0.5f,
			new Vector2(0.2f, 0.4f),
			0.5f,
			0f,
			0.5f,
			0.5f);

		Assert.Equal(0.5f, uv.X, 4);
		Assert.Equal(0.5f, uv.Y, 4);
	}

	[Fact]
	public void ThreeCornersBlendByTheirWeights()
	{
		Vector2 uv = SoftwareSceneRasterizer.InterpolateUv(
			Vector2.Zero,
			1f,
			Vector2.UnitX,
			1f,
			Vector2.UnitY,
			1f,
			0.25f,
			0.25f,
			0.5f);

		Assert.Equal(0.25f, uv.X, 4);
		Assert.Equal(0.5f, uv.Y, 4);
	}

	[Fact]
	public void ADegenerateWeightFallsBackToTheFirstCorner()
	{
		Vector2 uv = SoftwareSceneRasterizer.InterpolateUv(
			new Vector2(0.3f, 0.7f),
			0f,
			Vector2.UnitX,
			0f,
			Vector2.UnitY,
			0f,
			0.25f,
			0.25f,
			0.5f);

		Assert.Equal(new Vector2(0.3f, 0.7f), uv);
	}
}
