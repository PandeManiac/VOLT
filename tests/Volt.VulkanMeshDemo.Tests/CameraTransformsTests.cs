using System.Numerics;
using System.Runtime.InteropServices;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class CameraTransformsTests
{
	private const float WideAspect = 1280.0f / 720.0f;

	[Fact]
	public void CreateView_PlacesCameraTargetThreeUnitsAhead()
	{
		Matrix4x4 view = CameraTransforms.CreateView(
			DemoScene.CameraTransforms);

		Vector3 target = Vector3.Transform(
			CameraTransforms.CameraTarget,
			view);

		Vector3 point = Vector3.Transform(
			new Vector3(1.0f, 2.0f, 0.0f),
			view);

		Assert.Equal(0.0f, target.X, 5);
		Assert.Equal(0.0f, target.Y, 5);
		Assert.Equal(-3.0f, target.Z, 5);

		Assert.Equal(1.0f, point.X, 5);
		Assert.Equal(2.0f, point.Y, 5);
		Assert.Equal(-3.0f, point.Z, 5);
	}

	[Fact]
	public void CreateView_UsesIdentityForClipSpaceScenes()
	{
		Assert.Equal(
			Matrix4x4.Identity,
			CameraTransforms.CreateView(DemoScene.Gallery));

		Assert.Equal(
			Matrix4x4.Identity,
			CameraTransforms.CreateView(DemoScene.DepthOcclusion));

		Assert.NotEqual(
			Matrix4x4.Identity,
			CameraTransforms.CreateView(DemoScene.CameraTransforms));
	}

	[Fact]
	public void CreateProjection_LegacySceneReproducesAspectCorrection()
	{
		Matrix4x4 projection = CameraTransforms.CreateProjection(
			DemoScene.Gallery,
			WideAspect);

		Vector4 clip = Vector4.Transform(
			new Vector4(0.4f, 0.3f, 0.2f, 1.0f),
			projection);

		// horizontal scale = 1 / max(16/9, 1) = 0.5625
		// vertical scale = min(16/9, 1) = 1, negated for Vulkan.
		Assert.Equal(0.225f, clip.X, 5);
		Assert.Equal(-0.3f, clip.Y, 5);
		Assert.Equal(0.2f, clip.Z, 5);
		Assert.Equal(1.0f, clip.W, 5);
	}

	[Fact]
	public void CreateProjection_LegacySceneHandlesTallViewports()
	{
		Matrix4x4 projection = CameraTransforms.CreateProjection(
			DemoScene.DepthOcclusion,
			0.5f);

		Vector4 clip = Vector4.Transform(
			new Vector4(0.4f, 0.3f, 0.8f, 1.0f),
			projection);

		// horizontal scale = 1 / max(0.5, 1) = 1
		// vertical scale = min(0.5, 1) = 0.5, negated for Vulkan.
		Assert.Equal(0.4f, clip.X, 5);
		Assert.Equal(-0.15f, clip.Y, 5);
		Assert.Equal(0.8f, clip.Z, 5);
		Assert.Equal(1.0f, clip.W, 5);
	}

	[Fact]
	public void CreateProjection_PerspectiveMapsNearAndFarToVulkanDepthRange()
	{
		Matrix4x4 projection = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			WideAspect);

		Vector4 nearClip = Vector4.Transform(
			new Vector4(
				0.0f,
				0.0f,
				-CameraTransforms.NearPlane,
				1.0f),
			projection);

		Vector4 farClip = Vector4.Transform(
			new Vector4(
				0.0f,
				0.0f,
				-CameraTransforms.FarPlane,
				1.0f),
			projection);

		Assert.True(nearClip.W > 0.0f);
		Assert.True(farClip.W > 0.0f);

		Assert.Equal(
			CameraTransforms.NearPlane,
			nearClip.W,
			4);

		Assert.Equal(
			CameraTransforms.FarPlane,
			farClip.W,
			3);

		Assert.Equal(0.0f, nearClip.Z / nearClip.W, 4);
		Assert.Equal(1.0f, farClip.Z / farClip.W, 4);
	}

	[Fact]
	public void CreateProjection_PerspectiveFlipsYForVulkanViewport()
	{
		Matrix4x4 projection = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			WideAspect);

		// A world-space point one unit above the camera target
		// becomes (0, 1, -3) in view space.
		Vector4 clip = Vector4.Transform(
			new Vector4(0.0f, 1.0f, -3.0f, 1.0f),
			projection);

		float ndcY = clip.Y / clip.W;

		// Vulkan's framebuffer Y axis points down, so +Y in world
		// space must produce a negative NDC Y (the top of the image).
		Assert.True(ndcY < 0.0f);
		Assert.Equal(-0.57735026f, ndcY, 4);
	}

	[Fact]
	public void CreateProjection_DependsOnFramebufferAspect()
	{
		Matrix4x4 wideFrom720 = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			1280.0f / 720.0f);

		Matrix4x4 wideFrom540 = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			960.0f / 540.0f);

		Matrix4x4 square = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			800.0f / 800.0f);

		// Both resize extents share the 16:9 aspect ratio.
		Assert.Equal(wideFrom720, wideFrom540);

		Assert.NotEqual(wideFrom720, square);

		// A narrower viewport needs a larger horizontal scale.
		Assert.True(square.M11 > wideFrom720.M11);
	}

	[Fact]
	public void CreateProjection_RejectsInvalidAspect()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraTransforms.CreateProjection(
				DemoScene.CameraTransforms,
				0.0f));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => CameraTransforms.CreateProjection(
				DemoScene.Gallery,
				float.NaN));
	}

	[Fact]
	public void CreateModelViewProjection_ComposesModelViewProjectionInOrder()
	{
		Matrix4x4 model = CameraTransforms.TriangleBModel;

		Matrix4x4 mvp = CameraTransforms.CreateModelViewProjection(
			DemoScene.CameraTransforms,
			model,
			WideAspect);

		Matrix4x4 expected =
			model *
			CameraTransforms.CreateView(DemoScene.CameraTransforms) *
			CameraTransforms.CreateProjection(
				DemoScene.CameraTransforms,
				WideAspect);

		Assert.Equal(expected, mvp);
	}

	[Fact]
	public void CreateModelViewProjection_MatchesImplicitColumnMajorPacking()
	{
		Matrix4x4 mvp = CameraTransforms.CreateModelViewProjection(
			DemoScene.CameraTransforms,
			CameraTransforms.TriangleBModel,
			WideAspect);

		Vector4 local = new(0.25f, -0.35f, 0.1f, 1.0f);

		// The CPU row-vector product is what the shader must produce
		// after its column-major reinterpretation of the same bytes.
		Vector4 expected = Vector4.Transform(local, mvp);
		Vector4 shader = MultiplyLikeGlsl(mvp, local);

		Assert.True(
			Vector4.Distance(expected, shader) < 1e-4f,
			$"CPU {expected} and column-major shader {shader} differ.");

		// A transposed CPU matrix would not reproduce the shader
		// result for this asymmetric transform.
		Vector4 transposed = Vector4.Transform(
			local,
			Matrix4x4.Transpose(mvp));

		Assert.True(
			Vector4.Distance(expected, transposed) > 1e-3f);
	}

	[Fact]
	public void MeshPushConstants_AreOneSequentialMatrix()
	{
		Assert.Equal(64, Marshal.SizeOf<MeshPushConstants>());

		MeshPushConstants constants = new(
			CameraTransforms.TriangleAModel);

		Assert.Equal(
			CameraTransforms.TriangleAModel,
			constants.Mvp);
	}

	/// <summary>
	/// Multiplies the packed bytes the way GLSL reads a column-major
	/// <c>mat4</c> push constant.
	/// </summary>
	private static Vector4 MultiplyLikeGlsl(
		Matrix4x4 matrix,
		Vector4 vector)
	{
		Span<float> values = stackalloc float[16];

		MemoryMarshal.Write(MemoryMarshal.AsBytes(values), in matrix);

		Span<float> components = stackalloc float[4]
		{
			vector.X,
			vector.Y,
			vector.Z,
			vector.W
		};

		float x = 0.0f;
		float y = 0.0f;
		float z = 0.0f;
		float w = 0.0f;

		for (int column = 0; column < 4; column++)
		{
			int offset = column * 4;
			float scale = components[column];

			x += values[offset] * scale;
			y += values[offset + 1] * scale;
			z += values[offset + 2] * scale;
			w += values[offset + 3] * scale;
		}

		return new Vector4(x, y, z, w);
	}
}
