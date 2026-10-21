using System.Numerics;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

public sealed class MouseMotionInputTests
{
	[Fact]
	public void Motion_AccumulatesWithinOneFrame()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(100, 200, 3, -4), InputEvents.Motion(112, 190, 12, 6), InputEvents.Motion(112, 190, -2, 1));

		Assert.Equal(
			new Vector2(13, 3),
			window.Input.Snapshot.MouseMotion);
	}

	[Fact]
	public void Motion_ResetsAtTheNextFrame()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(100, 200, 8, 8));
		window.Poll();

		Assert.Equal(Vector2.Zero, window.Input.Snapshot.MouseMotion);
	}

	[Fact]
	public void Position_IsTheLatestMotionEvent()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(10, 20, 1, 1), InputEvents.Motion(480.5f, 270.25f, 2, 2));

		Assert.Equal(
			new Vector2(480.5f, 270.25f),
			window.Input.Snapshot.MousePosition);
	}

	[Fact]
	public void Position_SurvivesFramesWithoutMotion()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(320, 240, 0, 0));
		window.Poll();

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.Equal(new Vector2(320, 240), snapshot.MousePosition);
		Assert.Equal(Vector2.Zero, snapshot.MouseMotion);
	}

	[Fact]
	public void Motion_IsNeverClampedToTheWindowBounds()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(-4000, 9000, -320.75f, 512.5f));

		Assert.Equal(
			new Vector2(-320.75f, 512.5f),
			window.Input.Snapshot.MouseMotion);
	}

	[Fact]
	public void Wheel_AccumulatesAndResets()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Wheel(0, 1), InputEvents.Wheel(0, 2), InputEvents.Wheel(1, -3));

		Assert.Equal(
			new Vector2(1, 0),
			window.Input.Snapshot.WheelDelta);

		window.Poll();

		Assert.Equal(Vector2.Zero, window.Input.Snapshot.WheelDelta);
	}

	[Fact]
	public void FlippedWheel_IsNormalizedToPositiveUp()
	{
		InputTestWindow window = new();

		// SDL reports opposite values for a flipped device and flags
		// the event; the adapter restores the usual convention.
		window.Poll(InputEvents.Wheel(2, -3, flipped: true));

		Assert.Equal(
			new Vector2(-2, 3),
			window.Input.Snapshot.WheelDelta);
	}

	[Fact]
	public void RelativeMotion_IsZeroOutsideRelativeMouseMode()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(100, 100, 40, -25));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.Equal(new Vector2(40, -25), snapshot.MouseMotion);
		Assert.Equal(Vector2.Zero, snapshot.RelativeMotion);
	}

	[Fact]
	public void RelativeMotion_ReportsMovementWhileCaptured()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		window.Poll(InputEvents.Motion(640, 360, 15, -6), InputEvents.Motion(640, 360, 5, 2));

		Assert.Equal(
			new Vector2(20, -4),
			window.Input.Snapshot.RelativeMotion);
	}

	[Fact]
	public void RelativeMotion_IsZeroInTheFrameThatStartsCapture()
	{
		InputTestWindow window = new();

		// The pointer moved before the click that captures it in the
		// application's update, after this poll completed.
		window.Poll(InputEvents.Motion(300, 200, 240, -90));

		Assert.Equal(Vector2.Zero, window.Input.Snapshot.RelativeMotion);

		window.Native.Enabled = true;

		window.Poll(InputEvents.Motion(300, 200, 2, 1));

		Assert.Equal(new Vector2(2, 1), window.Input.Snapshot.RelativeMotion);
	}

	[Fact]
	public void RelativeMotion_StopsWhenCaptureEnds()
	{
		InputTestWindow window = new();
		window.Native.Enabled = true;

		window.Poll(InputEvents.Motion(300, 200, 10, 10));
		Assert.Equal(new Vector2(10, 10), window.Input.Snapshot.RelativeMotion);

		window.Native.Enabled = false;

		window.Poll(InputEvents.Motion(310, 210, 4, 4));

		InputSnapshot snapshot = window.Input.Snapshot;

		Assert.Equal(Vector2.Zero, snapshot.RelativeMotion);
		Assert.Equal(new Vector2(4, 4), snapshot.MouseMotion);
	}

	[Fact]
	public void Motion_DoesNotCreateButtonOrKeyEdges()
	{
		InputTestWindow window = new();

		window.Poll(InputEvents.Motion(1, 1, 1, 1));

		InputSnapshot snapshot = window.Input.Snapshot;

		foreach (MouseButton button in Enum.GetValues<MouseButton>())
		{
			Assert.False(snapshot.IsMouseButtonDown(button));
			Assert.False(snapshot.WasMouseButtonPressed(button));
		}

		Assert.False(snapshot.IsKeyDown(KeyCode.W));
	}
}
