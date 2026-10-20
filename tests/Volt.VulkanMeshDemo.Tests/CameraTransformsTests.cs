using System.Numerics;
using System.Runtime.InteropServices;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Transforms;
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
	public void CreateView_UsesTheSuppliedCameraForThePerspectiveScene()
	{
		CameraController camera = new(CameraTransforms.CameraPosition);

		camera.ApplyLook(-300.0f, 40.0f);

		Assert.Equal(
			camera.CreateView(),
			CameraTransforms.CreateView(
				DemoScene.CameraTransforms,
				camera));

		Assert.NotEqual(
			CameraTransforms.CreateView(DemoScene.CameraTransforms),
			CameraTransforms.CreateView(
				DemoScene.CameraTransforms,
				camera));
	}

	[Fact]
	public void CreateView_IgnoresTheCameraForClipSpaceScenes()
	{
		CameraController camera = new(CameraTransforms.CameraPosition);

		camera.ApplyLook(-300.0f, 40.0f);

		Assert.Equal(
			Matrix4x4.Identity,
			CameraTransforms.CreateView(DemoScene.Gallery, camera));

		Assert.Equal(
			Matrix4x4.Identity,
			CameraTransforms.CreateView(
				DemoScene.DepthOcclusion,
				camera));
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
	public void PerspectiveCamera_KeepsTheSamplesFarPlane()
	{
		CameraComponent camera = CameraTransforms.PerspectiveCamera;

		Assert.Equal(
			CameraTransforms.FieldOfViewRadians,
			camera.VerticalFieldOfView);

		Assert.Equal(CameraTransforms.NearPlane, camera.NearPlane);
		Assert.Equal(100.0f, camera.FarPlane);

		// The ECS default uses a far plane of 1000. The sample's
		// scene is defined with 100 and must not inherit it.
		Assert.NotEqual(CameraComponent.Default.FarPlane, camera.FarPlane);
	}

	[Fact]
	public void CreateProjection_PerspectiveUsesTheSharedAdapter()
	{
		Matrix4x4 expected = CameraProjection.CreatePerspective(
			CameraTransforms.PerspectiveCamera,
			WideAspect,
			ViewportYOrientation.YDown);

		Matrix4x4 actual = CameraTransforms.CreateProjection(
			DemoScene.CameraTransforms,
			WideAspect);

		Assert.Equal(expected, actual);

		// The Y orientation is explicit: without it the image is
		// vertically mirrored.
		Assert.NotEqual(
			expected,
			CameraTransforms.PerspectiveCamera
				.CreateProjectionMatrix(WideAspect));
	}

	[Fact]
	public void TriangleAModel_MatchesTheSharedTransformComposition()
	{
		Matrix4x4 expected = TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(-0.75f, 0.35f, 0.0f)),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(0.45f)));

		Assert.Equal(expected, CameraTransforms.TriangleAModel);
		Assert.Equal(0.45f, CameraTransforms.TriangleAModel.M11);
		Assert.Equal(
			-0.75f,
			CameraTransforms.TriangleAModel.M41);
	}

	[Fact]
	public void TriangleBModel_MatchesThePreviousCompoundRotation()
	{
		Matrix4x4 previous =
			Matrix4x4.CreateScale(0.40f) *
			Matrix4x4.CreateRotationZ(25.0f * (MathF.PI / 180.0f)) *
			Matrix4x4.CreateRotationY(35.0f * (MathF.PI / 180.0f)) *
			Matrix4x4.CreateTranslation(1.05f, 0.50f, -0.35f);

		Matrix4x4 actual = CameraTransforms.TriangleBModel;

		// The quaternion conversion reorders the factors, so only
		// floating-point rounding may differ from the previous
		// matrix product.
		AssertMatrixNear(previous, actual, 0.000001f);

		Matrix4x4 wrongOrder =
			Matrix4x4.CreateScale(0.40f) *
			Matrix4x4.CreateRotationY(35.0f * (MathF.PI / 180.0f)) *
			Matrix4x4.CreateRotationZ(25.0f * (MathF.PI / 180.0f)) *
			Matrix4x4.CreateTranslation(1.05f, 0.50f, -0.35f);

		Assert.True(
			MaxDifference(previous, wrongOrder) > 0.01f,
			"Swapping the rotation order must change the transform.");
	}

	[Fact]
	public void QuadModel_UsesNonuniformScale()
	{
		Vector3 world = Vector3.Transform(
			Vector3.One,
			CameraTransforms.QuadModel);

		AssertVectorNear(
			new Vector3(1.35f, 0.75f, -0.1f),
			world);
	}

	[Fact]
	public void HexagonModel_MatchesTheDocumentedScaleAndOffset()
	{
		Vector3 world = Vector3.Transform(
			Vector3.Zero,
			CameraTransforms.HexagonModel);

		Assert.Equal(new Vector3(-1.60f, -0.85f, -0.30f), world);
		Assert.Equal(0.30f, CameraTransforms.HexagonModel.M22, 5);
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

	private static void AssertMatrixNear(
		Matrix4x4 expected,
		Matrix4x4 actual,
		float tolerance)
	{
		Assert.True(
			MaxDifference(expected, actual) <= tolerance,
			$"Matrices differ by {MaxDifference(expected, actual)}.");
	}

	private static float MaxDifference(
		Matrix4x4 left,
		Matrix4x4 right)
	{
		float difference = 0.0f;

		for (int row = 0; row < 4; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				difference = MathF.Max(
					difference,
					MathF.Abs(left[row, column] - right[row, column]));
			}
		}

		return difference;
	}

	private static void AssertVectorNear(
		Vector3 expected,
		Vector3 actual)
	{
		Assert.True(
			Vector3.Distance(expected, actual) <= 0.00001f,
			$"Expected {expected}, actual {actual}.");
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
