using System.Numerics;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class CameraControllerTests
{
	[Fact]
	public void Constructor_StartsAtDocumentedPose()
	{
		CameraController camera = new();

		Assert.Equal(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
		Assert.Equal(CameraController.DefaultMoveSpeed, camera.MoveSpeed);
		Assert.Equal(
			CameraController.DefaultSprintMultiplier,
			camera.SprintMultiplier);
		Assert.Equal(
			CameraController.DefaultMouseSensitivity,
			camera.MouseSensitivity);
	}

	[Fact]
	public void CreateView_MatchesThePreviousFixedCameraExactly()
	{
		Matrix4x4 expected =
			CameraTransforms.CreateView(DemoScene.CameraTransforms);

		Matrix4x4 actual = new CameraController().CreateView();

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void CreateView_PlacesTheOriginThreeUnitsInFront()
	{
		CameraController camera = new();

		Vector3 origin = Vector3.Transform(Vector3.Zero, camera.CreateView());

		Assert.Equal(0.0f, origin.X, 5);
		Assert.Equal(0.0f, origin.Y, 5);
		Assert.Equal(-3.0f, origin.Z, 5);
	}

	[Fact]
	public void Basis_IsOrthonormal()
	{
		CameraController camera = new();

		Look(camera, 0.6f, 0.35f);

		Vector3 forward = camera.Forward;
		Vector3 right = camera.Right;

		Assert.Equal(1.0f, forward.Length(), 5);
		Assert.Equal(1.0f, right.Length(), 5);

		Assert.Equal(0.0f, Vector3.Dot(forward, right), 5);
		Assert.Equal(0.35f, MathF.Asin(forward.Y), 4);

		// The camera looks toward -Z at the default orientation, so
		// the conventional right vector is world +X.
		Assert.Equal(1.0f, new CameraController().Right.X, 5);
	}

	[Fact]
	public void Move_ForwardFollowsTheViewDirection()
	{
		CameraController camera = new();

		camera.Move(Forward(), 0.1f);

		Assert.Equal(0.0f, camera.Position.X, 5);
		Assert.Equal(0.0f, camera.Position.Y, 5);
		Assert.Equal(3.0f - 0.25f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_BackwardMovesOppositeTheView()
	{
		CameraController camera = new();

		camera.Move(Input(backward: true), 0.1f);

		Assert.Equal(3.0f + 0.25f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_StrafeFollowsTheRightVector()
	{
		CameraController right = new();
		CameraController left = new();

		right.Move(Input(strafeRight: true), 0.1f);
		left.Move(Input(strafeLeft: true), 0.1f);

		Assert.Equal(0.25f, right.Position.X, 5);
		Assert.Equal(3.0f, right.Position.Z, 5);

		Assert.Equal(-0.25f, left.Position.X, 5);
		Assert.Equal(3.0f, left.Position.Z, 5);
	}

	[Fact]
	public void Move_VerticalAxesUseWorldUp()
	{
		CameraController up = new();
		CameraController down = new();

		Look(up, 0.4f, 0.5f);
		Look(down, -0.9f, -0.2f);

		up.Move(Input(up: true), 0.1f);
		down.Move(Input(down: true), 0.1f);

		Assert.Equal(0.25f, up.Position.Y, 5);
		Assert.Equal(0.0f, up.Position.X, 5);
		Assert.Equal(3.0f, up.Position.Z, 5);

		Assert.Equal(-0.25f, down.Position.Y, 5);
	}

	[Fact]
	public void Move_StrafeStaysHorizontalWhenLookingUp()
	{
		CameraController camera = new();

		Look(camera, 0.0f, 1.2f);

		camera.Move(Input(strafeRight: true), 0.1f);

		Assert.Equal(0.25f, camera.Position.X, 5);
		Assert.Equal(0.0f, camera.Position.Y, 5);
		Assert.Equal(3.0f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_NormalizesSimultaneousAxes()
	{
		CameraController diagonal = new();
		CameraController single = new();

		diagonal.Move(
			Input(forward: true, strafeRight: true),
			0.1f);

		single.Move(Forward(), 0.1f);

		float diagonalDistance =
			(diagonal.Position - new Vector3(0.0f, 0.0f, 3.0f)).Length();

		float singleDistance =
			(single.Position - new Vector3(0.0f, 0.0f, 3.0f)).Length();

		Assert.Equal(0.25f, diagonalDistance, 5);
		Assert.Equal(singleDistance, diagonalDistance, 5);

		// A normalized diagonal is 45 degrees to both axes.
		Assert.Equal(
			MathF.Sqrt(0.5f) * 0.25f,
			diagonal.Position.X,
			5);

		Assert.Equal(-MathF.Sqrt(0.5f) * 0.25f, diagonal.Position.Z - 3.0f, 5);
	}

	[Fact]
	public void Move_SprintingMultipliesTheDistance()
	{
		CameraController camera = new();

		camera.Move(Input(forward: true, sprint: true), 0.1f);

		Assert.Equal(
			3.0f -
				(CameraController.DefaultMoveSpeed *
					CameraController.DefaultSprintMultiplier *
					0.1f),
			camera.Position.Z,
			5);
	}

	[Fact]
	public void Move_DependsOnElapsedTime()
	{
		CameraController shortFrame = new();
		CameraController longFrame = new();

		shortFrame.Move(Forward(), 0.02f);
		longFrame.Move(Forward(), 0.04f);

		float shortDistance = 3.0f - shortFrame.Position.Z;
		float longDistance = 3.0f - longFrame.Position.Z;

		Assert.Equal(2.0f, longDistance / shortDistance, 5);
		Assert.Equal(0.05f, shortDistance, 5);
	}

	[Fact]
	public void Move_ZeroElapsedTimeDoesNotMove()
	{
		CameraController camera = new();

		camera.Move(Input(forward: true, strafeRight: true), 0.0f);

		Assert.Equal(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
	}

	[Fact]
	public void Move_ClampsPauseSizedDeltas()
	{
		CameraController camera = new();

		camera.Move(Forward(), 30.0f);

		Assert.Equal(
			3.0f -
				(CameraController.DefaultMoveSpeed *
					CameraController.MaxIntegrationStepSeconds),
			camera.Position.Z,
			5);
	}

	[Fact]
	public void Move_IgnoresNonFiniteElapsedTime()
	{
		CameraController camera = new();

		camera.Move(Forward(), float.NaN);
		camera.Move(Forward(), float.PositiveInfinity);
		camera.Move(Forward(), float.NegativeInfinity);

		Assert.Equal(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
	}

	[Fact]
	public void Move_WithoutAxesDoesNotMove()
	{
		CameraController camera = new();

		camera.Move(CameraMovementInput.None, 0.5f);

		Assert.Equal(new Vector3(0.0f, 0.0f, 3.0f), camera.Position);
	}

	[Fact]
	public void Move_ForwardFollowsYaw()
	{
		CameraController camera = new();

		// Positive yaw turns left, so forward points toward -X.
		Look(camera, MathF.PI / 2.0f, 0.0f);

		camera.Move(Forward(), 0.1f);

		Assert.Equal(-0.25f, camera.Position.X, 4);
		Assert.Equal(0.0f, camera.Position.Y, 4);
		Assert.Equal(3.0f, camera.Position.Z, 4);
	}

	[Fact]
	public void Move_ForwardIncludesPitch()
	{
		CameraController camera = new();

		Look(camera, 0.0f, MathF.PI / 6.0f);

		camera.Move(Forward(), 0.1f);

		Assert.Equal(0.125f, camera.Position.Y, 4);
		Assert.Equal(
			3.0f - (0.25f * MathF.Cos(MathF.PI / 6.0f)),
			camera.Position.Z,
			4);
	}

	[Fact]
	public void ApplyLook_TurnsRightForPositiveHorizontalDelta()
	{
		CameraController camera = new();

		camera.ApplyLook(100.0f, 0.0f);

		Assert.True(camera.Yaw < 0.0f);
		Assert.Equal(100.0f * camera.MouseSensitivity, -camera.Yaw, 5);
		Assert.True(camera.Forward.X > 0.0f);
	}

	[Fact]
	public void ApplyLook_TurnsLeftForNegativeHorizontalDelta()
	{
		CameraController camera = new();

		camera.ApplyLook(-100.0f, 0.0f);

		Assert.True(camera.Yaw > 0.0f);
		Assert.True(camera.Forward.X < 0.0f);
	}

	[Fact]
	public void ApplyLook_LooksUpForNegativeVerticalDelta()
	{
		CameraController camera = new();

		camera.ApplyLook(0.0f, -100.0f);

		Assert.True(camera.Pitch > 0.0f);
		Assert.True(camera.Forward.Y > 0.0f);
	}

	[Fact]
	public void ApplyLook_LooksDownForPositiveVerticalDelta()
	{
		CameraController camera = new();

		camera.ApplyLook(0.0f, 100.0f);

		Assert.True(camera.Pitch < 0.0f);
		Assert.True(camera.Forward.Y < 0.0f);
	}

	[Fact]
	public void ApplyLook_ClampsPitchJustShortOfVertical()
	{
		CameraController up = new();
		CameraController down = new();

		up.ApplyLook(0.0f, -100_000.0f);
		down.ApplyLook(0.0f, 100_000.0f);

		Assert.Equal(CameraController.MaxPitchRadians, up.Pitch, 6);
		Assert.Equal(-CameraController.MaxPitchRadians, down.Pitch, 6);
		Assert.True(CameraController.MaxPitchRadians < MathF.PI / 2.0f);

		AssertFinite(up.CreateView());
		AssertFinite(down.CreateView());
	}

	[Fact]
	public void ApplyLook_KeepsYawContinuous()
	{
		CameraController camera = new();
		const float pixelsPerStep = 5.0f;
		const int steps = 1000;

		for (int step = 0; step < steps; step++)
		{
			camera.ApplyLook(pixelsPerStep, 0.0f);
		}

		float expectedYaw =
			-(pixelsPerStep * camera.MouseSensitivity * steps);

		// More than one full turn: yaw is accumulated, not wrapped.
		Assert.True(MathF.Abs(expectedYaw) > MathF.Tau);
		Assert.Equal(expectedYaw, camera.Yaw, 3);
		Assert.False(float.IsNaN(camera.Forward.X));
		Assert.Equal(1.0f, camera.Forward.Length(), 4);
	}

	[Fact]
	public void ApplyLook_DoesNotAccumulateRotationDrift()
	{
		CameraController camera = new();

		// Deltas stay small enough that pitch never reaches its clamp,
		// so the reverse pass must cancel the forward pass exactly.
		for (int step = 0; step < 1000; step++)
		{
			camera.ApplyLook(0.4f, 0.2f);
		}

		for (int step = 0; step < 1000; step++)
		{
			camera.ApplyLook(-0.4f, -0.2f);
		}

		Assert.Equal(0.0f, camera.Yaw, 4);
		Assert.Equal(0.0f, camera.Pitch, 4);

		Vector4 expected = Vector4.Transform(
			new Vector3(1.0f, 2.0f, 3.0f),
			CameraTransforms.CreateView(DemoScene.CameraTransforms));

		Vector4 actual = Vector4.Transform(
			new Vector3(1.0f, 2.0f, 3.0f),
			camera.CreateView());

		Assert.True(Vector4.Distance(expected, actual) < 1e-5f);
	}

	[Fact]
	public void ApplyLook_IgnoresNonFiniteDeltas()
	{
		CameraController camera = new();

		camera.ApplyLook(float.NaN, 0.0f);
		camera.ApplyLook(0.0f, float.PositiveInfinity);
		camera.ApplyLook(float.NegativeInfinity, float.NaN);

		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
	}

	private static CameraMovementInput Forward()
	{
		return CameraMovementInput.ForwardOnly;
	}

	private static CameraMovementInput Input(
		bool forward = false,
		bool backward = false,
		bool strafeLeft = false,
		bool strafeRight = false,
		bool up = false,
		bool down = false,
		bool sprint = false)
	{
		return new CameraMovementInput(
			forward,
			backward,
			strafeLeft,
			strafeRight,
			up,
			down,
			sprint);
	}

	/// <summary>
	/// Produces the requested orientation through the same mouse-delta
	/// path as interactive input.
	/// </summary>
	private static void Look(
		CameraController camera,
		float yawRadians,
		float pitchRadians)
	{
		camera.ApplyLook(
			-yawRadians / camera.MouseSensitivity,
			-pitchRadians / camera.MouseSensitivity);
	}

	private static void AssertFinite(Matrix4x4 matrix)
	{
		float[] values =
		[
			matrix.M11,
			matrix.M12,
			matrix.M13,
			matrix.M14,
			matrix.M21,
			matrix.M22,
			matrix.M23,
			matrix.M24,
			matrix.M31,
			matrix.M32,
			matrix.M33,
			matrix.M34,
			matrix.M41,
			matrix.M42,
			matrix.M43,
			matrix.M44
		];

		Assert.All(values, value => Assert.True(float.IsFinite(value)));
	}
}
