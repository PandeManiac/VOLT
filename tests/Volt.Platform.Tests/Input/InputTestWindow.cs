using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Tests.Input;

/// <summary>
/// Drives one <see cref="WindowInput"/> through complete frames
/// without creating a native window or touching SDL's event queue.
/// </summary>
/// <remarks>
/// The harness replaces the native relative-mode backend with
/// <see cref="FakeRelativeMouseMode"/> and feeds synthetic event
/// values straight to the translator. No SDL function is called, so
/// the tests need neither a desktop nor a display server.
/// </remarks>
internal sealed class InputTestWindow
{
	private static readonly IntPtr OwnHandle = new(0x1000);
	private static readonly IntPtr OtherHandle = new(0x2000);

	public InputTestWindow()
	{
		Input = new WindowInput(
			OwnHandle,
			Native.Set,
			Native.Query,
			Native.Discard);
	}

	public WindowInput Input { get; }

	public FakeRelativeMouseMode Native { get; } = new();

	public void Poll()
	{
		PollFrame([]);
	}

	public void Poll(params SDL.Event[] events)
	{
		PollFrame(events);
	}

	public void PollFrame(
		SDL.Event[] events,
		bool keyboardFocused = true,
		MouseFocusState mouseFocus = MouseFocusState.OwnWindow)
	{
		Input.BeginFrame();

		foreach (SDL.Event nativeEvent in events)
		{
			Input.ReceiveEvent(nativeEvent);
		}

		IntPtr mouseFocusHandle = mouseFocus switch
		{
			MouseFocusState.OwnWindow => OwnHandle,
			MouseFocusState.OtherWindow => OtherHandle,
			_ => IntPtr.Zero
		};

		Input.CompleteFrame(keyboardFocused, mouseFocusHandle);
	}
}

/// <summary>
/// Models the native relative-mouse-mode state for the input tests.
/// </summary>
internal sealed class FakeRelativeMouseMode
{
	public bool Enabled { get; set; }

	public bool EnableResult { get; set; } = true;

	public bool DisableResult { get; set; } = true;

	public int EnableCalls { get; private set; }

	public int DisableCalls { get; private set; }

	public int DiscardCalls { get; private set; }

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

		if (result)
		{
			Enabled = enabled;
		}

		return result;
	}

	public bool Query()
	{
		return Enabled;
	}

	public void Discard()
	{
		DiscardCalls++;
	}
}

/// <summary>
/// Builds synthetic native event values for the input tests.
/// </summary>
/// <remarks>
/// The values only fill the fields the translator reads; no native
/// call creates or consumes them.
/// </remarks>
internal static class InputEvents
{
	public static SDL.Event KeyDown(
		SDL.Scancode scancode,
		bool repeat = false)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.KeyDown;
		nativeEvent.Key.Scancode = scancode;
		nativeEvent.Key.Down = true;
		nativeEvent.Key.Repeat = repeat;
		return nativeEvent;
	}

	public static SDL.Event KeyUp(SDL.Scancode scancode)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.KeyUp;
		nativeEvent.Key.Scancode = scancode;
		nativeEvent.Key.Down = false;
		return nativeEvent;
	}

	public static SDL.Event MouseButtonDown(byte button)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.MouseButtonDown;
		nativeEvent.Button.Button = button;
		nativeEvent.Button.Down = true;
		return nativeEvent;
	}

	public static SDL.Event MouseButtonUp(byte button)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.MouseButtonUp;
		nativeEvent.Button.Button = button;
		nativeEvent.Button.Down = false;
		return nativeEvent;
	}

	public static SDL.Event Motion(
		float x,
		float y,
		float xRelative,
		float yRelative)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.MouseMotion;
		nativeEvent.Motion.X = x;
		nativeEvent.Motion.Y = y;
		nativeEvent.Motion.XRel = xRelative;
		nativeEvent.Motion.YRel = yRelative;
		return nativeEvent;
	}

	public static SDL.Event Wheel(
		float x,
		float y,
		bool flipped = false)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.MouseWheel;
		nativeEvent.Wheel.X = x;
		nativeEvent.Wheel.Y = y;
		nativeEvent.Wheel.Direction = flipped
			? SDL.MouseWheelDirection.Flipped
			: SDL.MouseWheelDirection.Normal;
		return nativeEvent;
	}

	public static SDL.Event FocusLost()
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.WindowFocusLost;
		return nativeEvent;
	}

	public static SDL.Event Unrelated()
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.WindowResized;
		nativeEvent.Window.Data1 = 1280;
		nativeEvent.Window.Data2 = 720;
		return nativeEvent;
	}
}
