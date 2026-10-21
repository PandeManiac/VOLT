using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeReticleTests
{
	[Theory]
	[InlineData(1280, 720)]
	[InlineData(960, 540)]
	[InlineData(800, 600)]
	[InlineData(1920, 1080)]
	public void OverlayTransform_IsCentredAtEverySize(
		int width,
		int height)
	{
		Matrix4x4 transform =
			TargetRangeReticle.CreateOverlayTransform(width, height);

		// The reticle mesh is symmetric around the clip-space origin.
		// A zero translation therefore puts its centre exactly where
		// the camera's forward direction projects.
		Assert.Equal(Vector3.Zero, transform.Translation);
		Assert.Equal(0.0f, transform.M13);
		Assert.Equal(0.0f, transform.M23);
		Assert.Equal(0.0f, transform.M31);
		Assert.Equal(0.0f, transform.M32);
	}

	[Theory]
	[InlineData(1280, 720)]
	[InlineData(960, 540)]
	[InlineData(800, 600)]
	public void OverlayTransform_KeepsASquarePixelSize(
		int width,
		int height)
	{
		Matrix4x4 transform =
			TargetRangeReticle.CreateOverlayTransform(width, height);

		// The mesh spans -1..1, so its half extent in pixels is the
		// clip-space edge scaled by half the framebuffer.
		float halfWidthPixels = transform.M11 * width * 0.5f;
		float halfHeightPixels = transform.M22 * height * 0.5f;

		float expected =
			TargetRangeReticle.ResolveHalfSizePixels(height);

		Assert.Equal(expected, halfWidthPixels, 4);
		Assert.Equal(expected, halfHeightPixels, 4);
	}

	[Fact]
	public void ResolveHalfSizePixels_ScalesWithFramebufferHeight()
	{
		Assert.Equal(
			0.022f * 720.0f,
			TargetRangeReticle.ResolveHalfSizePixels(720),
			4);

		Assert.Equal(
			0.022f * 540.0f,
			TargetRangeReticle.ResolveHalfSizePixels(540),
			4);

		Assert.True(
			TargetRangeReticle.ResolveHalfSizePixels(540) <
			TargetRangeReticle.ResolveHalfSizePixels(720));
	}

	[Fact]
	public void ResolveHalfSizePixels_KeepsAMinimumSize()
	{
		Assert.Equal(
			TargetRangeReticle.MinimumHalfSizePixels,
			TargetRangeReticle.ResolveHalfSizePixels(32));
	}

	[Theory]
	[InlineData(0, 720)]
	[InlineData(1280, 0)]
	[InlineData(-1, 720)]
	[InlineData(1280, -1)]
	public void OverlayTransform_RejectsAnEmptyViewport(
		int width,
		int height)
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => TargetRangeReticle.CreateOverlayTransform(
				width,
				height));
	}

	[Fact]
	public void OverlayTransform_LeavesAuthoredDepthAlone()
	{
		Matrix4x4 transform =
			TargetRangeReticle.CreateOverlayTransform(1280, 720);

		Assert.Equal(1.0f, transform.M33);
		Assert.Equal(0.0f, transform.M34);
	}
}
