using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Xunit;

namespace Volt.Rendering.Tests.Cameras;

public sealed class CameraControllerTests
{
	private static readonly Vector3 Start = new(0.0f, 0.0f, 3.0f);

	private static CameraController CreateCamera()
	{
		return new CameraController(Start);
	}

	[Fact]
	public void Constructor_StartsAtTheSuppliedPose()
	{
		CameraController camera = CreateCamera();

		Assert.Equal(Start, camera.Position);
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
	public void Constructor_DoesNotHardCodeAPose()
	{
		Vector3 position = new(7.0f, -2.0f, 1.5f);

		CameraController camera = new(position);

		Assert.Equal(position, camera.Position);

		Vector3 origin = Vector3.Transform(
			Vector3.Zero,
			camera.CreateView());

		AssertVectorNear(-position, origin);
	}

	[Fact]
	public void CreateView_MatchesTheFixedLookAtPoseExactly()
	{
		Matrix4x4 expected = Matrix4x4.CreateLookAt(
			Start,
			Vector3.Zero,
			Vector3.UnitY);

		Matrix4x4 actual = CreateCamera().CreateView();

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void CreateView_MatchesTheEcsViewForTheSamePose()
	{
		CameraController camera = CreateCamera();

		Look(camera, 0.6f, -0.25f);

		camera.Move(CameraMovementInput.ForwardOnly, 0.4f);

		PositionComponent position = new(camera.Position);
		RotationComponent rotation = new(camera.CreateRotation());

		Matrix4x4 expected = CameraComponent.CreateViewMatrix(
			in position,
			in rotation);

		Assert.Equal(expected, camera.CreateView());
	}

	[Fact]
	public void CreateView_AgreesWithALookAtBasis()
	{
		CameraController camera = CreateCamera();

		Look(camera, 0.75f, 0.4f);

		Matrix4x4 expected = Matrix4x4.CreateLookAt(
			camera.Position,
			camera.Position + camera.Forward,
			Vector3.UnitY);

		Matrix4x4 actual = camera.CreateView();

		AssertMatrixNear(expected, actual);
	}

	[Fact]
	public void CreateView_PlacesTheOriginThreeUnitsInFront()
	{
		Vector3 origin = Vector3.Transform(
			Vector3.Zero,
			CreateCamera().CreateView());

		Assert.Equal(0.0f, origin.X, 5);
		Assert.Equal(0.0f, origin.Y, 5);
		Assert.Equal(-3.0f, origin.Z, 5);
	}

	[Fact]
	public void CreateRotation_ZeroYawAndPitchIsIdentity()
	{
		CameraController camera = CreateCamera();

		Quaternion rotation = camera.CreateRotation();

		Assert.Equal(Quaternion.Identity, rotation);
	}

	[Fact]
	public void CreateRotation_PositiveYawTurnsLeft()
	{
		CameraController camera = CreateCamera();

		Look(camera, MathF.PI / 2.0f, 0.0f);

		Quaternion rotation = camera.CreateRotation();

		// At +90 degrees yaw the camera looks down -X.
		Vector3 forward = Vector3.Transform(
			-Vector3.UnitZ,
			Matrix4x4.CreateFromQuaternion(rotation));

		AssertVectorNear(-Vector3.UnitX, forward);
	}

	[Fact]
	public void CreateRotation_PositivePitchLooksUp()
	{
		CameraController camera = CreateCamera();

		Look(camera, 0.0f, MathF.PI / 6.0f);

		Quaternion rotation = camera.CreateRotation();

		Vector3 forward = Vector3.Transform(
			-Vector3.UnitZ,
			Matrix4x4.CreateFromQuaternion(rotation));

		AssertVectorNear(
			new Vector3(0.0f, 0.5f, -MathF.Cos(MathF.PI / 6.0f)),
			forward);
	}

	[Fact]
	public void CreateRotation_MatchesForwardForAnAsymmetricPose()
	{
		CameraController camera = new(
			new Vector3(2.0f, -1.0f, 4.0f));

		Look(camera, 0.7f, -0.35f);

		Quaternion rotation = camera.CreateRotation();

		Vector3 forward = Vector3.Transform(
			-Vector3.UnitZ,
			Matrix4x4.CreateFromQuaternion(rotation));

		AssertVectorNear(camera.Forward, forward);

		// No roll: the rotated up vector stays in the X/Y plane of
		// the camera basis, so its horizontal component is parallel
		// to Right.
		Vector3 up = Vector3.Transform(
			Vector3.UnitY,
			Matrix4x4.CreateFromQuaternion(rotation));

		Assert.Equal(0.0f, Vector3.Dot(up, camera.Right), 5);
	}

	[Fact]
	public void CreateRotation_IsNormalized()
	{
		CameraController camera = CreateCamera();

		Look(camera, 12.0f, -1.2f);

		Quaternion rotation = camera.CreateRotation();

		Assert.Equal(1.0f, rotation.Length(), 6);
		Assert.True(rotation.LengthSquared() > 0.0f);
	}

	[Fact]
	public void CreateRotation_TracksYawChanges()
	{
		CameraController camera = CreateCamera();

		Quaternion first = camera.CreateRotation();

		camera.ApplyLook(120.0f, 0.0f);

		Quaternion second = camera.CreateRotation();

		Assert.NotEqual(first, second);
		Assert.Equal(0.0f, camera.Pitch);
		Assert.True(camera.Yaw < 0.0f);
	}

	[Fact]
	public void CreateRotation_TracksPitchChanges()
	{
		CameraController camera = CreateCamera();

		Quaternion first = camera.CreateRotation();

		camera.ApplyLook(0.0f, -60.0f);

		Quaternion second = camera.CreateRotation();

		Assert.NotEqual(first, second);
		Assert.Equal(0.0f, camera.Yaw);
		Assert.True(camera.Pitch > 0.0f);
	}

	[Fact]
	public void Basis_IsOrthonormal()
	{
		CameraController camera = CreateCamera();

		Look(camera, 0.6f, 0.35f);

		Vector3 forward = camera.Forward;
		Vector3 right = camera.Right;

		Assert.Equal(1.0f, forward.Length(), 5);
		Assert.Equal(1.0f, right.Length(), 5);
		Assert.Equal(0.0f, Vector3.Dot(forward, right), 5);
		Assert.Equal(0.35f, MathF.Asin(forward.Y), 4);

		// The camera looks toward -Z at the default orientation, so
		// the conventional right vector is world +X.
		Assert.Equal(1.0f, CreateCamera().Right.X, 5);
	}

	[Fact]
	public void Move_ForwardFollowsTheViewDirection()
	{
		CameraController camera = CreateCamera();

		camera.Move(CameraMovementInput.ForwardOnly, 0.1f);

		Assert.Equal(0.0f, camera.Position.X, 5);
		Assert.Equal(0.0f, camera.Position.Y, 5);
		Assert.Equal(3.0f - 0.25f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_BackwardMovesOppositeTheView()
	{
		CameraController camera = CreateCamera();

		camera.Move(Input(backward: true), 0.1f);

		Assert.Equal(3.0f + 0.25f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_StrafeFollowsTheRightVector()
	{
		CameraController right = CreateCamera();
		CameraController left = CreateCamera();

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
		CameraController up = CreateCamera();
		CameraController down = CreateCamera();

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
		CameraController camera = CreateCamera();

		Look(camera, 0.0f, 1.2f);

		camera.Move(Input(strafeRight: true), 0.1f);

		Assert.Equal(0.25f, camera.Position.X, 5);
		Assert.Equal(0.0f, camera.Position.Y, 5);
		Assert.Equal(3.0f, camera.Position.Z, 5);
	}

	[Fact]
	public void Move_NormalizesSimultaneousAxes()
	{
		CameraController diagonal = CreateCamera();
		CameraController single = CreateCamera();

		diagonal.Move(
			Input(forward: true, strafeRight: true),
			0.1f);

		single.Move(CameraMovementInput.ForwardOnly, 0.1f);

		float diagonalDistance =
			(diagonal.Position - Start).Length();

		float singleDistance =
			(single.Position - Start).Length();

		Assert.Equal(0.25f, diagonalDistance, 5);
		Assert.Equal(singleDistance, diagonalDistance, 5);

		// A normalized diagonal is 45 degrees to both axes.
		Assert.Equal(
			MathF.Sqrt(0.5f) * 0.25f,
			diagonal.Position.X,
			5);

		Assert.Equal(
			-MathF.Sqrt(0.5f) * 0.25f,
			diagonal.Position.Z - 3.0f,
			5);
	}

	[Fact]
	public void Move_SprintingMultipliesTheDistance()
	{
		CameraController camera = CreateCamera();

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
		CameraController shortFrame = CreateCamera();
		CameraController longFrame = CreateCamera();

		shortFrame.Move(CameraMovementInput.ForwardOnly, 0.02f);
		longFrame.Move(CameraMovementInput.ForwardOnly, 0.04f);

		float shortDistance = 3.0f - shortFrame.Position.Z;
		float longDistance = 3.0f - longFrame.Position.Z;

		Assert.Equal(2.0f, longDistance / shortDistance, 5);
		Assert.Equal(0.05f, shortDistance, 5);
	}

	[Fact]
	public void Move_IsIndependentOfFrameSubdivision()
	{
		CameraController oneStep = CreateCamera();
		CameraController manySteps = CreateCamera();

		oneStep.Move(CameraMovementInput.ForwardOnly, 0.1f);

		for (int step = 0; step < 10; step++)
		{
			manySteps.Move(CameraMovementInput.ForwardOnly, 0.01f);
		}

		Assert.Equal(
			oneStep.Position.Z,
			manySteps.Position.Z,
			4);
	}

	[Fact]
	public void Move_ZeroElapsedTimeDoesNotMove()
	{
		CameraController camera = CreateCamera();

		camera.Move(
			Input(forward: true, strafeRight: true),
			0.0f);

		Assert.Equal(Start, camera.Position);
	}

	[Fact]
	public void Move_ClampsPauseSizedDeltas()
	{
		CameraController camera = CreateCamera();

		camera.Move(CameraMovementInput.ForwardOnly, 30.0f);

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
		CameraController camera = CreateCamera();

		camera.Move(CameraMovementInput.ForwardOnly, float.NaN);
		camera.Move(
			CameraMovementInput.ForwardOnly,
			float.PositiveInfinity);

		camera.Move(
			CameraMovementInput.ForwardOnly,
			float.NegativeInfinity);

		Assert.Equal(Start, camera.Position);
	}

	[Fact]
	public void Move_WithoutAxesDoesNotMove()
	{
		CameraController camera = CreateCamera();

		camera.Move(CameraMovementInput.None, 0.5f);

		Assert.Equal(Start, camera.Position);
	}

	[Fact]
	public void Move_ForwardFollowsYaw()
	{
		CameraController camera = CreateCamera();

		// Positive yaw turns left, so forward points toward -X.
		Look(camera, MathF.PI / 2.0f, 0.0f);

		camera.Move(CameraMovementInput.ForwardOnly, 0.1f);

		Assert.Equal(-0.25f, camera.Position.X, 4);
		Assert.Equal(0.0f, camera.Position.Y, 4);
		Assert.Equal(3.0f, camera.Position.Z, 4);
	}

	[Fact]
	public void Move_ForwardIncludesPitch()
	{
		CameraController camera = CreateCamera();

		Look(camera, 0.0f, MathF.PI / 6.0f);

		camera.Move(CameraMovementInput.ForwardOnly, 0.1f);

		Assert.Equal(0.125f, camera.Position.Y, 4);
		Assert.Equal(
			3.0f - (0.25f * MathF.Cos(MathF.PI / 6.0f)),
			camera.Position.Z,
			4);
	}

	[Fact]
	public void ApplyLook_TurnsRightForPositiveHorizontalDelta()
	{
		CameraController camera = CreateCamera();

		camera.ApplyLook(100.0f, 0.0f);

		Assert.True(camera.Yaw < 0.0f);
		Assert.Equal(100.0f * camera.MouseSensitivity, -camera.Yaw, 5);
		Assert.True(camera.Forward.X > 0.0f);
	}

	[Fact]
	public void ApplyLook_TurnsLeftForNegativeHorizontalDelta()
	{
		CameraController camera = CreateCamera();

		camera.ApplyLook(-100.0f, 0.0f);

		Assert.True(camera.Yaw > 0.0f);
		Assert.True(camera.Forward.X < 0.0f);
	}

	[Fact]
	public void ApplyLook_LooksUpForNegativeVerticalDelta()
	{
		CameraController camera = CreateCamera();

		camera.ApplyLook(0.0f, -100.0f);

		Assert.True(camera.Pitch > 0.0f);
		Assert.True(camera.Forward.Y > 0.0f);
	}

	[Fact]
	public void ApplyLook_LooksDownForPositiveVerticalDelta()
	{
		CameraController camera = CreateCamera();

		camera.ApplyLook(0.0f, 100.0f);

		Assert.True(camera.Pitch < 0.0f);
		Assert.True(camera.Forward.Y < 0.0f);
	}

	[Fact]
	public void ApplyLook_ClampsPitchJustShortOfVertical()
	{
		CameraController up = CreateCamera();
		CameraController down = CreateCamera();

		up.ApplyLook(0.0f, -100_000.0f);
		down.ApplyLook(0.0f, 100_000.0f);

		Assert.Equal(CameraController.MaxPitchRadians, up.Pitch, 6);
		Assert.Equal(-CameraController.MaxPitchRadians, down.Pitch, 6);
		Assert.True(CameraController.MaxPitchRadians < MathF.PI / 2.0f);

		AssertFinite(up.CreateView());
		AssertFinite(down.CreateView());
		Assert.Equal(1.0f, up.CreateRotation().Length(), 6);
		Assert.Equal(1.0f, down.CreateRotation().Length(), 6);
	}

	[Fact]
	public void ApplyLook_KeepsYawContinuous()
	{
		CameraController camera = CreateCamera();
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
		Assert.Equal(1.0f, camera.CreateRotation().Length(), 6);
	}

	[Fact]
	public void ApplyLook_DoesNotAccumulateRotationDrift()
	{
		CameraController camera = CreateCamera();

		// Deltas stay small enough that pitch never reaches its
		// clamp, so the reverse pass must cancel the forward pass.
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
			Matrix4x4.CreateLookAt(Start, Vector3.Zero, Vector3.UnitY));

		Vector4 actual = Vector4.Transform(
			new Vector3(1.0f, 2.0f, 3.0f),
			camera.CreateView());

		Assert.True(Vector4.Distance(expected, actual) < 1e-5f);
	}

	[Fact]
	public void ApplyLook_IgnoresNonFiniteDeltas()
	{
		CameraController camera = CreateCamera();

		camera.ApplyLook(float.NaN, 0.0f);
		camera.ApplyLook(0.0f, float.PositiveInfinity);
		camera.ApplyLook(float.NegativeInfinity, float.NaN);

		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
	}

	[Fact]
	public void FrameUpdate_DoesNotAllocate()
	{
		CameraController camera = CreateCamera();

		Vector3 position = new(1.0f, 2.0f, 3.0f);
		RotationComponent rotation = new(camera.CreateRotation());

		void Update()
		{
			camera.ApplyLook(3.0f, -2.0f);
			camera.Move(
				Input(forward: true, strafeRight: true),
				1.0f / 60.0f);

			Matrix4x4 view = camera.CreateView();

			_ = TransformForProbe(view, position, rotation);
		}

		for (int iteration = 0; iteration < 64; iteration++)
		{
			Update();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < 1024; iteration++)
		{
			Update();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	/// <summary>
	/// Keeps the per-frame rotation conversion alive so the allocation
	/// measurement covers the quaternion and component path too.
	/// </summary>
	private static float TransformForProbe(
		Matrix4x4 view,
		Vector3 position,
		RotationComponent rotation)
	{
		PositionComponent component = new(position);

		return Vector3.Transform(
			Vector3.Zero,
			CameraComponent.CreateViewMatrix(
				in component,
				in rotation) *
				view).Z;
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

	[Fact]
	public void SetPose_RestoresTheDocumentedPose()
	{
		CameraController camera = new(new Vector3(4.0f, 2.0f, -3.0f));

		camera.ApplyLook(400.0f, -120.0f);
		camera.Move(CameraMovementInput.ForwardOnly, 0.5f);

		Vector3 position = new(1.5f, 0.25f, 6.0f);

		camera.SetPose(position, 0.75f, -0.4f);

		Assert.Equal(position, camera.Position);
		Assert.Equal(0.75f, camera.Yaw, 5);
		Assert.Equal(-0.4f, camera.Pitch, 5);

		float cosPitch = MathF.Cos(-0.4f);

		AssertVectorNear(
			new Vector3(
				-MathF.Sin(0.75f) * cosPitch,
				MathF.Sin(-0.4f),
				-MathF.Cos(0.75f) * cosPitch),
			camera.Forward);
	}

	[Fact]
	public void SetPose_ReturnsToTheIdentityOrientation()
	{
		CameraController camera = new(Vector3.Zero);

		camera.ApplyLook(-900.0f, 300.0f);

		camera.SetPose(Vector3.Zero, 0.0f, 0.0f);

		Assert.Equal(Quaternion.Identity, camera.CreateRotation());
		Assert.Equal(-Vector3.UnitZ, camera.Forward);
		Assert.Equal(Matrix4x4.Identity, camera.CreateView());
	}

	[Fact]
	public void SetPose_ClampsPitchWithoutWrappingYaw()
	{
		CameraController camera = new(Vector3.Zero);

		camera.SetPose(Vector3.Zero, 9.0f, MathF.PI);

		Assert.Equal(9.0f, camera.Yaw, 5);
		Assert.Equal(CameraController.MaxPitchRadians, camera.Pitch, 5);
	}

	[Theory]
	[InlineData(float.NaN, 0.0f)]
	[InlineData(0.0f, float.PositiveInfinity)]
	[InlineData(0.0f, float.NaN)]
	public void SetPose_RejectsNonFiniteAngles(float yaw, float pitch)
	{
		CameraController camera = new(Vector3.One);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => camera.SetPose(Vector3.One, yaw, pitch));

		// The invalid call leaves the previous pose untouched.
		Assert.Equal(Vector3.One, camera.Position);
		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
	}

	[Fact]
	public void SetPose_RejectsANonFinitePosition()
	{
		CameraController camera = new(Vector3.One);

		Assert.Throws<ArgumentOutOfRangeException>(
			() => camera.SetPose(
				new Vector3(float.NaN, 0.0f, 0.0f),
				0.0f,
				0.0f));

		Assert.Equal(Vector3.One, camera.Position);
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

		Assert.All(
			values,
			value => Assert.True(float.IsFinite(value)));
	}

	private static void AssertMatrixNear(
		Matrix4x4 expected,
		Matrix4x4 actual,
		float tolerance = 0.00001f)
	{
		for (int row = 0; row < 4; row++)
		{
			for (int column = 0; column < 4; column++)
			{
				float difference = MathF.Abs(
					expected[row, column] -
						actual[row, column]);

				Assert.True(
					difference <= tolerance,
					$"Row {row}, column {column}: expected " +
						$"{expected[row, column]}, actual " +
						$"{actual[row, column]}.");
			}
		}
	}

	private static void AssertVectorNear(
		Vector3 expected,
		Vector3 actual,
		float tolerance = 0.00001f)
	{
		Assert.True(
			Vector3.Distance(expected, actual) <= tolerance,
			$"Expected {expected}, actual {actual}.");
	}
}
