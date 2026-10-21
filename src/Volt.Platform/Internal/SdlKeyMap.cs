using SDL3;
using Volt.Platform.Input;

namespace Volt.Platform.Internal;

/// <summary>
/// Translates SDL physical scancodes into <see cref="KeyCode"/>.
/// </summary>
/// <remarks>
/// <para>
/// SDL scancodes describe physical key positions rather than the
/// character a layout produces, which is exactly the identity
/// <see cref="KeyCode"/> uses. The mapping is a lookup table built
/// once at type initialization, so translating an event performs no
/// allocation and no search.
/// </para>
/// <para>
/// Scancodes outside the supported subset, including modifier and
/// keypad codes the engine does not expose yet, map to
/// <see cref="KeyCode.Unknown"/> and are ignored by the accumulator.
/// The table covers <see cref="SDL.Scancode.Count"/> entries, so even
/// an arbitrary native value is answered without a bounds failure.
/// </para>
/// </remarks>
internal static class SdlKeyMap
{
	private static readonly KeyCode[] s_keys = CreateMap();

	/// <summary>
	/// Translates a native scancode.
	/// </summary>
	/// <returns>
	/// True when the scancode is part of the supported key set.
	/// </returns>
	internal static bool TryMap(SDL.Scancode scancode, out KeyCode key)
	{
		int index = (int)scancode;

		if ((uint)index >= (uint)s_keys.Length)
		{
			key = KeyCode.Unknown;
			return false;
		}

		key = s_keys[index];
		return key != KeyCode.Unknown;
	}

	private static KeyCode[] CreateMap()
	{
		KeyCode[] keys = new KeyCode[(int)SDL.Scancode.Count];

		keys[(int)SDL.Scancode.A] = KeyCode.A;
		keys[(int)SDL.Scancode.B] = KeyCode.B;
		keys[(int)SDL.Scancode.C] = KeyCode.C;
		keys[(int)SDL.Scancode.D] = KeyCode.D;
		keys[(int)SDL.Scancode.E] = KeyCode.E;
		keys[(int)SDL.Scancode.F] = KeyCode.F;
		keys[(int)SDL.Scancode.G] = KeyCode.G;
		keys[(int)SDL.Scancode.H] = KeyCode.H;
		keys[(int)SDL.Scancode.I] = KeyCode.I;
		keys[(int)SDL.Scancode.J] = KeyCode.J;
		keys[(int)SDL.Scancode.K] = KeyCode.K;
		keys[(int)SDL.Scancode.L] = KeyCode.L;
		keys[(int)SDL.Scancode.M] = KeyCode.M;
		keys[(int)SDL.Scancode.N] = KeyCode.N;
		keys[(int)SDL.Scancode.O] = KeyCode.O;
		keys[(int)SDL.Scancode.P] = KeyCode.P;
		keys[(int)SDL.Scancode.Q] = KeyCode.Q;
		keys[(int)SDL.Scancode.R] = KeyCode.R;
		keys[(int)SDL.Scancode.S] = KeyCode.S;
		keys[(int)SDL.Scancode.T] = KeyCode.T;
		keys[(int)SDL.Scancode.U] = KeyCode.U;
		keys[(int)SDL.Scancode.V] = KeyCode.V;
		keys[(int)SDL.Scancode.W] = KeyCode.W;
		keys[(int)SDL.Scancode.X] = KeyCode.X;
		keys[(int)SDL.Scancode.Y] = KeyCode.Y;
		keys[(int)SDL.Scancode.Z] = KeyCode.Z;

		keys[(int)SDL.Scancode.Alpha0] = KeyCode.Number0;
		keys[(int)SDL.Scancode.Alpha1] = KeyCode.Number1;
		keys[(int)SDL.Scancode.Alpha2] = KeyCode.Number2;
		keys[(int)SDL.Scancode.Alpha3] = KeyCode.Number3;
		keys[(int)SDL.Scancode.Alpha4] = KeyCode.Number4;
		keys[(int)SDL.Scancode.Alpha5] = KeyCode.Number5;
		keys[(int)SDL.Scancode.Alpha6] = KeyCode.Number6;
		keys[(int)SDL.Scancode.Alpha7] = KeyCode.Number7;
		keys[(int)SDL.Scancode.Alpha8] = KeyCode.Number8;
		keys[(int)SDL.Scancode.Alpha9] = KeyCode.Number9;

		keys[(int)SDL.Scancode.F1] = KeyCode.F1;
		keys[(int)SDL.Scancode.F2] = KeyCode.F2;
		keys[(int)SDL.Scancode.F3] = KeyCode.F3;
		keys[(int)SDL.Scancode.F4] = KeyCode.F4;
		keys[(int)SDL.Scancode.F5] = KeyCode.F5;
		keys[(int)SDL.Scancode.F6] = KeyCode.F6;
		keys[(int)SDL.Scancode.F7] = KeyCode.F7;
		keys[(int)SDL.Scancode.F8] = KeyCode.F8;
		keys[(int)SDL.Scancode.F9] = KeyCode.F9;
		keys[(int)SDL.Scancode.F10] = KeyCode.F10;
		keys[(int)SDL.Scancode.F11] = KeyCode.F11;
		keys[(int)SDL.Scancode.F12] = KeyCode.F12;

		keys[(int)SDL.Scancode.Up] = KeyCode.Up;
		keys[(int)SDL.Scancode.Down] = KeyCode.Down;
		keys[(int)SDL.Scancode.Left] = KeyCode.Left;
		keys[(int)SDL.Scancode.Right] = KeyCode.Right;

		keys[(int)SDL.Scancode.Escape] = KeyCode.Escape;
		keys[(int)SDL.Scancode.Space] = KeyCode.Space;
		keys[(int)SDL.Scancode.Return] = KeyCode.Enter;
		keys[(int)SDL.Scancode.Tab] = KeyCode.Tab;
		keys[(int)SDL.Scancode.Backspace] = KeyCode.Backspace;
		keys[(int)SDL.Scancode.Delete] = KeyCode.Delete;
		keys[(int)SDL.Scancode.Insert] = KeyCode.Insert;
		keys[(int)SDL.Scancode.Home] = KeyCode.Home;
		keys[(int)SDL.Scancode.End] = KeyCode.End;
		keys[(int)SDL.Scancode.Pageup] = KeyCode.PageUp;
		keys[(int)SDL.Scancode.Pagedown] = KeyCode.PageDown;

		keys[(int)SDL.Scancode.LShift] = KeyCode.LeftShift;
		keys[(int)SDL.Scancode.RShift] = KeyCode.RightShift;
		keys[(int)SDL.Scancode.LCtrl] = KeyCode.LeftControl;
		keys[(int)SDL.Scancode.RCtrl] = KeyCode.RightControl;
		keys[(int)SDL.Scancode.LAlt] = KeyCode.LeftAlt;
		keys[(int)SDL.Scancode.RAlt] = KeyCode.RightAlt;

		keys[(int)SDL.Scancode.Minus] = KeyCode.Minus;
		keys[(int)SDL.Scancode.Equals] = KeyCode.Equals;
		keys[(int)SDL.Scancode.Leftbracket] = KeyCode.LeftBracket;
		keys[(int)SDL.Scancode.Rightbracket] = KeyCode.RightBracket;
		keys[(int)SDL.Scancode.Backslash] = KeyCode.Backslash;
		keys[(int)SDL.Scancode.Semicolon] = KeyCode.Semicolon;
		keys[(int)SDL.Scancode.Apostrophe] = KeyCode.Apostrophe;
		keys[(int)SDL.Scancode.Grave] = KeyCode.Grave;
		keys[(int)SDL.Scancode.Comma] = KeyCode.Comma;
		keys[(int)SDL.Scancode.Period] = KeyCode.Period;
		keys[(int)SDL.Scancode.Slash] = KeyCode.Slash;

		return keys;
	}
}
