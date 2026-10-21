using System.Numerics;
using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class InputFocusTests
{
	[Fact]
	public void FocusedPoll_ReportsKeyboardAndOwnPointerFocus()
	{
		InputTestWindow window = new();

		window.Poll();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.Equal(MouseFocusState.OwnWindow, snapshot.MouseFocus);
	}

	[Fact]
	public void PointerOverAnotherWindow_ReportsOtherWindow()
	{
		InputTestWindow window = new();

		window.PollFrame([], mouseFocus: MouseFocusState.OtherWindow);

		Assert.Equal(
			MouseFocusState.OtherWindow,
			window.Input.Snapshot.MouseFocus);
	}

	[Fact]
	public void NullPointerFocus_ReportsNone()
	{
		InputTestWindow window = new();

		window.PollFrame([], mouseFocus: MouseFocusState.None);

		Assert.Equal(
			MouseFocusState.None,
			window.Input.Snapshot.MouseFocus);
	}

	[Fact]
	public void RepeatedFocusTransitions_KeepHeldStateConsistent()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.PollFrame([], keyboardFocused: false);
		window.PollFrame([], keyboardFocused: true);
		window.PollFrame([], keyboardFocused: false);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.KeyboardFocused);
		Assert.False(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void FocusLostAndRegainedInOnePoll_ClearsHeldStateWithoutEdges()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.PollFrame(
			[InputEvents.FocusLost()],
			keyboardFocused: true);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.True(snapshot.FocusLostDuringFrame);
		Assert.False(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void QuietFocusedFrame_DoesNotReportAFocusLoss()
	{
		InputTestWindow window = new();

		window.Poll();

		Assert.False(window.Input.Snapshot.FocusLostDuringFrame);
	}

	[Fact]
	public void FocusLossFlag_DoesNotSurviveIntoTheNextFrame()
	{
		InputTestWindow window = new();

		window.PollFrame([InputEvents.FocusLost()], keyboardFocused: true);
		window.Poll();

		Assert.False(window.Input.Snapshot.FocusLostDuringFrame);
	}

	[Fact]
	public void FocusLostThenRegainedWithAKeyDown_ReportsNoPress()
	{
		InputTestWindow window = new();

		window.PollFrame(
			[InputEvents.FocusLost(), InputEvents.KeyDown(SDL.Scancode.W)],
			keyboardFocused: true);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.True(snapshot.FocusLostDuringFrame);
		Assert.False(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void FocusLostThenRegainedWithAClick_ReportsNoClick()
	{
		InputTestWindow window = new();

		window.PollFrame(
			[
				InputEvents.FocusLost(),
				InputEvents.MouseButtonDown(1),
				InputEvents.MouseButtonUp(1)
			],
			keyboardFocused: true);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonReleased(MouseButton.Left));
	}

	[Fact]
	public void FocusLostThenRegainedWithMotion_ReportsNoMovement()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		window.PollFrame(
			[
				InputEvents.FocusLost(),
				InputEvents.Motion(300, 200, 55, -30),
				InputEvents.Wheel(0, 2)
			],
			keyboardFocused: true);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.Equal(Vector2.Zero, snapshot.MouseMotion);
		Assert.Equal(Vector2.Zero, snapshot.RelativeMotion);
		Assert.Equal(Vector2.Zero, snapshot.WheelDelta);

		// The last known position is a value, not a transition, so it
		// survives the frame.
		Assert.Equal(new Vector2(300, 200), snapshot.MousePosition);
	}

	[Fact]
	public void MovementBeforeAFocusLossInTheSameFrame_IsDropped()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		window.Poll(
			InputEvents.Motion(300, 200, 10, 10),
			InputEvents.FocusLost());

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.True(snapshot.FocusLostDuringFrame);
		Assert.Equal(Vector2.Zero, snapshot.MouseMotion);
		Assert.Equal(Vector2.Zero, snapshot.RelativeMotion);
	}

	[Fact]
	public void TheFrameAfterATransientFocusLoss_ReportsNewInputNormally()
	{
		InputTestWindow window = new();

		window.PollFrame([InputEvents.FocusLost()], keyboardFocused: true);
		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.FocusLostDuringFrame);
		Assert.True(snapshot.WasKeyPressed(KeyCode.W));
		Assert.True(snapshot.IsKeyDown(KeyCode.W));
	}

	[Fact]
	public void TransientFocusLoss_IsPerWindow()
	{
		InputTestWindow first = new();
		InputTestWindow second = new();

		first.PollFrame([InputEvents.FocusLost()], keyboardFocused: true);
		second.Poll(InputEvents.KeyDown(SDL.Scancode.S));

		Assert.True(first.Input.Snapshot.FocusLostDuringFrame);
		Assert.False(second.Input.Snapshot.FocusLostDuringFrame);
		Assert.True(second.Input.Snapshot.WasKeyPressed(KeyCode.S));
	}

	[Fact]
	public void FocusLossWithASimultaneousPress_ReportsNoClick()
	{
		InputTestWindow window = new();

		window.PollFrame([
				InputEvents.KeyDown(SDL.Scancode.W),
				InputEvents.MouseButtonDown(1)
			], keyboardFocused: false);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.KeyboardFocused);
		Assert.False(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
	}

	[Fact]
	public void FocusRegainedWithAButtonHeld_ReportsNoClick()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1));
		window.PollFrame([], keyboardFocused: false);
		window.Poll();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.KeyboardFocused);
		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
	}
}
