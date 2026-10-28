using System.Numerics;

namespace Volt.Ui;

/// <summary>
/// An eight-bit-per-channel, straight-alpha color.
/// </summary>
/// <remarks>
/// The UI framework uses this value type instead of a graphics-API
/// color so layout, styling, and draw-command generation stay
/// independent of any renderer.
/// </remarks>
public readonly record struct UiColor(byte R, byte G, byte B, byte A = 255)
{
	public static UiColor Transparent => new(0, 0, 0, 0);

	public static UiColor White => new(255, 255, 255);

	public static UiColor Black => new(0, 0, 0);

	/// <summary>
	/// Creates a color from 0xRRGGBB.
	/// </summary>
	public static UiColor FromRgb(uint rgb)
	{
		return new UiColor(
			(byte)((rgb >> 16) & 0xFF),
			(byte)((rgb >> 8) & 0xFF),
			(byte)(rgb & 0xFF));
	}

	/// <summary>
	/// Creates a color from 0xAARRGGBB.
	/// </summary>
	public static UiColor FromArgb(uint argb)
	{
		return new UiColor(
			(byte)((argb >> 16) & 0xFF),
			(byte)((argb >> 8) & 0xFF),
			(byte)(argb & 0xFF),
			(byte)((argb >> 24) & 0xFF));
	}

	public UiColor WithAlpha(byte alpha)
	{
		return new UiColor(R, G, B, alpha);
	}

	public UiColor WithAlpha(float alpha)
	{
		return WithAlpha(
			(byte)Math.Clamp((int)MathF.Round(alpha * 255f), 0, 255));
	}

	public Vector4 ToVector4()
	{
		const float scale = 1f / 255f;

		return new Vector4(
			R * scale,
			G * scale,
			B * scale,
			A * scale);
	}

	/// <summary>
	/// Multiplies alpha by the supplied factor.
	/// </summary>
	public UiColor ScaleAlpha(float factor)
	{
		return WithAlpha(A / 255f * factor);
	}

	public static UiColor Lerp(UiColor from, UiColor to, float amount)
	{
		amount = Math.Clamp(amount, 0f, 1f);

		return new UiColor(
			Interpolate(from.R, to.R, amount),
			Interpolate(from.G, to.G, amount),
			Interpolate(from.B, to.B, amount),
			Interpolate(from.A, to.A, amount));
	}

	private static byte Interpolate(byte from, byte to, float amount)
	{
		return (byte)Math.Clamp(
			(int)MathF.Round(from + ((to - from) * amount)),
			0,
			255);
	}
}
