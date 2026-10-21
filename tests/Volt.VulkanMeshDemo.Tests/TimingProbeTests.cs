using System.Numerics;
using Volt.Ecs;
using Volt.Rendering.Cameras;
using Volt.Rendering.Extraction;
using Volt.Rendering.Transforms;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TimingProbeTests
{
	private const int Width = 1280;
	private const int Height = 720;

	private static readonly Vector2 MaximumProbe = new(671, 298);
	private static readonly Vector2 MinimumProbe = new(297, 298);
	private static readonly Vector2 QuadProbe = new(640, 360);

	[Fact]
	public void PulseAtMaximum_CoversTheDocumentedProbe()
	{
		using EcsDemoScene scene = CreateScene();

		scene.SetPosition(
			scene.TriangleAEntity,
			new Vector3(-0.75f + TimingDemo.PulseOffset(30), 0.35f, 0.0f));

		Vector2[] triangle = ProjectTriangle(scene);

		float clearance = TriangleClearance(MaximumProbe, triangle);

		Assert.True(
			clearance > 5.0f,
			$"The maximum probe is only {clearance:F2} pixels inside " +
			"the pulse triangle.");

		Assert.InRange(
			(triangle[0] + triangle[1] + triangle[2]).X / 3.0f,
			668.0f,
			674.0f);
		Assert.InRange(
			(triangle[0] + triangle[1] + triangle[2]).Y / 3.0f,
			295.0f,
			301.0f);

		AssertInsideQuad(scene, MaximumProbe);
		AssertInsideQuad(scene, QuadProbe);
	}

	[Fact]
	public void PulseAtMinimum_LeavesTheMaximumProbeBlue()
	{
		using EcsDemoScene scene = CreateScene();

		scene.SetPosition(
			scene.TriangleAEntity,
			new Vector3(-0.75f + TimingDemo.PulseOffset(90), 0.35f, 0.0f));

		Vector2[] triangle = ProjectTriangle(scene);

		float minimumClearance = TriangleClearance(
			MinimumProbe,
			triangle);

		Assert.True(
			minimumClearance > 5.0f,
			$"The minimum probe is only {minimumClearance:F2} pixels " +
			"inside the pulse triangle.");

		Assert.True(
			TriangleClearance(MaximumProbe, triangle) < 0.0f,
			"The maximum probe must be outside the pulse at its " +
			"minimum position.");

		AssertInsideQuad(scene, MaximumProbe);
	}

	[Fact]
	public void PulseAtMaximum_IsInFrontOfTheQuad()
	{
		using EcsDemoScene scene = CreateScene();

		scene.SetPosition(
			scene.TriangleAEntity,
			new Vector3(-0.75f + TimingDemo.PulseOffset(30), 0.35f, 0.0f));

		RenderInstance triangle = Find(
			scene.Extract(),
			scene.TriangleAEntity);
		RenderInstance quad = Find(scene.Extract(), scene.QuadEntity);

		// The pulse is nearer the camera than the quad, so depth
		// testing makes the red pixels win at the shared probe.
		Assert.True(triangle.Model.M43 > quad.Model.M43);
	}

	[Fact]
	public void ResizedProbes_ScaleWithTheFramebuffer()
	{
		using EcsDemoScene scene = CreateScene();

		scene.SetPosition(
			scene.TriangleAEntity,
			new Vector3(-0.75f + TimingDemo.PulseOffset(90), 0.35f, 0.0f));

		Vector2[] triangle = ProjectTriangle(scene, 960, 540);

		// 960x540 keeps the 16:9 aspect, so every pixel coordinate
		// scales by exactly 0.75.
		Vector2 resizedProbe = new(
			MinimumProbe.X * 0.75f,
			MinimumProbe.Y * 0.75f);

		Assert.True(TriangleClearance(resizedProbe, triangle) > 3.0f);
	}

	private static EcsDemoScene CreateScene()
	{
		return new EcsDemoScene(
			EcsMeshResolver.CreateDefault(
				MeshGeometry.CreateCameraTransformMeshes()));
	}

	private static Vector2[] ProjectTriangle(
		EcsDemoScene scene,
		int width = Width,
		int height = Height)
	{
		return
		[
			Project(scene, scene.TriangleAEntity, new Vector3(-0.6f, -0.45f, 0.0f), width, height),
			Project(scene, scene.TriangleAEntity, new Vector3(0.6f, -0.45f, 0.0f), width, height),
			Project(scene, scene.TriangleAEntity, new Vector3(0.0f, 0.55f, 0.0f), width, height)
		];
	}

	private static void AssertInsideQuad(
		EcsDemoScene scene,
		Vector2 probe)
	{
		Vector2[] corners =
		[
			Project(scene, scene.QuadEntity, new Vector3(-1.0f, -1.0f, 0.0f), Width, Height),
			Project(scene, scene.QuadEntity, new Vector3(1.0f, 1.0f, 0.0f), Width, Height)
		];

		float minimumX = MathF.Min(corners[0].X, corners[1].X);
		float maximumX = MathF.Max(corners[0].X, corners[1].X);
		float minimumY = MathF.Min(corners[0].Y, corners[1].Y);
		float maximumY = MathF.Max(corners[0].Y, corners[1].Y);

		Assert.True(probe.X > minimumX + 20.0f);
		Assert.True(probe.X < maximumX - 20.0f);
		Assert.True(probe.Y > minimumY + 20.0f);
		Assert.True(probe.Y < maximumY - 20.0f);
	}

	private static float TriangleClearance(
		Vector2 point,
		Vector2[] triangle)
	{
		Vector2 a = triangle[0];
		Vector2 b = triangle[1];
		Vector2 c = triangle[2];

		float ab = Cross(b - a, point - a);
		float bc = Cross(c - b, point - b);
		float ca = Cross(a - c, point - c);

		bool inside = (ab >= 0.0f && bc >= 0.0f && ca >= 0.0f) ||
			(ab <= 0.0f && bc <= 0.0f && ca <= 0.0f);

		if (!inside)
		{
			return -1.0f;
		}

		return MathF.Min(
			MathF.Abs(ab) / Vector2.Distance(a, b),
			MathF.Min(
				MathF.Abs(bc) / Vector2.Distance(b, c),
				MathF.Abs(ca) / Vector2.Distance(c, a)));
	}

	private static float Cross(Vector2 left, Vector2 right)
	{
		return (left.X * right.Y) - (left.Y * right.X);
	}

	private static Vector2 Project(
		EcsDemoScene scene,
		Entity entity,
		Vector3 local,
		int width,
		int height)
	{
		CameraEntityMatrices.CreateMatrices(
			scene.World,
			scene.CameraEntity,
			width / (float)height,
			ViewportYOrientation.YDown,
			out Matrix4x4 view,
			out Matrix4x4 projection);

		RenderInstance instance = Find(scene.Extract(), entity);

		Matrix4x4 mvp = TransformMatrices.CreateModelViewProjection(
			instance.Model,
			view,
			projection);

		Vector4 clip = Vector4.Transform(
			new Vector4(local, 1.0f),
			mvp);

		Assert.True(clip.W > 0.0f);

		return new Vector2(
			((clip.X / clip.W) + 1.0f) * 0.5f * width,
			((clip.Y / clip.W) + 1.0f) * 0.5f * height);
	}

	private static RenderInstance Find(
		ReadOnlySpan<RenderInstance> instances,
		Entity entity)
	{
		foreach (RenderInstance instance in instances)
		{
			if (instance.Entity == entity)
			{
				return instance;
			}
		}

		throw new InvalidOperationException(
			$"Entity {entity} was not extracted.");
	}
}
