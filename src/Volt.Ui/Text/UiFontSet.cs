namespace Volt.Ui.Text;

/// <summary>
/// The logical font roles used by a theme at one display scale.
/// </summary>
public sealed class UiFontSet
{
	public UiFontSet(float dpiScale)
		: this(dpiScale, uiScale: 1f)
	{
	}

	/// <summary>
	/// Creates a font set at a display scale and an interface scale.
	/// </summary>
	/// <remarks>
	/// The two factors are separate on purpose. The display scale maps
	/// logical units to physical pixels and matches the monitor's DPI;
	/// the interface scale is a reading preference that enlarges text
	/// and controls together. Fonts are rasterized at the product of
	/// both, so a 150% interface on a 1.5x display stays crisp instead
	/// of being resampled from a 100% atlas.
	/// </remarks>
	public UiFontSet(float dpiScale, float uiScale)
	{
		if (!float.IsFinite(dpiScale) || dpiScale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(dpiScale));
		}

		if (!float.IsFinite(uiScale) || uiScale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(uiScale));
		}

		DpiScale = dpiScale;
		UiScale = uiScale;

		float raster = dpiScale * uiScale;

		Small = new UiFont(UiFontKind.Sans, 13f, raster);
		Body = new UiFont(UiFontKind.Sans, 15f, raster);
		Large = new UiFont(UiFontKind.Sans, 17f, raster);
		Heading = new UiFont(UiFontKind.Sans, 19f, raster);
		Title = new UiFont(UiFontKind.Sans, 23f, raster);
		PanelHeader = new UiFont(UiFontKind.Sans, 14f, raster);
		MonoSmall = new UiFont(UiFontKind.Monospace, 13f, raster);
		Mono = new UiFont(UiFontKind.Monospace, 14f, raster);
	}

	public float DpiScale { get; }

	/// <summary>
	/// Gets the interface scale these fonts were created for.
	/// </summary>
	public float UiScale { get; }

	public UiFont Small { get; }

	public UiFont Body { get; }

	public UiFont Large { get; }

	public UiFont Heading { get; }

	/// <summary>
	/// Gets the font for the application wordmark and splash headings.
	/// </summary>
	public UiFont Title { get; }

	/// <summary>
	/// Gets the font for dock tab and panel header captions.
	/// </summary>
	public UiFont PanelHeader { get; }

	public UiFont MonoSmall { get; }

	public UiFont Mono { get; }

	/// <summary>
	/// Creates the same roles at another display scale.
	/// </summary>
	public UiFontSet WithScale(float dpiScale)
	{
		return new UiFontSet(dpiScale, UiScale);
	}

	/// <summary>
	/// Creates the same roles at another interface scale.
	/// </summary>
	public UiFontSet WithUiScale(float uiScale)
	{
		return new UiFontSet(DpiScale, uiScale);
	}

	/// <summary>
	/// Enumerates every font whose atlas the renderer must upload.
	/// </summary>
	public IEnumerable<UiFont> Enumerate()
	{
		yield return Small;
		yield return Body;
		yield return Large;
		yield return Heading;
		yield return Title;
		yield return PanelHeader;
		yield return MonoSmall;
		yield return Mono;
	}
}
