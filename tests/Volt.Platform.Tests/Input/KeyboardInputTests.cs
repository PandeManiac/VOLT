using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class KeyboardInputTests
{
	[Fact]
	public void Snapshot_BeforeTheFirstPoll_IsAllUp()
	{
		InputTestWindow window = new();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.KeyboardFocused);
		Assert.Equal(MouseFocusState.None, snapshot.MouseFocus);

		foreach (KeyCode key in Enum.GetValues<KeyCode>())
		{
			Assert.False(snapshot.IsKeyDown(key));
			Assert.False(snapshot.WasKeyPressed(key));
			Assert.False(snapshot.WasKeyReleased(key));
		}
	}

	[Fact]
	public void KeyDown_ReportsDownAndOnePressEdge()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.W));
		Assert.True(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
		Assert.False(snapshot.IsKeyDown(KeyCode.S));
	}

	[Fact]
	public void HeldKey_StaysDownWithoutRepeatingThePressEdge()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.Poll();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
		Assert.False(snapshot.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void KeyRepeat_DoesNotCreateAnotherPressEdge()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.A));

		Assert.True(window.Input.Snapshot.WasKeyPressed(KeyCode.A));

		window.Poll(InputEvents.KeyDown(SDL.Scancode.A, repeat: true), InputEvents.KeyDown(SDL.Scancode.A, repeat: true));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.A));
		Assert.False(snapshot.WasKeyPressed(KeyCode.A));
	}

	[Fact]
	public void DuplicateKeyDown_WithoutAKeyUp_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.Space), InputEvents.KeyDown(SDL.Scancode.Space));

		Assert.True(window.Input.Snapshot.IsKeyDown(KeyCode.Space));
		Assert.True(window.Input.Snapshot.WasKeyPressed(KeyCode.Space));
	}

	[Fact]
	public void KeyUp_ReportsReleaseEdgeAndClearsHeldState()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.D));
		window.Poll(InputEvents.KeyUp(SDL.Scancode.D));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsKeyDown(KeyCode.D));
		Assert.False(snapshot.WasKeyPressed(KeyCode.D));
		Assert.True(snapshot.WasKeyReleased(KeyCode.D));
	}

	[Fact]
	public void KeyUp_WithoutAKeyDown_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyUp(SDL.Scancode.Q));

		Invariant(window, KeyCode.Q);
	}

	[Fact]
	public void MultipleKeys_HoldIndependently()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W), InputEvents.KeyDown(SDL.Scancode.A), InputEvents.KeyDown(SDL.Scancode.Left));

		window.Poll(InputEvents.KeyUp(SDL.Scancode.A));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.IsKeyDown(KeyCode.A));
		Assert.True(snapshot.IsKeyDown(KeyCode.Left));
		Assert.True(snapshot.WasKeyReleased(KeyCode.A));
	}

	[Fact]
	public void PressAndReleaseInOneFrame_ReportsBothEdgesAndEndsUp()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.Escape), InputEvents.KeyUp(SDL.Scancode.Escape));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsKeyDown(KeyCode.Escape));
		Assert.True(snapshot.WasKeyPressed(KeyCode.Escape));
		Assert.True(snapshot.WasKeyReleased(KeyCode.Escape));
	}

	[Fact]
	public void ReleaseAndPressInOneFrame_ReportsBothEdgesAndEndsDown()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.R));
		window.Poll(InputEvents.KeyUp(SDL.Scancode.R), InputEvents.KeyDown(SDL.Scancode.R));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.R));
		Assert.True(snapshot.WasKeyPressed(KeyCode.R));
		Assert.True(snapshot.WasKeyReleased(KeyCode.R));
	}

	[Fact]
	public void UnknownScancode_IsIgnored()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.Capslock), InputEvents.KeyDown(SDL.Scancode.Kp5));

		InputSnapshot snapshot = window.Input.Snapshot;

		foreach (KeyCode key in Enum.GetValues<KeyCode>())
		{
			Assert.False(snapshot.IsKeyDown(key));
			Assert.False(snapshot.WasKeyPressed(key));
			Assert.False(snapshot.WasKeyReleased(key));
		}
	}

	[Fact]
	public void InvalidKeyValue_IsInert()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsKeyDown(KeyCode.Unknown));
		Assert.False(snapshot.IsKeyDown((KeyCode)(-1)));
		Assert.False(snapshot.IsKeyDown((KeyCode)5_000));
		Assert.False(snapshot.WasKeyPressed((KeyCode)5_000));
		Assert.False(snapshot.WasKeyReleased((KeyCode)5_000));
	}

	[Fact]
	public void KeyCodes_FitTheSnapshotBitset()
	{
		HashSet<int> seen = [];

		foreach (KeyCode key in Enum.GetValues<KeyCode>())
		{
			int value = (int)key;

			Assert.InRange(value, 0, 127);
			Assert.True(
				key == KeyCode.Unknown || value > 0,
				$"{key} must not collide with KeyCode.Unknown.");
			Assert.True(seen.Add(value), $"{key} shares the value {value}.");
		}

		Assert.True(seen.Count > 40);
	}

	[Fact]
	public void KeyboardFocusLoss_ClearsHeldKeysWithoutReportingARelease()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W), InputEvents.KeyDown(SDL.Scancode.LShift));

		window.PollFrame([], keyboardFocused: false);

		InputSnapshot lost = window.Input.Snapshot;

		Assert.False(lost.KeyboardFocused);
		Assert.False(lost.IsKeyDown(KeyCode.W));
		Assert.False(lost.IsKeyDown(KeyCode.LeftShift));
		Assert.False(lost.WasKeyReleased(KeyCode.W));

		window.Poll();

		InputSnapshot regained = window.Input.Snapshot;

		Assert.True(regained.KeyboardFocused);
		Assert.False(regained.IsKeyDown(KeyCode.W));
		Assert.False(regained.WasKeyPressed(KeyCode.W));
		Assert.False(regained.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void FocusLostEvent_ClearsHeldKeysImmediately()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.Poll(InputEvents.FocusLost());

		Assert.False(window.Input.Snapshot.IsKeyDown(KeyCode.W));
		Assert.False(window.Input.Snapshot.WasKeyReleased(KeyCode.W));
	}

	[Fact]
	public void KeyEventsForAnUnfocusedWindow_AreSuppressed()
	{
		InputTestWindow window = new();

		window.PollFrame([
				InputEvents.KeyDown(SDL.Scancode.R),
				InputEvents.KeyDown(SDL.Scancode.Escape)
			], keyboardFocused: false);

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.KeyboardFocused);
		Assert.False(snapshot.IsKeyDown(KeyCode.R));
		Assert.False(snapshot.WasKeyPressed(KeyCode.R));
		Assert.False(snapshot.IsKeyDown(KeyCode.Escape));
	}

	[Fact]
	public void RefocusWithAKeyHeld_DoesNotInventAPress()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.PollFrame([], keyboardFocused: false);
		window.PollFrame([], keyboardFocused: true);

		Assert.False(window.Input.Snapshot.IsKeyDown(KeyCode.W));

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));

		Assert.True(window.Input.Snapshot.IsKeyDown(KeyCode.W));
		Assert.True(window.Input.Snapshot.WasKeyPressed(KeyCode.W));
	}

	[Fact]
	public void UnrelatedEvents_DoNotChangeKeyboardState()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.KeyDown(SDL.Scancode.W));
		window.Poll(InputEvents.Unrelated());

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.True(snapshot.IsKeyDown(KeyCode.W));
		Assert.False(snapshot.WasKeyPressed(KeyCode.W));
	}

	private static void Invariant(InputTestWindow window, KeyCode key)
	{
		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.False(snapshot.IsKeyDown(key));
		Assert.False(snapshot.WasKeyPressed(key));
		Assert.False(snapshot.WasKeyReleased(key));
	}
}
