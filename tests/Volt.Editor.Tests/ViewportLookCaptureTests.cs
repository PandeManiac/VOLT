using System.Numerics;
using Volt.Editor.Input;
using Volt.Editor.Viewport;
using Volt.Rendering.Cameras;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers free-look pointer capture: the platform state machine, the
/// viewport's capture lifecycle, relative motion, fallback behaviour,
/// and the cleanup that has to happen when navigation ends for any
/// reason.
/// </summary>
/// <remarks>
/// Nothing here needs a window or a GPU. The capture is injected, so a
/// focus loss, a refused native request, and a revoked mode can all be
/// exercised exactly as they happen on a desktop.
/// </remarks>
public sealed class ViewportLookCaptureTests : IDisposable
{
	private static readonly UiSize Surface = new(1200f, 800f);

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();
	private readonly FakePointerCapture _capture = new();
	private readonly SceneViewport _viewport;

	public ViewportLookCaptureTests()
	{
		_context.PointerCapture = _capture;

		_viewport = new SceneViewport(_session);

		_context.Add(_viewport);
		_context.BeginFrame(0.0, Surface, UiPoint.Zero);
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();
	}

	private UiPoint Centre => _viewport.GlobalVisualBounds.Center;

	private float Yaw => _viewport.Camera.Yaw;

	[Fact]
	public void RightPress_CapturesThePointerWithoutTurningTheView()
	{
		float before = Yaw;

		_context.PointerDown(UiMouseButton.Right, Centre);

		Assert.True(_capture.IsCaptured);
		Assert.Equal(1, _capture.CaptureRequests);

		// The click that starts the look must not move the camera, and
		// the mode change itself must not fabricate motion.
		_viewport.Tick(0.016, 1f);

		Assert.Equal(before, Yaw, 5);
	}

	[Fact]
	public void CapturedMotion_TurnsTheCameraByTheRelativeDelta()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		_capture.AccumulateMotion(new UiPoint(100f, 0f));
		_viewport.Tick(0.016, 1f);

		// Positive X motion turns right, which subtracts from yaw.
		float expected = 100f * CameraController.DefaultMouseSensitivity;

		Assert.Equal(before - expected, Yaw, 4);
	}

	[Fact]
	public void CapturedMotion_IsNotBoundByAnyDesktopEdge()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		// Ten frames of motion far larger than a monitor: a captured
		// pointer keeps reporting movement, so yaw keeps accumulating
		// across complete revolutions instead of stopping at an edge.
		for (int frame = 0; frame < 10; frame++)
		{
			_capture.AccumulateMotion(new UiPoint(2000f, 0f));
			_viewport.Tick(0.016, 1f);
		}

		float travelled = before - Yaw;

		Assert.True(
			MathF.Abs(travelled) > MathF.Tau * 2f,
			$"Ten wide sweeps must pass two full turns, got {travelled} rad.");
	}

	[Fact]
	public void MotionIsConsumedExactlyOncePerFrame()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		_capture.AccumulateMotion(new UiPoint(40f, 0f));

		// A repaint and a shell tick both call Tick in the same frame in
		// some hosts; the motion belongs to the frame, not the call.
		_viewport.Tick(0.016, 1f);
		float afterFirst = Yaw;
		_viewport.Tick(0.016, 1f);

		Assert.Equal(afterFirst, Yaw, 5);

		float expected = 40f * CameraController.DefaultMouseSensitivity;

		Assert.Equal(before - expected, afterFirst, 4);
	}

	[Fact]
	public void ReleasingTheLookButton_GivesThePointerBack()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		Assert.True(_capture.IsCaptured);

		_context.PointerUp(UiMouseButton.Right, Centre);

		Assert.False(_capture.IsCaptured);
		Assert.True(_capture.ReleaseRequests > 0);

		// Motion reported after the release cannot keep turning the
		// camera, and the capture ignores it because it is not active.
		float afterRelease = Yaw;
		_capture.AccumulateMotion(new UiPoint(120f, 0f));
		_viewport.Tick(0.016, 1f);

		Assert.Equal(afterRelease, Yaw, 5);
	}

	[Fact]
	public void FocusLoss_EndsTheLookAndRestoresThePointer()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		_context.WindowFocusLost();

		Assert.False(_capture.IsCaptured);
		Assert.True(_capture.ReleaseRequests > 0);

		_capture.AccumulateMotion(new UiPoint(300f, 0f));
		_viewport.Tick(0.016, 1f);

		Assert.Equal(before, Yaw, 5);
	}

	[Fact]
	public void RemovingTheViewport_ReleasesTheCapture()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		Assert.True(_capture.IsCaptured);

		_viewport.RemoveFromParent();

		Assert.False(_capture.IsCaptured);
	}

	[Fact]
	public void Escape_EndsTheLookWithoutMovingTheCamera()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		Assert.True(_context.KeyDown(UiKey.Escape));

		Assert.False(_capture.IsCaptured);

		_capture.AccumulateMotion(new UiPoint(250f, 0f));
		_viewport.Tick(0.016, 1f);

		Assert.Equal(before, Yaw, 5);
	}

	[Fact]
	public void RefusedCapture_FallsBackToOrdinaryPointerMovement()
	{
		_capture.CaptureSucceeds = false;

		_context.PointerDown(UiMouseButton.Right, Centre);
		float before = Yaw;

		Assert.False(_capture.IsCaptured);

		// The cursor is visible and moving normally in this mode, so
		// the viewport must still look around from pointer deltas.
		_context.PointerMove(Centre, new UiPoint(60f, 0f));

		float expected = 60f * CameraController.DefaultMouseSensitivity;

		Assert.Equal(before - expected, Yaw, 4);
	}

	[Fact]
	public void RevokedCapture_EndsTheLookInsteadOfTurningInvisibly()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		Assert.True(_capture.IsCaptured);

		// The platform dropped the mode on its own: the pointer is
		// visible again, so the view must stop claiming it.
		_capture.Revoke();

		float before = Yaw;
		_viewport.Tick(0.016, 1f);

		// The look ended, so the UI capture went with it.
		Assert.Null(_context.CapturedWidget);

		_capture.AccumulateMotion(new UiPoint(300f, 0f));
		_viewport.Tick(0.016, 1f);

		Assert.Equal(before, Yaw, 5);
	}

	[Fact]
	public void MovementKeys_FlyTheCameraWhileTheLookIsCaptured()
	{
		_context.PointerDown(UiMouseButton.Right, Centre);
		Vector3 before = _viewport.Camera.Position;

		Assert.True(_context.KeyDown(UiKey.W));
		_viewport.Tick(0.5, 1f);

		Assert.True(
			Vector3.Distance(before, _viewport.Camera.Position) > 0.01f,
			"W must fly the camera while the look button is held.");
	}

	[Fact]
	public void CapturedPointer_DoesNotHoverOtherPanels()
	{
		Button button = new("Inspector");

		_context.Overlay.Add(button);
		_context.Overlay.SetSlot(button, new UiRect(20f, 20f, 100f, 30f));
		_context.BeginFrame(0.0, Surface, UiPoint.Zero);

		// Without capture the button is the hover target, which proves
		// the probe itself works.
		_context.PointerMove(new UiPoint(40f, 30f));

		Assert.Same(button, _context.HoveredWidget);

		_context.PointerDown(UiMouseButton.Right, Centre);
		_context.PointerMove(new UiPoint(45f, 32f));

		Assert.NotSame(button, _context.HoveredWidget);
	}

	private sealed class FakePointerCapture : IUiPointerCapture
	{
		private UiPoint _motion;

		public bool CaptureSucceeds { get; set; } = true;

		public int CaptureRequests { get; private set; }

		public int ReleaseRequests { get; private set; }

		public bool IsCaptured { get; private set; }

		public bool TryCapture()
		{
			CaptureRequests++;

			if (!CaptureSucceeds)
			{
				return false;
			}

			IsCaptured = true;
			_motion = UiPoint.Zero;

			return true;
		}

		public bool TryRelease()
		{
			ReleaseRequests++;

			IsCaptured = false;
			_motion = UiPoint.Zero;

			return true;
		}

		public void DiscardMotion()
		{
			_motion = UiPoint.Zero;
		}

		public void AccumulateMotion(UiPoint motion)
		{
			if (!IsCaptured)
			{
				return;
			}

			_motion = new UiPoint(_motion.X + motion.X, _motion.Y + motion.Y);
		}

		public UiPoint ConsumeMotion()
		{
			if (!IsCaptured)
			{
				return UiPoint.Zero;
			}

			UiPoint motion = _motion;

			_motion = UiPoint.Zero;

			return motion;
		}

		/// <summary>
		/// Models the platform dropping the mode, as a focus change or a
		/// compositor can.
		/// </summary>
		public void Revoke()
		{
			IsCaptured = false;
			_motion = UiPoint.Zero;
		}
	}
}
