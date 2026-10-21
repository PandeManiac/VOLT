using System.Numerics;
using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Internal;

/// <summary>
/// Translates native SDL input events into accumulator calls.
/// </summary>
/// <remarks>
/// <para>
/// This is the only place that reads SDL's tagged event union for
/// input. Each event type is read through its own member, so a motion
/// event never reads a key field and a key event never reads a wheel
/// field. Event types that do not describe keyboard, mouse, or focus
/// input leave the accumulator untouched, which makes it harmless to
/// offer every routed event to the translator.
/// </para>
/// <para>
/// Window scoping is not this type's job: the shared pump already
/// resolved the native window ID before calling it. The translator is
/// deterministic and allocation-free, so it can be exercised with
/// synthetic event values on any platform, including a headless one.
/// </para>
/// </remarks>
internal static class SdlInputTranslator
{
	// SDL numbers its mouse buttons independently of the flags used
	// for the button state mask: 1 is left, 2 is middle, 3 is right,
	// then the two side buttons. The public enum uses its own dense
	// ordering, so the mapping is explicit rather than an offset.
	private const byte LeftButtonCode = 1;
	private const byte MiddleButtonCode = 2;
	private const byte RightButtonCode = 3;
	private const byte X1ButtonCode = 4;
	private const byte X2ButtonCode = 5;

	/// <summary>
	/// Applies one native event to the frame accumulator.
	/// </summary>
	internal static void Translate(
		in SDL.Event nativeEvent,
		ref InputStateAccumulator accumulator)
	{
		switch ((SDL.EventType)nativeEvent.Type)
		{
			case SDL.EventType.KeyDown:
				TranslateKey(nativeEvent.Key, ref accumulator, down: true);
				break;

			case SDL.EventType.KeyUp:
				TranslateKey(nativeEvent.Key, ref accumulator, down: false);
				break;

			case SDL.EventType.MouseButtonDown:
				TranslateMouseButton(
					nativeEvent.Button,
					ref accumulator,
					down: true);
				break;

			case SDL.EventType.MouseButtonUp:
				TranslateMouseButton(
					nativeEvent.Button,
					ref accumulator,
					down: false);
				break;

			case SDL.EventType.MouseMotion:
				accumulator.ApplyMouseMotion(
					new Vector2(nativeEvent.Motion.X, nativeEvent.Motion.Y),
					new Vector2(
						nativeEvent.Motion.XRel,
						nativeEvent.Motion.YRel));
				break;

			case SDL.EventType.MouseWheel:
				TranslateMouseWheel(nativeEvent.Wheel, ref accumulator);
				break;

			// Losing keyboard focus drops held keys and buttons without
			// reporting releases, so a focus change can neither leave a
			// key stuck down nor fabricate a gameplay click. The frame
			// remembers the loss even if focus returns before it ends,
			// so input delivered while unfocused cannot leak into the
			// completed snapshot.
			case SDL.EventType.WindowFocusLost:
				accumulator.ApplyFocusLost();
				break;
		}
	}

	private static void TranslateKey(
		in SDL.KeyboardEvent keyEvent,
		ref InputStateAccumulator accumulator,
		bool down)
	{
		if (!SdlKeyMap.TryMap(keyEvent.Scancode, out KeyCode key))
		{
			return;
		}

		accumulator.ApplyKey(key, down, keyEvent.Repeat);
	}

	private static void TranslateMouseButton(
		in SDL.MouseButtonEvent buttonEvent,
		ref InputStateAccumulator accumulator,
		bool down)
	{
		MouseButton button;

		switch (buttonEvent.Button)
		{
			case LeftButtonCode:
				button = MouseButton.Left;
				break;

			case MiddleButtonCode:
				button = MouseButton.Middle;
				break;

			case RightButtonCode:
				button = MouseButton.Right;
				break;

			case X1ButtonCode:
				button = MouseButton.X1;
				break;

			case X2ButtonCode:
				button = MouseButton.X2;
				break;

			default:
				return;
		}

		accumulator.ApplyMouseButton(button, down);
	}

	private static void TranslateMouseWheel(
		in SDL.MouseWheelEvent wheelEvent,
		ref InputStateAccumulator accumulator)
	{
		// SDL reports the deltas already inverted for a flipped device
		// and flags the event so applications can restore the usual
		// positive-up convention.
		float scale = wheelEvent.Direction == SDL.MouseWheelDirection.Flipped
			? -1.0f
			: 1.0f;

		accumulator.ApplyMouseWheel(
			new Vector2(wheelEvent.X * scale, wheelEvent.Y * scale));
	}
}
