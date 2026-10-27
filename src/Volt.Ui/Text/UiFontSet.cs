namespace Volt.Ui.Text;

/// <summary>
/// The logical font sizes used by a theme at one display scale.
/// </summary>
public sealed class UiFontSet
{
	public UiFontSet(float dpiScale)
	{
		if (!float.IsFinite(dpiScale) || dpiScale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(dpiScale));
		}

		DpiScale = dpiScale;
		Small = new UiFont(UiFontKind.Sans, 11f, dpiScale);
		Body = new UiFont(UiFontKind.Sans, 13f, dpiScale);
		Large = new UiFont(UiFontKind.Sans, 15f, dpiScale);
		Heading = new UiFont(UiFontKind.Sans, 17f, dpiScale);
		MonoSmall = new UiFont(UiFontKind.Monospace, 11f, dpiScale);
		Mono = new UiFont(UiFontKind.Monospace, 12f, dpiScale);
	}

	public float DpiScale { get; }

	public UiFont Small { get; }

	public UiFont Body { get; }

	public UiFont Large { get; }

	public UiFont Heading { get; }

	public UiFont MonoSmall { get; }

	public UiFont Mono { get; }

	/// <summary>
	/// Enumerates every font whose atlas the renderer must upload.
	/// </summary>
	public IEnumerable<UiFont> Enumerate()
	{
		yield return Small;
		yield return Body;
		yield return Large;
		yield return Heading;
		yield return MonoSmall;
		yield return Mono;
	}
}
