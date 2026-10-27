namespace Volt.Ui;

/// <summary>
/// Describes how an image fills the area it is drawn into.
/// </summary>
public enum UiImageScale
{
	/// <summary>
	/// The image fills the whole area. The aspect ratio is not
	/// preserved, so the image is distorted.
	/// </summary>
	Stretch = 0,

	/// <summary>
	/// The image is scaled to the largest size that fits inside the
	/// area without distortion and is centered in it.
	/// </summary>
	Fit = 1,

	/// <summary>
	/// The image is scaled to the smallest size that covers the area
	/// without distortion and is centered in it, so it extends beyond
	/// the area on one axis.
	/// </summary>
	Fill = 2
}
