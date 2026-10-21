using System.Numerics;
using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class InputFrameTests
{
	[Fact]
	public void TwoWindows_KeepIndependentState()
	{
		InputTestWindow first = new();
		InputTestWindow second = new();

		first.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		second.Poll(InputEvents.KeyDown(SDL.Scancode.S));

		Assert.True(first.Input.Snapshot.IsKeyDown(KeyCode.W));
		Assert.False(first.Input.Snapshot.IsKeyDown(KeyCode.S));

		Assert.True(second.Input.Snapshot.IsKeyDown(KeyCode.S));
		Assert.False(second.Input.Snapshot.IsKeyDown(KeyCode.W));

		// A window's snapshot only changes when that window takes
		// part in a completed frame; the shared pump completes all
		// registered windows at once. Polling one input directly must
		// therefore leave the other window's last snapshot untouched.
		first.Poll();

		Assert.True(second.Input.Snapshot.WasKeyPressed(KeyCode.S));
		Assert.True(second.Input.Snapshot.IsKeyDown(KeyCode.S));

		second.Poll();

		Assert.False(second.Input.Snapshot.WasKeyPressed(KeyCode.S));
		Assert.True(second.Input.Snapshot.IsKeyDown(KeyCode.S));
		Assert.True(first.Input.Snapshot.IsKeyDown(KeyCode.W));
	}

	[Fact]
	public void TwoWindows_KeepIndependentMotionAndFocus()
	{
		InputTestWindow first = new();
		InputTestWindow second = new();

		first.PollFrame(
			[InputEvents.Motion(10, 10, 5, 5)],
			mouseFocus: MouseFocusState.OwnWindow);

		second.PollFrame(
			[],
			keyboardFocused: false,
			mouseFocus: MouseFocusState.OtherWindow);

		Assert.Equal(
			new Vector2(5, 5),
			first.Input.Snapshot.MouseMotion);
		Assert.Equal(MouseFocusState.OwnWindow,
			first.Input.Snapshot.MouseFocus);

		Assert.Equal(
			Vector2.Zero,
			second.Input.Snapshot.MouseMotion);
		Assert.Equal(MouseFocusState.OtherWindow,
			second.Input.Snapshot.MouseFocus);
		Assert.False(second.Input.Snapshot.KeyboardFocused);
	}

	[Fact]
	public void Snapshot_IsStableAcrossLaterPolls()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		InputSnapshot captured = window.Input.Snapshot;

		window.Poll(InputEvents.KeyUp(SDL.Scancode.W), InputEvents.Motion(50, 60, 70, 80));

		Assert.True(captured.IsKeyDown(KeyCode.W));
		Assert.True(captured.WasKeyPressed(KeyCode.W));
		Assert.False(captured.WasKeyReleased(KeyCode.W));
		Assert.Equal(Vector2.Zero, captured.MouseMotion);
		Assert.Equal(Vector2.Zero, captured.MousePosition);

		InputSnapshot latest = window.Input.Snapshot;

		Assert.False(latest.IsKeyDown(KeyCode.W));
		Assert.True(latest.WasKeyReleased(KeyCode.W));
		Assert.Equal(new Vector2(70, 80), latest.MouseMotion);
	}

	[Fact]
	public void Edges_AreReportedByExactlyOneCompletedFrame()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1));

		Assert.True(
			window.Input.Snapshot.WasMouseButtonPressed(MouseButton.Left));

		for (int frame = 0; frame < 4; frame++)
		{
			window.Poll();

			InputSnapshot snapshot = window.Input.Snapshot;

			Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
			Assert.False(
				snapshot.WasMouseButtonPressed(MouseButton.Left));
			Assert.False(
				snapshot.WasMouseButtonReleased(MouseButton.Left));
		}
	}

	[Fact]
	public void QuietFrame_ReportsNoEdgesAndNoMotion()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W), InputEvents.Motion(100, 100, 5, 5));

		window.Poll();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.True(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
		Assert.Equal(Vector2.Zero, snapshot.MouseMotion);
		Assert.Equal(Vector2.Zero, snapshot.WheelDelta);
		Assert.Equal(new Vector2(100, 100), snapshot.MousePosition);
	}

	[Fact]
	public void DisposedInput_ThrowsOnPublicAccessors()
	{
		InputTestWindow window = new();
		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));

		window.Input.Dispose();

		Assert.Throws<ObjectDisposedException>(
			() => _ = window.Input.Snapshot);
		Assert.Throws<ObjectDisposedException>(
			() => _ = window.Input.IsRelativeMouseModeEnabled);
		Assert.Throws<ObjectDisposedException>(
			() => window.Input.TrySetRelativeMouseMode(true));
		Assert.Throws<ObjectDisposedException>(
			() => window.Input.DiscardRelativeMotion());
	}

	[Fact]
	public void Dispose_IsIdempotentAndDoesNotChangeCapture()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		window.Input.Dispose();
		window.Input.Dispose();

		Assert.Equal(0, window.Native.EnableCalls);
		Assert.Equal(0, window.Native.DisableCalls);
		Assert.True(window.Native.Enabled);
	}

	[Fact]
	public void PumpCallsAfterDisposal_AreIgnored()
	{
		InputTestWindow window = new();

		window.Input.Dispose();

		window.Input.BeginFrame();
		window.Input.ReceiveEvent(InputEvents.KeyDown(SDL.Scancode.W));
		window.Input.CompleteFrame(
			keyboardFocused: true,
			mouseFocus: IntPtr.Zero);
	}

	[Fact]
	public void PollingAfterWarmUp_AllocatesNothingOnQuietFrames()
	{
		InputTestWindow window = new();

		for (int frame = 0; frame < 64; frame++)
		{
			window.Poll();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int frame = 0; frame < 10_000; frame++)
		{
			window.Poll();

			InputSnapshot snapshot = window.Input.Snapshot;

			Consume(
				snapshot.IsKeyDown(KeyCode.W) ||
				snapshot.WasKeyPressed(KeyCode.Escape) ||
				snapshot.WasMouseButtonReleased(MouseButton.Left) ||
				snapshot.RelativeMotion.X != 0.0f ||
				snapshot.WheelDelta.Y != 0.0f ||
				snapshot.MouseFocus == MouseFocusState.OwnWindow);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0L, allocated);
	}

	[Fact]
	public void PollingAfterWarmUp_AllocatesNothingWithInputActivity()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		SDL.Event[] frameEvents =
		[
			InputEvents.KeyDown(SDL.Scancode.W),
			InputEvents.Motion(120, 80, 4, -2),
			InputEvents.MouseButtonDown(1),
			InputEvents.Wheel(0, 1)
		];

		SDL.Event[] releaseEvents =
		[
			InputEvents.KeyUp(SDL.Scancode.W),
			InputEvents.MouseButtonUp(1)
		];

		for (int frame = 0; frame < 64; frame++)
		{
			window.Poll(frameEvents);
			window.Poll(releaseEvents);
			window.Poll();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int frame = 0; frame < 10_000; frame++)
		{
			window.Poll(frameEvents);

			InputSnapshot pressed = window.Input.Snapshot;

			Consume(
				pressed.IsKeyDown(KeyCode.W) &&
				pressed.WasKeyPressed(KeyCode.W) &&
				pressed.IsMouseButtonDown(MouseButton.Left) &&
				pressed.WasMouseButtonPressed(MouseButton.Left) &&
				pressed.MouseMotion.X == 4.0f &&
				pressed.RelativeMotion.Y == -2.0f &&
				pressed.WheelDelta.Y == 1.0f);

			window.Poll(releaseEvents);

			InputSnapshot released = window.Input.Snapshot;

			Consume(
				released.WasKeyReleased(KeyCode.W) &&
				released.WasMouseButtonReleased(MouseButton.Left) &&
				!released.IsKeyDown(KeyCode.W));

			window.Poll();
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0L, allocated);
	}

	private static void Consume(bool value)
	{
		if (!value)
		{
			throw new InvalidOperationException(
				"Unexpected input state during the allocation measurement.");
		}
	}
}
