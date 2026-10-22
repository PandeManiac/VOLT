
namespace Volt.Graphics.Devices;

/// <summary>
/// Describes the configuration requested when creating
/// a graphics device.
/// </summary>
/// <remarks>
/// This type contains backend-independent configuration.
/// It does not create or own graphics resources.
///
/// Backend availability, device compatibility, and support
/// for optional features are checked during device creation.
/// </remarks>
public readonly record struct GraphicsDeviceDescription(
	GraphicsBackend Backend,
	bool EnableValidation = false)
{
	/// <summary>
	/// Validates the backend selection.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The backend is unknown or undefined.
	/// </exception>
	public void Validate()
	{
		if (Backend == GraphicsBackend.Unknown
			|| !Enum.IsDefined(Backend))
		{
			throw new ArgumentOutOfRangeException(
				nameof(Backend),
				Backend,
				"A defined graphics backend must be selected.");
		}
	}
}
