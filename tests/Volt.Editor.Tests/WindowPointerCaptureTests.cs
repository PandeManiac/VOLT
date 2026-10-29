using Volt.Editor.Input;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the native pointer-capture state machine: confirmed captures,
/// refused requests, retried releases, and shutdown.
/// </summary>
/// <remarks>
/// The platform is modelled by an in-memory mode, so the cases that
/// matter on a desktop — a backend that lies about success, a release
/// that fails once, a mode dropped by the compositor — are all
/// deterministic here.
/// </remarks>
public sealed class WindowPointerCaptureTests
{
	[Fact]
	public void Capture_EnablesTheNativeModeAndDiscardsStaleMotion()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());
		Assert.True(capture.IsCaptured);
		Assert.True(native.LiveEnabled);

		// The motion accumulated before the mode change is dropped.
		Assert.True(native.Discards > 0);
	}

	[Fact]
	public void Capture_IsIdempotent()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());
		Assert.True(capture.TryCapture());

		Assert.Equal(1, native.EnableRequests);
	}

	[Fact]
	public void RefusedCapture_ReportsTheLiveStateInsteadOfTheRequest()
	{
		FakeNativeMouseMode native = new()
		{
			RefuseEnable = true,
			ApplyRefusedChanges = true
		};

		WindowPointerCapture capture = Create(native);

		// SDL can report failure and still change the mode, so the
		// capture must follow what the backend says now.
		Assert.False(capture.TryCapture());
		Assert.True(capture.IsCaptured);

		// Because it is captured, a later release must actually run.
		Assert.True(capture.TryRelease());
		Assert.False(native.LiveEnabled);
	}

	[Fact]
	public void FailedRelease_StaysPendingAndIsRetried()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());

		native.RefuseDisable = true;

		Assert.False(capture.TryRelease());
		Assert.True(capture.IsCaptured);
		Assert.True(capture.IsReleasePending);

		// While the release is pending the view must stay frozen
		// rather than keep steering a pointer that may still be
		// captured.
		capture.AccumulateMotion(new Volt.Ui.UiPoint(50f, 0f));

		Assert.Equal(Volt.Ui.UiPoint.Zero, capture.ConsumeMotion());

		native.RefuseDisable = false;

		Assert.True(capture.TryRelease());
		Assert.False(capture.IsCaptured);
		Assert.False(capture.IsReleasePending);
	}

	[Fact]
	public void Capture_RetriesAPendingReleaseFirst()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());
		native.RefuseDisable = true;

		Assert.False(capture.TryRelease());

		native.RefuseDisable = false;

		Assert.True(capture.TryCapture());
		Assert.True(capture.IsCaptured);
		Assert.False(capture.IsReleasePending);

		// A capture request that followed a failed release must have
		// released and re-enabled, not assumed the mode was already on.
		Assert.True(native.EnableRequests >= 2);
	}

	[Fact]
	public void Motion_IsIgnoredWhenNotCapturedAndClearedWhenConsumed()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		capture.AccumulateMotion(new Volt.Ui.UiPoint(10f, 4f));

		Assert.Equal(Volt.Ui.UiPoint.Zero, capture.ConsumeMotion());

		Assert.True(capture.TryCapture());
		capture.AccumulateMotion(new Volt.Ui.UiPoint(10f, 4f));
		capture.AccumulateMotion(new Volt.Ui.UiPoint(5f, 1f));

		Volt.Ui.UiPoint motion = capture.ConsumeMotion();

		Assert.Equal(15f, motion.X, 4);
		Assert.Equal(5f, motion.Y, 4);

		// A second pull in the same frame reports nothing: movement is
		// applied once, never twice.
		Assert.Equal(Volt.Ui.UiPoint.Zero, capture.ConsumeMotion());
	}

	[Fact]
	public void Dispose_ReleasesAPointerThatIsStillCaptured()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());

		capture.Dispose();

		Assert.False(native.LiveEnabled);

		// A disposed capture must never start a new one, and releasing
		// again is harmless.
		Assert.False(capture.TryCapture());
		Assert.True(capture.TryRelease());
	}

	[Fact]
	public void Dispose_RetriesWhenTheNativeReleaseIsRefused()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());
		native.RefuseDisable = true;

		capture.Dispose();

		Assert.True(capture.IsCaptured);
		Assert.True(capture.IsReleasePending);
		Assert.True(native.LiveEnabled);

		// A second disposal retries the pending native release instead
		// of claiming cleanup succeeded on the previous attempt.
		native.RefuseDisable = false;
		capture.Dispose();

		Assert.False(native.LiveEnabled);
		Assert.False(capture.TryCapture());
	}

	[Fact]
	public void NonFiniteMotion_IsIgnored()
	{
		FakeNativeMouseMode native = new();
		WindowPointerCapture capture = Create(native);

		Assert.True(capture.TryCapture());

		capture.AccumulateMotion(new Volt.Ui.UiPoint(float.NaN, 1f));
		capture.AccumulateMotion(new Volt.Ui.UiPoint(1f, float.PositiveInfinity));

		Assert.Equal(Volt.Ui.UiPoint.Zero, capture.ConsumeMotion());
	}

	private static WindowPointerCapture Create(FakeNativeMouseMode native)
	{
		return new WindowPointerCapture(
			native.Set,
			() => native.LiveEnabled,
			native.Discard);
	}

	/// <summary>
	/// Models SDL's relative mouse mode: a request can be refused, and a
	/// refused request can still have changed the mode.
	/// </summary>
	private sealed class FakeNativeMouseMode
	{
		public bool LiveEnabled { get; set; }

		public bool RefuseEnable { get; set; }

		public bool RefuseDisable { get; set; }

		public bool ApplyRefusedChanges { get; set; }

		public int EnableRequests { get; private set; }

		public int Discards { get; private set; }

		public bool Set(bool enabled)
		{
			if (enabled)
			{
				EnableRequests++;
			}

			bool refused = enabled ? RefuseEnable : RefuseDisable;

			if (refused)
			{
				if (ApplyRefusedChanges)
				{
					LiveEnabled = enabled;
				}

				return false;
			}

			LiveEnabled = enabled;

			return true;
		}

		public void Discard()
		{
			Discards++;
		}
	}
}
