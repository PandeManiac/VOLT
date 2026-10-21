using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class CameraCaptureControllerTests
{
	private static readonly CameraInputSample Idle = new(
		KeyboardFocused: true,
		PointerFocus: CameraPointerFocus.OwnWindow,
		LeftButtonPressed: false,
		EscapeDown: false,
		FocusLostDuringFrame: false);

	/// <summary>
	/// A sample carrying a fresh left press. The platform reports one
	/// press edge per physical click, so this value is a click, not a
	/// held button.
	/// </summary>
	private static readonly CameraInputSample FocusedClick = new(
		KeyboardFocused: true,
		PointerFocus: CameraPointerFocus.OwnWindow,
		LeftButtonPressed: true,
		EscapeDown: false,
		FocusLostDuringFrame: false);

	[Fact]
	public void Update_CapturesOnAClickInTheFocusedOwnWindow()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.False(transition.CaptureFailed);
		Assert.True(capture.IsCaptured);
		Assert.True(capture.IsLookActive);
		Assert.True(native.NativeEnabled);
		Assert.Equal(1, native.EnableCalls);
	}

	[Fact]
	public void Update_DiscardsPendingMotionWhenCaptureStarts()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		Assert.Equal(1, native.DiscardCalls);
	}

	[Fact]
	public void Update_DoesNotCaptureWithoutKeyboardFocus()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { KeyboardFocused = false });

		Assert.False(capture.IsCaptured);
		Assert.False(transition.CaptureFailed);
		Assert.Equal(0, native.EnableCalls);
	}

	[Fact]
	public void Update_DoesNotCaptureWhenNoWindowOwnsThePointer()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(
			FocusedClick with { PointerFocus = CameraPointerFocus.None });

		Assert.False(capture.IsCaptured);
		Assert.Equal(0, native.EnableCalls);
	}

	[Fact]
	public void Update_DoesNotCaptureWhenAnotherWindowOwnsThePointer()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with
			{
				PointerFocus = CameraPointerFocus.OtherWindow
			});

		Assert.False(capture.IsCaptured);
		Assert.False(transition.CaptureFailed);
		Assert.Equal(0, native.EnableCalls);
	}

	[Fact]
	public void Update_DoesNotCaptureWithoutAClick()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(Idle);

		Assert.False(capture.IsCaptured);
		Assert.Equal(0, native.EnableCalls);
	}

	[Fact]
	public void Update_DoesNotRecaptureWhileTheButtonStaysHeld()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		// Losing focus releases capture while the button is still held.
		capture.Update(Idle with { KeyboardFocused = false });

		Assert.False(capture.IsCaptured);

		// A held button produces no further press edge, so regaining
		// focus reports idle samples until the user clicks again.
		capture.Update(Idle);

		Assert.False(capture.IsCaptured);
		Assert.Equal(1, native.EnableCalls);

		capture.Update(FocusedClick);

		Assert.True(capture.IsCaptured);
		Assert.Equal(2, native.EnableCalls);
	}

	[Fact]
	public void Update_ReleasesWhileEscapeIsHeld()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		CameraCaptureTransition transition = capture.Update(
			Idle with { EscapeDown = true });

		Assert.False(transition.ReleaseFailed);
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsLookActive);
		Assert.False(native.NativeEnabled);
		Assert.Equal(1, native.DisableCalls);
	}

	[Fact]
	public void Update_HoldingEscapeDoesNotReleaseRepeatedly()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle with { EscapeDown = true });
		capture.Update(Idle with { EscapeDown = true });
		capture.Update(Idle with { EscapeDown = false });

		Assert.Equal(1, native.DisableCalls);
	}

	[Fact]
	public void Update_HeldEscapeBlocksCaptureAndFireUntilItIsReleased()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		Assert.True(capture.Update(FocusedClick).CaptureStarted);

		capture.Update(Idle);

		// The first held sample releases the pointer exactly once.
		Assert.False(
			capture.Update(Idle with { EscapeDown = true }).ReleaseFailed);

		Assert.False(capture.IsCaptured);
		Assert.Equal(1, native.DisableCalls);

		int enableCalls = native.EnableCalls;

		// Escape stays down across several frames of fresh clicks. No
		// click may recapture the pointer or fire while it is held, and
		// the confirmed release is not repeated frame after frame.
		for (int frame = 0; frame < 4; frame++)
		{
			CameraCaptureTransition pressed = capture.Update(
				FocusedClick with { EscapeDown = true });

			Assert.False(pressed.CaptureStarted);
			Assert.False(pressed.FireRequested);
			Assert.False(capture.IsCaptured);

			Assert.False(
				capture.Update(
					Idle with { EscapeDown = true }).FireRequested);

			Assert.Equal(enableCalls, native.EnableCalls);
			Assert.Equal(1, native.DisableCalls);
		}

		Assert.False(native.NativeEnabled);

		// Releasing Escape restores the normal click behaviour: the
		// first fresh press captures without firing and the next fresh
		// press fires exactly once.
		CameraCaptureTransition recapture = capture.Update(FocusedClick);

		Assert.True(recapture.CaptureStarted);
		Assert.False(recapture.FireRequested);
		Assert.True(capture.IsCaptured);
		Assert.Equal(2, native.EnableCalls);

		// The button stays down, so the following frames carry no new
		// press edge and produce no shot.
		Assert.False(capture.Update(Idle).FireRequested);
		Assert.False(capture.Update(Idle).FireRequested);

		CameraCaptureTransition shot = capture.Update(FocusedClick);

		Assert.True(shot.FireRequested);
		Assert.False(shot.CaptureStarted);
	}

	[Fact]
	public void Update_EscapeHeldBeforeTheFirstClick_BlocksCapture()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { EscapeDown = true });

		Assert.False(transition.CaptureStarted);
		Assert.False(transition.FireRequested);
		Assert.False(capture.IsCaptured);
		Assert.Equal(0, native.EnableCalls);
	}

	[Fact]
	public void Update_EscapeAndClickTogether_ReleasesWithoutRecapturing()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		int enableCalls = native.EnableCalls;

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { EscapeDown = true });

		Assert.False(capture.IsCaptured);
		Assert.False(transition.ReleaseFailed);
		Assert.Equal(enableCalls, native.EnableCalls);
		Assert.False(native.NativeEnabled);

		// The key is still held one frame later and the button yields no
		// new press edge; neither can recapture.
		capture.Update(Idle with { EscapeDown = true });

		Assert.False(capture.IsCaptured);
		Assert.Equal(enableCalls, native.EnableCalls);
	}

	[Fact]
	public void Update_FocusLossAndClickTogether_ReleasesWithoutRecapturing()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		int enableCalls = native.EnableCalls;

		CameraCaptureTransition transition = capture.Update(
			new CameraInputSample(
				KeyboardFocused: false,
				PointerFocus: CameraPointerFocus.OwnWindow,
				LeftButtonPressed: true,
				EscapeDown: false,
				FocusLostDuringFrame: false));

		Assert.False(capture.IsCaptured);
		Assert.False(transition.ReleaseFailed);
		Assert.Equal(enableCalls, native.EnableCalls);
		Assert.False(native.NativeEnabled);
	}

	[Fact]
	public void Update_AnotherWindowTakingThePointerReleases()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		capture.Update(
			Idle with { PointerFocus = CameraPointerFocus.OtherWindow });

		Assert.False(capture.IsCaptured);
		Assert.False(native.NativeEnabled);
	}

	[Fact]
	public void Update_NoWindowOwningThePointerDoesNotRelease()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		// A null mouse focus cannot be attributed to another window, so
		// it is not treated as a focus loss.
		capture.Update(Idle with { PointerFocus = CameraPointerFocus.None });

		Assert.True(capture.IsCaptured);
		Assert.Equal(0, native.DisableCalls);
	}

	[Fact]
	public void Update_RecapturesOnAFreshClickAfterRelease()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);
		capture.Update(Idle with { EscapeDown = true });
		capture.Update(Idle);
		capture.Update(FocusedClick);

		Assert.True(capture.IsCaptured);
		Assert.Equal(2, native.EnableCalls);
		Assert.Equal(2, native.DiscardCalls);
	}

	[Fact]
	public void Update_FailedDisableKeepsCaptureAndBlocksLook()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		native.DisableResult = false;

		CameraCaptureTransition transition = capture.Update(
			Idle with { EscapeDown = true });

		Assert.True(transition.ReleaseFailed);
		Assert.True(transition.ReleasePending);
		Assert.True(capture.IsCaptured);
		Assert.True(capture.IsReleasePending);
		Assert.False(capture.IsLookActive);
		Assert.True(native.NativeEnabled);
		Assert.Equal(1, native.DisableCalls);
	}

	[Fact]
	public void Update_FailedDisableIsRetriedWithoutANewRequest()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		native.DisableResult = false;
		capture.Update(Idle with { EscapeDown = true });

		int enableCalls = native.EnableCalls;

		// Clicking while the release is pending must not start a fresh
		// capture; the pointer is still tracked as captured until the
		// native state is confirmed released.
		capture.Update(
			FocusedClick with { EscapeDown = true });

		Assert.True(capture.IsCaptured);
		Assert.True(capture.IsReleasePending);
		Assert.False(capture.IsLookActive);
		Assert.Equal(enableCalls, native.EnableCalls);

		// Escape is still held, so the retry cannot come from a new
		// edge; the pending release is retried on its own.
		native.DisableResult = true;

		CameraCaptureTransition transition = capture.Update(
			Idle with { EscapeDown = true });

		Assert.False(transition.ReleaseFailed);
		Assert.False(transition.ReleasePending);
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsReleasePending);

		// One failed attempt while the release was requested and one
		// failed retry triggered by the pending state.
		Assert.Equal(3, native.DisableCalls);
		Assert.Equal(enableCalls, native.EnableCalls);
	}

	[Fact]
	public void Update_FailedDisableThatStillChangedNativeState_IsReleased()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		native.DisableResult = false;
		native.ApplyResultDespiteFailure = true;

		CameraCaptureTransition transition = capture.Update(
			Idle with { EscapeDown = true });

		Assert.False(transition.ReleaseFailed);
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsReleasePending);
		Assert.False(native.NativeEnabled);
	}

	[Fact]
	public void Update_FailedEnableIsReportedAndNotCaptured()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		native.EnableResult = false;

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.CaptureFailed);
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsLookActive);
		Assert.Equal(0, native.DiscardCalls);
	}

	[Fact]
	public void Update_FailedEnableThatStillCapturedTracksTheNativeState()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		native.EnableResult = false;
		native.ApplyResultDespiteFailure = true;

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.CaptureFailed);

		// The pointer is captured even though SDL reported failure, so
		// the controller must still be able to release it later.
		Assert.True(capture.IsCaptured);
		Assert.True(native.NativeEnabled);
		Assert.Equal(1, native.DiscardCalls);

		Assert.True(capture.TryRelease());
		Assert.False(native.NativeEnabled);
	}

	[Fact]
	public void TryRelease_WithoutCaptureDoesNotTouchNativeState()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		Assert.True(capture.TryRelease());
		Assert.Equal(0, native.EnableCalls);
		Assert.Equal(0, native.DisableCalls);
	}

	[Fact]
	public void Close_ReleasesOnceAndIsIdempotent()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		Assert.True(capture.Close());
		Assert.False(capture.IsCaptured);
		Assert.Equal(1, native.DisableCalls);

		Assert.True(capture.Close());
		Assert.Equal(1, native.DisableCalls);
		Assert.False(native.NativeEnabled);

		// A closed controller ignores later input entirely.
		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.Equal(default(CameraCaptureTransition), transition);
		Assert.Equal(1, native.EnableCalls);
	}

	[Fact]
	public void Close_AfterAFailedReleaseCanRetry()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		native.DisableResult = false;

		Assert.False(capture.Close());
		Assert.Equal(1, native.DisableCalls);
		Assert.True(capture.IsCaptured);

		native.DisableResult = true;

		Assert.True(capture.Close());
		Assert.Equal(2, native.DisableCalls);
		Assert.False(capture.IsCaptured);
		Assert.False(native.NativeEnabled);
	}

	[Fact]
	public void Close_WithoutCaptureDoesNotTouchNativeState()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		Assert.True(capture.Close());
		Assert.True(capture.Close());
		Assert.Equal(0, native.DisableCalls);
	}

	[Fact]
	public void Update_TheCaptureClickDoesNotFire()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.CaptureStarted);
		Assert.False(transition.FireRequested);
	}

	[Fact]
	public void Update_AFreshClickWhileCapturedFiresOnce()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.FireRequested);
		Assert.False(transition.CaptureStarted);

		// The frames in which the button stays down carry no press edge,
		// so the shot is not repeated.
		Assert.False(capture.Update(Idle).FireRequested);
		Assert.False(capture.Update(Idle).FireRequested);
	}

	[Fact]
	public void Update_AReleaseAndSecondPressFiresAgain()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		Assert.True(capture.Update(FocusedClick).FireRequested);

		capture.Update(Idle);

		Assert.True(capture.Update(FocusedClick).FireRequested);
	}

	[Fact]
	public void Update_EscapeSuppressesTheSameFrameClick()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { EscapeDown = true });

		Assert.False(capture.IsCaptured);
		Assert.False(transition.FireRequested);
		Assert.False(transition.CaptureStarted);

		// Escape is still held on the next frame and the button has no
		// new edge; none of that may turn into a shot.
		Assert.False(
			capture.Update(
				Idle with { EscapeDown = true }).FireRequested);
	}

	[Fact]
	public void Update_FocusLossSuppressesFire()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { KeyboardFocused = false });

		Assert.False(transition.FireRequested);
		Assert.False(capture.IsCaptured);
	}

	[Fact]
	public void Update_AClickFromAnotherWindowDoesNotFire()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);
		capture.Update(Idle);

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with
			{
				PointerFocus = CameraPointerFocus.OtherWindow
			});

		Assert.False(transition.FireRequested);
		Assert.False(capture.IsCaptured);
	}

	[Fact]
	public void Update_APendingReleaseSuppressesFire()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		native.DisableResult = false;

		Assert.True(
			capture.Update(Idle with { EscapeDown = true }).ReleaseFailed);

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.ReleasePending);
		Assert.False(transition.FireRequested);
	}

	[Fact]
	public void Update_AFailedCaptureDoesNotFire()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		native.EnableResult = false;

		CameraCaptureTransition transition = capture.Update(FocusedClick);

		Assert.True(transition.CaptureFailed);
		Assert.False(transition.CaptureStarted);
		Assert.False(transition.FireRequested);

		// The press that failed to capture stays consumed: the button is
		// down, so later frames carry no new edge and no shot.
		Assert.False(capture.Update(Idle).FireRequested);
	}

	[Fact]
	public void Update_TransientFocusLossReleasesCapture()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		Assert.True(capture.IsCaptured);

		// Focus left and returned inside one poll, so the sample is
		// focused and idle apart from the loss indication.
		CameraCaptureTransition transition = capture.Update(
			Idle with { FocusLostDuringFrame = true });

		Assert.False(transition.ReleaseFailed);
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsLookActive);
		Assert.False(native.NativeEnabled);
		Assert.Equal(1, native.DisableCalls);
	}

	[Fact]
	public void Update_TransientFocusLossDoesNotRecaptureWithoutAFreshPress()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		int enableCalls = native.EnableCalls;

		// Focus is back before the application reads the frame.
		capture.Update(Idle with { FocusLostDuringFrame = true });

		for (int frame = 0; frame < 4; frame++)
		{
			capture.Update(Idle);
		}

		Assert.False(capture.IsCaptured);
		Assert.Equal(enableCalls, native.EnableCalls);
		Assert.Equal(1, native.DisableCalls);

		// Only a new intentional click may capture again.
		Assert.True(capture.Update(FocusedClick).CaptureStarted);
		Assert.Equal(enableCalls + 1, native.EnableCalls);
	}

	[Fact]
	public void Update_TransientFocusLossSuppressesTheSameFramePress()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		CameraCaptureTransition transition = capture.Update(
			FocusedClick with { FocusLostDuringFrame = true });

		Assert.False(transition.FireRequested);
		Assert.False(capture.IsCaptured);
		Assert.Equal(1, native.DisableCalls);

		// The press belonged to the unsafe frame, so it cannot fire on a
		// later frame either. A fresh click only recaptures.
		Assert.False(capture.Update(Idle).FireRequested);

		CameraCaptureTransition recapture = capture.Update(FocusedClick);

		Assert.True(recapture.CaptureStarted);
		Assert.False(recapture.FireRequested);
	}

	private sealed class FakeRelativeMouseMode
	{
		public bool NativeEnabled { get; private set; }

		public bool EnableResult { get; set; } = true;

		public bool DisableResult { get; set; } = true;

		/// <summary>
		/// Gets or sets whether a call that reported failure still
		/// changed the native state.
		/// </summary>
		public bool ApplyResultDespiteFailure { get; set; }

		public int EnableCalls { get; private set; }

		public int DisableCalls { get; private set; }

		public int DiscardCalls { get; private set; }

		public CameraCaptureController CreateController()
		{
			return new CameraCaptureController(Set, Query, Discard);
		}

		public bool Set(bool enabled)
		{
			bool result = enabled ? EnableResult : DisableResult;

			if (enabled)
			{
				EnableCalls++;
			}

			else
			{
				DisableCalls++;
			}

			if (result || ApplyResultDespiteFailure)
			{
				NativeEnabled = enabled;
			}

			return result;
		}

		public bool Query()
		{
			return NativeEnabled;
		}

		public void Discard()
		{
			DiscardCalls++;
		}
	}
}
