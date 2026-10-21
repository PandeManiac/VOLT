using System.Numerics;
using SDL3;
using Volt.Platform.Input;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo.Tests;

/// <summary>
/// Covers the migration seam: engine input frames in, camera motion
/// and gameplay commands out.
/// </summary>
/// <remarks>
/// The frames come from the real translator and the real platform
/// accumulator through <see cref="PlatformInputHarness"/>, so these
/// tests fail if the platform stops reporting an edge the sample
/// depends on or starts reporting one it must ignore. No native
/// window, capture call, or desktop input is involved.
/// </remarks>
public sealed class CameraInputTests
{
	private static readonly Vector3 InitialPosition = new(0, 1.7f, 8);

	private const float Step = 1.0f / 60.0f;

	[Fact]
	public void Update_FirstClickCapturesWithoutFiring()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.True(frame.KeyboardFocused);
		Assert.False(frame.FireRequested);
		Assert.Equal(TargetRangeCommand.None, TargetRangeCommand.FromInput(frame));
	}

	[Fact]
	public void Update_FreshClickWhileCapturedFiresOnce()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		input.Poll(TestInput.MouseUp());
		input.Update(camera);

		input.Poll(TestInput.MouseDown());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.FireRequested);
		Assert.Equal(
			TargetRangeCommand.FireOnly,
			TargetRangeCommand.FromInput(frame));
	}

	[Fact]
	public void Update_ShortClickCapturesWithoutFiring()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		// The button goes down and up between two polls, so the frame
		// reports a press edge and no held button.
		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.False(frame.FireRequested);
		Assert.Equal(1, input.Native.EnableCalls);

		// The consumed click cannot capture or fire again.
		input.Poll();

		Assert.False(input.Update(camera).FireRequested);
		Assert.True(input.IsCaptured);
		Assert.Equal(1, input.Native.EnableCalls);
	}

	[Fact]
	public void Update_ShortClickWhileCapturedFiresExactlyOnce()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());
		input.Update(camera);

		Assert.True(input.IsCaptured);

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());

		Assert.True(input.Update(camera).FireRequested);

		for (int frame = 0; frame < 3; frame++)
		{
			input.Poll();

			Assert.False(input.Update(camera).FireRequested);
			Assert.True(input.IsCaptured);
			Assert.Equal(0, input.Native.DisableCalls);
		}
	}

	[Fact]
	public void Update_ShortClickWithEscapeHeldDoesNothing()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(
			TestInput.KeyDown(SDL.Scancode.Escape),
			TestInput.MouseDown(),
			TestInput.MouseUp());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.False(input.IsCaptured);
		Assert.False(frame.FireRequested);
		Assert.Equal(0, input.Native.EnableCalls);
	}

	[Fact]
	public void Update_ShortClickWithoutKeyboardFocusDoesNothing()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.PollFrame(
			[TestInput.MouseDown(), TestInput.MouseUp()],
			keyboardFocused: false);

		CameraInputSnapshot frame = input.Update(camera);

		Assert.False(input.IsCaptured);
		Assert.False(frame.FireRequested);
		Assert.Equal(0, input.Native.EnableCalls);
	}

	[Fact]
	public void Update_HeldButtonDoesNotRepeatFire()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		input.Poll(TestInput.MouseUp());
		input.Update(camera);

		input.Poll(TestInput.MouseDown());

		Assert.True(input.Update(camera).FireRequested);

		for (int frame = 0; frame < 5; frame++)
		{
			input.Poll();

			Assert.False(input.Update(camera).FireRequested);
		}
	}

	[Fact]
	public void Update_HeldEscapeBlocksRecaptureAndFire()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		Assert.True(input.IsCaptured);

		input.Poll(
			TestInput.KeyDown(SDL.Scancode.Escape),
			TestInput.MouseUp());

		CameraInputSnapshot released = input.Update(camera);

		Assert.False(input.IsCaptured);
		Assert.False(released.FireRequested);

		for (int frame = 0; frame < 4; frame++)
		{
			input.Poll(
				TestInput.KeyDown(SDL.Scancode.Escape),
				frame % 2 == 0 ? TestInput.MouseUp() : TestInput.MouseDown());

			CameraInputSnapshot held = input.Update(camera);

			Assert.False(input.IsCaptured);
			Assert.False(held.FireRequested);
		}
	}

	[Fact]
	public void Update_ReleasingEscapeRestoresClickToCapture()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		input.Poll(
			TestInput.KeyDown(SDL.Scancode.Escape),
			TestInput.MouseUp());
		input.Update(camera);

		input.Poll(
			TestInput.KeyUp(SDL.Scancode.Escape),
			TestInput.MouseUp());
		input.Update(camera);

		input.Poll(
			TestInput.KeyUp(SDL.Scancode.Escape),
			TestInput.MouseDown());
		CameraInputSnapshot recaptured = input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.False(recaptured.FireRequested);
	}

	[Fact]
	public void Update_CaptureDiscardsPendingRelativeMotion()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		Assert.Equal(1, input.Native.DiscardCalls);
	}

	[Fact]
	public void Update_CaptureFrameDoesNotRotateTheCamera()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(
			TestInput.Motion(640, 360, 240, -90),
			TestInput.MouseDown());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
		Assert.False(frame.FireRequested);
	}

	[Fact]
	public void Update_CapturedMotionRotatesTheCamera()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		input.Poll(
			TestInput.MouseUp(),
			TestInput.Motion(640, 360, 100, -50));

		input.Update(camera);

		Assert.Equal(-100.0f * camera.MouseSensitivity, camera.Yaw, 5);
		Assert.Equal(50.0f * camera.MouseSensitivity, camera.Pitch, 5);
	}

	[Fact]
	public void Update_UncapturedMotionDoesNotRotateTheCamera()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.Motion(640, 360, 300, 120));
		input.Update(camera);

		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
	}

	[Fact]
	public void Update_WasdAndVerticalKeysMapToMovement()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(
			TestInput.KeyDown(SDL.Scancode.W),
			TestInput.KeyDown(SDL.Scancode.A),
			TestInput.KeyDown(SDL.Scancode.Space),
			TestInput.KeyDown(SDL.Scancode.LShift));

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.Movement.Forward);
		Assert.False(frame.Movement.Backward);
		Assert.True(frame.Movement.StrafeLeft);
		Assert.False(frame.Movement.StrafeRight);
		Assert.True(frame.Movement.Up);
		Assert.False(frame.Movement.Down);
		Assert.True(frame.Movement.Sprint);
	}

	[Fact]
	public void Update_LeftControlDescends()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.KeyDown(SDL.Scancode.LCtrl));

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.Movement.Down);
		Assert.False(frame.Movement.Up);
		Assert.False(frame.Movement.Sprint);
	}

	[Fact]
	public void Update_RightShiftAndRightControlAreNotMapped()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(
			TestInput.KeyDown(SDL.Scancode.RShift),
			TestInput.KeyDown(SDL.Scancode.RCtrl));

		CameraInputSnapshot frame = input.Update(camera);

		Assert.False(frame.Movement.Sprint);
		Assert.False(frame.Movement.Down);
	}

	[Fact]
	public void Update_RestartIsAOneShotEdgeOnlyWhileFocused()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.PollFrame(
			[TestInput.KeyDown(SDL.Scancode.R)],
			keyboardFocused: false);

		Assert.False(input.Update(camera).RestartRequested);

		input.Poll(TestInput.KeyDown(SDL.Scancode.R));

		Assert.True(input.Update(camera).RestartRequested);

		input.Poll();

		Assert.False(input.Update(camera).RestartRequested);
	}

	[Fact]
	public void Update_FocusLossReleasesAndSuppressesFiring()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		Assert.True(input.IsCaptured);

		input.PollFrame(
			[TestInput.MouseUp()],
			keyboardFocused: false);

		CameraInputSnapshot unfocused = input.Update(camera);

		Assert.False(unfocused.KeyboardFocused);
		Assert.False(unfocused.FireRequested);
		Assert.False(input.IsCaptured);

		input.PollFrame(
			[TestInput.MouseDown()],
			keyboardFocused: false);

		Assert.False(input.Update(camera).FireRequested);
	}

	[Fact]
	public void Update_TransientFocusLossReleasesCapture()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());
		input.Update(camera);

		Assert.True(input.IsCaptured);

		// Focus leaves and returns inside one poll, so the frame itself
		// still reports keyboard focus; only the per-frame loss flag can
		// tell the adapter that the pointer must be released.
		input.PollFrame(
			[PlatformInputHarness.FocusLost()],
			keyboardFocused: true);

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.KeyboardFocused);
		Assert.True(input.Snapshot.FocusLostDuringFrame);
		Assert.False(input.IsCaptured);
		Assert.False(input.Native.Enabled);
		Assert.Equal(1, input.Native.DisableCalls);

		input.Poll();
		input.Update(camera);

		Assert.False(input.IsCaptured);
		Assert.Equal(1, input.Native.DisableCalls);
	}

	[Fact]
	public void Update_TransientFocusLossDoesNotRotateTheCamera()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());
		input.Update(camera);

		input.PollFrame(
			[
				PlatformInputHarness.FocusLost(),
				TestInput.Motion(640, 360, 120, -80)
			],
			keyboardFocused: true);

		input.Update(camera);

		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
		Assert.False(input.IsCaptured);

		// Looking does not resume until the user captures the pointer
		// again with a fresh click.
		input.Poll(TestInput.Motion(640, 360, 60, 20));
		input.Update(camera);

		Assert.Equal(0.0f, camera.Yaw);
		Assert.Equal(0.0f, camera.Pitch);
	}

	[Fact]
	public void Update_TransientFocusLossSuppressesQuickClickAndRestart()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.PollFrame(
			[
				PlatformInputHarness.FocusLost(),
				TestInput.MouseDown(),
				TestInput.MouseUp(),
				TestInput.KeyDown(SDL.Scancode.R)
			],
			keyboardFocused: true);

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.KeyboardFocused);
		Assert.False(frame.FireRequested);
		Assert.False(frame.RestartRequested);
		Assert.False(input.IsCaptured);
		Assert.Equal(0, input.Native.EnableCalls);
	}

	[Fact]
	public void Update_TransientFocusLossRequiresANewClickToRecapture()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());
		input.Update(camera);

		input.PollFrame(
			[PlatformInputHarness.FocusLost()],
			keyboardFocused: true);
		input.Update(camera);

		for (int quietFrame = 0; quietFrame < 3; quietFrame++)
		{
			input.Poll();
			input.Update(camera);
		}

		Assert.False(input.IsCaptured);
		Assert.Equal(1, input.Native.EnableCalls);

		input.Poll(TestInput.MouseDown(), TestInput.MouseUp());

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.False(frame.FireRequested);
		Assert.Equal(2, input.Native.EnableCalls);
	}

	[Fact]
	public void Update_PointerLeavingTheWindowReleasesCapture()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		Assert.True(input.IsCaptured);
		Assert.Equal(1, input.Native.EnableCalls);

		input.PollFrame(
			[],
			mouseFocus: MouseFocusState.OtherWindow);

		CameraInputSnapshot frame = input.Update(camera);

		Assert.False(frame.FireRequested);

		input.PollFrame(
			[],
			mouseFocus: MouseFocusState.OtherWindow);

		input.Update(camera);

		Assert.False(input.IsCaptured);
		Assert.Equal(1, input.Native.DisableCalls);
	}

	[Fact]
	public void Dispose_ReleasesACapturedPointer()
	{
		TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		Assert.True(input.IsCaptured);

		input.Dispose();

		Assert.False(input.IsCaptured);
		Assert.Equal(1, input.Native.DisableCalls);
		Assert.False(input.Native.Enabled);
	}

	[Fact]
	public void Update_EmptyFrameReportsNothing()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll();

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.KeyboardFocused);
		Assert.False(frame.FireRequested);
		Assert.False(frame.RestartRequested);
		Assert.Equal(CameraMovementInput.None, frame.Movement);
	}

	[Fact]
	public void Update_RestartWinsOverFireOnTheSameFrame()
	{
		using TestInput input = new();
		CameraController camera = CreateCamera();

		input.Poll(TestInput.MouseDown());
		input.Update(camera);

		input.Poll(TestInput.MouseUp());
		input.Update(camera);

		input.Poll(
			TestInput.MouseDown(),
			TestInput.KeyDown(SDL.Scancode.R));

		CameraInputSnapshot frame = input.Update(camera);

		Assert.True(frame.FireRequested);
		Assert.True(frame.RestartRequested);
		Assert.Equal(
			TargetRangeCommand.RestartOnly,
			TargetRangeCommand.FromInput(frame));
	}

	private static CameraController CreateCamera()
	{
		return new CameraController(InitialPosition);
	}

	/// <summary>
	/// Bundles the synthetic engine input and the sample adapter so a
	/// test can drive frames and read the result from one object.
	/// </summary>
	private sealed class TestInput : IDisposable
	{
		private readonly PlatformInputHarness _harness = new();
		private readonly CameraInput _adapter;

		public TestInput()
		{
			_adapter = new CameraInput(_harness.Input);
		}

		public static SDL.Event KeyDown(SDL.Scancode scancode)
		{
			return PlatformInputHarness.KeyDown(scancode);
		}

		public static SDL.Event KeyUp(SDL.Scancode scancode)
		{
			return PlatformInputHarness.KeyUp(scancode);
		}

		public static SDL.Event MouseDown()
		{
			return PlatformInputHarness.MouseDown();
		}

		public static SDL.Event MouseUp()
		{
			return PlatformInputHarness.MouseUp();
		}

		public static SDL.Event Motion(
			float x,
			float y,
			float xRelative,
			float yRelative)
		{
			return PlatformInputHarness.Motion(
				x,
				y,
				xRelative,
				yRelative);
		}

		public FakeRelativeMouseMode Native => _harness.Native;

		public InputSnapshot Snapshot => _harness.Input.Snapshot;

		public bool IsCaptured => _adapter.IsCaptured;

		public void Poll(params SDL.Event[] events)
		{
			_harness.Poll(events);
		}

		public void PollFrame(
			SDL.Event[] events,
			bool keyboardFocused = true,
			MouseFocusState mouseFocus = MouseFocusState.OwnWindow)
		{
			_harness.PollFrame(events, keyboardFocused, mouseFocus);
		}

		public CameraInputSnapshot Update(CameraController camera)
		{
			return _adapter.Update(camera, Step);
		}

		public void Dispose()
		{
			_adapter.Dispose();
		}
	}
}
