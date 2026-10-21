namespace Volt.Platform.Input;

/// <summary>
/// Identifies a physical key by its position on a US-layout keyboard.
/// </summary>
/// <remarks>
/// <para>
/// The values are logical identifiers rather than native codes: the
/// platform backend translates its own scancodes into these members,
/// so no SDL type or numeric scancode is part of this contract.
/// Physical positions are used instead of layout-dependent keycodes,
/// so the key that sits where <c>W</c> is on a US layout keeps the
/// same <see cref="W"/> identity on every keyboard layout.
/// </para>
/// <para>
/// The members are dense and start at one, which lets a frame
/// snapshot pack every key into a fixed bitset. <see cref="Unknown"/>
/// is zero and is never reported as down, pressed, or released, so a
/// default <see cref="KeyCode"/> and any out-of-range cast are inert.
/// </para>
/// </remarks>
public enum KeyCode
{
	/// <summary>
	/// The key is not recognized, or no key was specified.
	/// </summary>
	Unknown = 0,

	A = 1,
	B = 2,
	C = 3,
	D = 4,
	E = 5,
	F = 6,
	G = 7,
	H = 8,
	I = 9,
	J = 10,
	K = 11,
	L = 12,
	M = 13,
	N = 14,
	O = 15,
	P = 16,
	Q = 17,
	R = 18,
	S = 19,
	T = 20,
	U = 21,
	V = 22,
	W = 23,
	X = 24,
	Y = 25,
	Z = 26,

	/// <summary>The <c>0</c> key above the letter row.</summary>
	Number0 = 27,
	Number1 = 28,
	Number2 = 29,
	Number3 = 30,
	Number4 = 31,
	Number5 = 32,
	Number6 = 33,
	Number7 = 34,
	Number8 = 35,
	Number9 = 36,

	F1 = 37,
	F2 = 38,
	F3 = 39,
	F4 = 40,
	F5 = 41,
	F6 = 42,
	F7 = 43,
	F8 = 44,
	F9 = 45,
	F10 = 46,
	F11 = 47,
	F12 = 48,

	Up = 49,
	Down = 50,
	Left = 51,
	Right = 52,

	Escape = 53,
	Space = 54,
	Enter = 55,
	Tab = 56,
	Backspace = 57,
	Delete = 58,
	Insert = 59,
	Home = 60,
	End = 61,
	PageUp = 62,
	PageDown = 63,

	LeftShift = 64,
	RightShift = 65,
	LeftControl = 66,
	RightControl = 67,
	LeftAlt = 68,
	RightAlt = 69,

	Minus = 70,
	Equals = 71,
	LeftBracket = 72,
	RightBracket = 73,
	Backslash = 74,
	Semicolon = 75,
	Apostrophe = 76,
	Grave = 77,
	Comma = 78,
	Period = 79,
	Slash = 80
}
