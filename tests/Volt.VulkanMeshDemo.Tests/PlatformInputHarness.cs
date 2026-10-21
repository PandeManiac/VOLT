using SDL3;
using Volt.Platform.Input;

namespace Volt.VulkanMeshDemo.Tests;

/// <summary>
/// Drives one engine <see cref="WindowInput"/> through complete frames
/// without creating a native window or touching SDL's event queue.
/// </summary>
/// <remarks>
/// The harness feeds synthetic native event values to the same
/// translator the shared pump uses, and replaces the native
/// relative-mode backend with an in-memory model. Nothing here calls
/// SDL, so the sample's input mapping can be tested on a machine with
/// no display server.
/// </remarks>
internal sealed class PlatformInputHarness
{
	private static readonly IntPtr OwnHandle = new(0x1000);
	private static readonly IntPtr OtherHandle = new(0x2000);

	public PlatformInputHarness()
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

	public static SDL.Event KeyDown(SDL.Scancode scancode)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.KeyDown;
		nativeEvent.Key.Scancode = scancode;
		nativeEvent.Key.Down = true;
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

	public static SDL.Event MouseDown(byte button = 1)
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.MouseButtonDown;
		nativeEvent.Button.Button = button;
		nativeEvent.Button.Down = true;
		return nativeEvent;
	}

	public static SDL.Event MouseUp(byte button = 1)
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

	public static SDL.Event FocusLost()
	{
		SDL.Event nativeEvent = default;
		nativeEvent.Type = (uint)SDL.EventType.WindowFocusLost;
		return nativeEvent;
	}
}

/// <summary>
/// Models the native relative-mouse-mode state for the bridge tests.
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
