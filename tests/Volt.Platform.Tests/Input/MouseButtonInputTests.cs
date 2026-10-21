using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class MouseButtonInputTests
{
	[Theory]
	[InlineData(1, MouseButton.Left)]
	[InlineData(2, MouseButton.Middle)]
	[InlineData(3, MouseButton.Right)]
	[InlineData(4, MouseButton.X1)]
	[InlineData(5, MouseButton.X2)]
	public void EachButton_ReportsPressHoldAndRelease(
		byte nativeButton,
		MouseButton button)
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(nativeButton));

		InputSnapshot pressed = window.Input.Snapshot;

		Assert.True(pressed.IsMouseButtonDown(button));
		Assert.True(pressed.WasMouseButtonPressed(button));
		Assert.False(pressed.WasMouseButtonReleased(button));

		window.Poll();

		InputSnapshot held = window.Input.Snapshot;

		Assert.True(held.IsMouseButtonDown(button));
		Assert.False(held.WasMouseButtonPressed(button));

		window.Poll(InputEvents.MouseButtonUp(nativeButton));

		InputSnapshot released = window.Input.Snapshot;

		Assert.False(released.IsMouseButtonDown(button));
		Assert.False(released.WasMouseButtonPressed(button));
		Assert.True(released.WasMouseButtonReleased(button));
	}

	[Fact]
	public void DuplicateButtonDown_WithoutAButtonUp_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1), InputEvents.MouseButtonDown(1));

		Assert.True(window.Input.Snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.True(window.Input.Snapshot.WasMouseButtonPressed(MouseButton.Left));
	}

	[Fact]
	public void ButtonUp_WithoutAButtonDown_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonUp(1));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonReleased(MouseButton.Left));
	}

	[Fact]
	public void MultipleButtons_HoldIndependently()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1), InputEvents.MouseButtonDown(3), InputEvents.MouseButtonDown(4));

		window.Poll(InputEvents.MouseButtonUp(3));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Right));
		Assert.True(snapshot.IsMouseButtonDown(MouseButton.X1));
		Assert.True(snapshot.WasMouseButtonReleased(MouseButton.Right));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
	}

	[Fact]
	public void PressAndReleaseInOneFrame_ReportsBothEdgesAndEndsUp()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1), InputEvents.MouseButtonUp(1));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.True(snapshot.WasMouseButtonPressed(MouseButton.Left));
		Assert.True(snapshot.WasMouseButtonReleased(MouseButton.Left));
	}

	[Fact]
	public void UnknownNativeButton_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(0), InputEvents.MouseButtonDown(9));

		InputSnapshot snapshot = window.Input.Snapshot;

		foreach (MouseButton button in Enum.GetValues<MouseButton>())
		{
			Assert.False(snapshot.IsMouseButtonDown(button));
			Assert.False(snapshot.WasMouseButtonPressed(button));
		}
	}

	[Fact]
	public void InvalidButtonValue_IsInert()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown((MouseButton)(-1)));
		Assert.False(snapshot.IsMouseButtonDown((MouseButton)5));
		Assert.False(snapshot.WasMouseButtonPressed((MouseButton)5));
		Assert.False(snapshot.WasMouseButtonReleased((MouseButton)5));
		Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
	}

	[Fact]
	public void MouseButtons_DoNotChangeRelativeMouseMode()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1));
		window.Poll(InputEvents.MouseButtonUp(1));

		Assert.Equal(0, window.Native.EnableCalls);
		Assert.Equal(0, window.Native.DisableCalls);
		Assert.False(window.Native.Enabled);
	}

	[Fact]
	public void FocusLoss_ClearsHeldButtonsWithoutAFabricatedClick()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.MouseButtonDown(1));
		window.PollFrame([], keyboardFocused: false);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonReleased(MouseButton.Left));
	}

	[Fact]
	public void ButtonEventsForAnUnfocusedWindow_AreSuppressed()
	{
		InputTestWindow window = new();

		window.PollFrame([InputEvents.MouseButtonDown(1)], keyboardFocused: false);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsMouseButtonDown(MouseButton.Left));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
	}

	[Fact]
	public void PointerOverAnotherWindow_StillReportsThisWindowsButtons()
	{
		InputTestWindow window = new();

		window.PollFrame([InputEvents.MouseButtonDown(1)], mouseFocus: MouseFocusState.OtherWindow);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.Equal(MouseFocusState.OtherWindow, snapshot.MouseFocus);
		Assert.True(snapshot.IsMouseButtonDown(MouseButton.Left));
	}
}
