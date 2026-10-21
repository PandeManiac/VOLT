using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class RelativeMouseModeTests
{
	[Fact]
	public void TrySetRelativeMouseMode_ForwardsTheRequest()
	{
		InputTestWindow window = new();

		Assert.True(window.Input.TrySetRelativeMouseMode(true));
		Assert.True(window.Native.Enabled);
		Assert.Equal(1, window.Native.EnableCalls);

		Assert.True(window.Input.TrySetRelativeMouseMode(false));
		Assert.False(window.Native.Enabled);
		Assert.Equal(1, window.Native.DisableCalls);
	}

	[Fact]
	public void TrySetRelativeMouseMode_ReportsANativeFailure()
	{
		InputTestWindow window = new();
		window.Native.EnableResult = false;

		Assert.False(window.Input.TrySetRelativeMouseMode(true));
		Assert.False(window.Native.Enabled);
	}

	[Fact]
	public void IsRelativeMouseModeEnabled_ReadsTheLiveNativeState()
	{
		InputTestWindow window = new();

		Assert.False(window.Input.IsRelativeMouseModeEnabled);

		// A state change the platform did not request, for example a
		// compositor releasing the pointer, is still reported.
		window.Native.Enabled = true;

		Assert.True(window.Input.IsRelativeMouseModeEnabled);
	}

	[Fact]
	public void DiscardRelativeMotion_ForwardsTheRequest()
	{
		InputTestWindow window = new();

		window.Input.DiscardRelativeMotion();

		Assert.Equal(1, window.Native.DiscardCalls);
	}

	[Fact]
	public void DefaultSnapshot_IsEmpty()
	{
		InputSnapshot snapshot = InputSnapshot.Empty;

		Assert.False(snapshot.KeyboardFocused);
		Assert.False(snapshot.FocusLostDuringFrame);
		Assert.Equal(MouseFocusState.None, snapshot.MouseFocus);
		Assert.Equal(default, snapshot.MousePosition);
		Assert.Equal(default, snapshot.MouseMotion);
		Assert.Equal(default, snapshot.RelativeMotion);
		Assert.Equal(default, snapshot.WheelDelta);
		Assert.False(snapshot.IsKeyDown(KeyCode.Unknown));
		Assert.False(snapshot.WasKeyPressed(KeyCode.A));
		Assert.False(snapshot.WasMouseButtonPressed(MouseButton.Left));
	}
}
