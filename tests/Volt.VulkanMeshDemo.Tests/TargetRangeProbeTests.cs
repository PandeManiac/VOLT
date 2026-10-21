using System.Numerics;
using Volt.Rendering.Cameras;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

/// <summary>
/// Recomputes the pixel probes used by the headless GPU runs from the
/// same camera, component, and projection data the renderer uses.
/// </summary>
/// <remarks>
/// A probe that stops being interior to its target, or that no longer
/// lands on the reticle, fails here before a GPU run can silently
/// verify the wrong pixel.
/// </remarks>
public sealed class TargetRangeProbeTests
{
	private const int Width = 1280;
	private const int Height = 720;
	private const int SmallWidth = 960;
	private const int SmallHeight = 540;

	// Probes documented in the sample README and used by the headless
	// harness commands.
	private static readonly Vector2 InitialNearProbe = new(405.0f, 360.0f);
	private static readonly Vector2 InitialCentreProbe =
		new(648.0f, 372.0f);

	private static readonly Vector2 CompletedNearProbe =
		new(310.0f, 323.0f);

	private static readonly Vector2 CompletedHighProbe =
		new(479.0f, 263.0f);

	[Fact]
	public void InitialPose_ProjectsTheDocumentedUnhitProbes()
	{
		using TargetRangeScene scene = CreateScene();
		CameraController camera = new(
			TargetRangeScene.InitialCameraPosition);

		scene.SyncCamera(camera);

		Vector2 nearCentre = ProjectTarget(scene, 0, Width, Height);
		Vector2 centreCentre = ProjectTarget(scene, 2, Width, Height);

		Assert.InRange(nearCentre.X, 403.5f, 406.5f);
		Assert.InRange(nearCentre.Y, 358.5f, 361.5f);
		Assert.InRange(centreCentre.X, 638.5f, 641.5f);
		Assert.InRange(centreCentre.Y, 362.5f, 365.5f);

		AssertProbeIsInterior(scene, 0, nearCentre, InitialNearProbe, Height);
		AssertProbeIsInterior(
			scene,
			2,
			centreCentre,
			InitialCentreProbe,
			Height);

		// The centre probe must also clear the reticle's arms, or it
		// would be white instead of the target's colour.
		Assert.True(
			IsOutsideReticle(InitialCentreProbe, Width, Height),
			"the centre probe overlaps the reticle.");
	}

	[Fact]
	public void InitialPose_ProjectsEveryTargetIntoTheView()
	{
		using TargetRangeScene scene = CreateScene();
		CameraController camera = new(
			TargetRangeScene.InitialCameraPosition);

		scene.SyncCamera(camera);

		for (int index = 0; index < TargetRangeScene.TargetCount; index++)
		{
			Vector2 centre = ProjectTarget(scene, index, Width, Height);

			Assert.InRange(centre.X, 0.0f, Width);
			Assert.InRange(centre.Y, 0.0f, Height);
		}
	}

	[Fact]
	public void CompletedRound_ProjectsTheDocumentedHitProbes()
	{
		using TargetRangeScene scene = CreateScene();
		TargetRangeController controller = new(scene);
		TargetRangeScript script = new();
		CameraController camera = new(
			TargetRangeScene.InitialCameraPosition);

		for (int frame = 0; frame < 200; frame++)
		{
			script.Advance(scene, controller, camera);
		}

		Assert.True(script.HasReachedFinalState);
		Assert.True(controller.IsRoundComplete);

		Vector2 nearCentre = ProjectTarget(scene, 0, Width, Height);
		Vector2 highCentre = ProjectTarget(scene, 3, Width, Height);

		// At 1280x720 the completed targets are documented to project
		// near (310, 323) and (479, 263).
		Assert.InRange(nearCentre.X, 308.0f, 312.0f);
		Assert.InRange(nearCentre.Y, 321.5f, 325.5f);
		Assert.InRange(highCentre.X, 477.0f, 481.0f);
		Assert.InRange(highCentre.Y, 261.0f, 265.0f);

		AssertProbeIsInterior(
			scene,
			0,
			nearCentre,
			CompletedNearProbe,
			Height);

		AssertProbeIsInterior(
			scene,
			3,
			highCentre,
			CompletedHighProbe,
			Height);

		// The resize run uses the same window position scaled by 0.75.
		Vector2 smallNear = ProjectTarget(
			scene,
			0,
			SmallWidth,
			SmallHeight);

		Vector2 smallHigh = ProjectTarget(
			scene,
			3,
			SmallWidth,
			SmallHeight);

		Assert.InRange(smallNear.X, 231.0f, 234.0f);
		Assert.InRange(smallNear.Y, 241.0f, 244.0f);
		Assert.InRange(smallHigh.X, 358.0f, 361.0f);
		Assert.InRange(smallHigh.Y, 196.0f, 199.0f);

		AssertProbeIsInterior(
			scene,
			0,
			smallNear,
			CompletedNearProbe * 0.75f,
			SmallHeight);

		AssertProbeIsInterior(
			scene,
			3,
			smallHigh,
			CompletedHighProbe * 0.75f,
			SmallHeight);
	}

	[Fact]
	public void ReticleProbes_StayOnTheHorizontalArmAtBothSizes()
	{
		AssertReticleProbe(new Vector2(647.0f, 360.0f), Width, Height);
		AssertReticleProbe(
			new Vector2(485.0f, 270.0f),
			SmallWidth,
			SmallHeight);
	}

	private static void AssertReticleProbe(
		Vector2 probe,
		int width,
		int height)
	{
		float half = TargetRangeReticle.ResolveHalfSizePixels(height);
		float armHalfThickness = half * 0.25f;

		float offsetX = probe.X - (width * 0.5f);
		float offsetY = probe.Y - (height * 0.5f);

		// The horizontal arm spans the full width of the plus and is
		// 25% of the half size thick, so the probe is white while it
		// stays on that arm and clear of its edge.
		Assert.InRange(
			offsetX,
			-(half - 2.0f),
			half - 2.0f);

		Assert.InRange(
			MathF.Abs(offsetY),
			0.0f,
			armHalfThickness - 1.0f);
	}

	private static bool IsOutsideReticle(
		Vector2 probe,
		int width,
		int height)
	{
		float half = TargetRangeReticle.ResolveHalfSizePixels(height);
		float centreX = width * 0.5f;
		float centreY = height * 0.5f;

		// The plus is the union of a horizontal and a vertical bar;
		// each bar is 25% of the half size thick.
		float arm = half * 0.25f;

		bool insideHorizontal =
			MathF.Abs(probe.X - centreX) <= half &&
			MathF.Abs(probe.Y - centreY) <= arm;

		bool insideVertical =
			MathF.Abs(probe.X - centreX) <= arm &&
			MathF.Abs(probe.Y - centreY) <= half;

		return !insideHorizontal && !insideVertical;
	}

	/// <summary>
	/// Requires the probe to sit inside the target's drawn silhouette
	/// with margin, using the target's current scale as its radius.
	/// </summary>
	private static void AssertProbeIsInterior(
		TargetRangeScene scene,
		int index,
		Vector2 centre,
		Vector2 probe,
		int height)
	{
		TargetSphere sphere = scene.GetTargetSphere(index);

		float distance = Vector3.Distance(
			scene.CameraPosition,
			sphere.Center);

		// A sphere of radius r at distance d subtends
		// atan(r / d); on a YDown (or YUp) perspective viewport its
		// pixel radius is r * f / d with f = 1 / tan(fov / 2).
		float focal = 1.0f / MathF.Tan(
			TargetRangeScene.FieldOfViewRadians * 0.5f);

		float pixelRadius = sphere.Radius * focal / distance *
			(height * 0.5f);

		float probeDistance = Vector2.Distance(probe, centre);

		Assert.True(
			probeDistance <= pixelRadius * 0.6f,
			$"probe {probe} is {probeDistance:F1} pixels from the " +
			$"target centre, but the target only spans " +
			$"{pixelRadius:F1} pixels.");
	}

	private static Vector2 ProjectTarget(
		TargetRangeScene scene,
		int index,
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

		Vector4 clip = Vector4.Transform(
			new Vector4(scene.GetTargetPosition(index), 1.0f),
			view * projection);

		Assert.True(clip.W > 0.0f);

		return new Vector2(
			((clip.X / clip.W) + 1.0f) * 0.5f * width,
			((clip.Y / clip.W) + 1.0f) * 0.5f * height);
	}

	private static TargetRangeScene CreateScene()
	{
		return new TargetRangeScene(
			EcsMeshResolver.CreateTargetRange(
				MeshGeometry.CreateTargetRangeMeshes()));
	}
}
