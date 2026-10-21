using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class CameraCaptureControllerTests
{
	private static readonly CameraInputSample Idle = new(
		KeyboardFocused: true,
		PointerFocus: CameraPointerFocus.OwnWindow,
		LeftButtonDown: false,
		EscapeDown: false);

	private static readonly CameraInputSample FocusedClick = new(
		KeyboardFocused: true,
		PointerFocus: CameraPointerFocus.OwnWindow,
		LeftButtonDown: true,
		EscapeDown: false);

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
	public void Update_DoesNotRecaptureFromAHeldButton()
	{
		FakeRelativeMouseMode native = new();
		CameraCaptureController capture = native.CreateController();

		capture.Update(FocusedClick);

		// Losing focus releases capture while the button is still held.
		capture.Update(FocusedClick with { KeyboardFocused = false });

		Assert.False(capture.IsCaptured);

		// The held press is not a new click, so regaining focus does not
		// recapture.
		capture.Update(FocusedClick);

		Assert.False(capture.IsCaptured);

		capture.Update(Idle);
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

		Assert.False(capture.Update(FocusedClick).FireRequested);
		Assert.False(
			capture.Update(FocusedClick with { LeftButtonDown = false })
				.FireRequested);

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

		// The keys and button are still held one frame later; a held
		// click is not a new capture request either.
		capture.Update(FocusedClick with { EscapeDown = true });

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
				LeftButtonDown: true,
				EscapeDown: false));

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

		// The same press continued into the next sampled frame is not a
		// new click.
		Assert.False(capture.Update(FocusedClick).FireRequested);
		Assert.False(capture.Update(FocusedClick).FireRequested);
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

		// Both are still held on the next frame; release must not turn
		// into a shot either.
		Assert.False(
			capture.Update(
				FocusedClick with { EscapeDown = true }).FireRequested);
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

		// The press that failed to capture stays consumed.
		Assert.False(capture.Update(FocusedClick).FireRequested);
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
