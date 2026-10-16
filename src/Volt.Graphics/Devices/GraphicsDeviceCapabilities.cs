
namespace Volt.Graphics.Devices;

/// <summary>
/// Describes the capabilities of a selected graphics device.
/// </summary>
/// <remarks>
/// Values must be obtained from the actual graphics adapter
/// and backend rather than inferred from the graphics API.
///
/// This type is an immutable snapshot of device capabilities.
/// It does not own or initialize graphics resources.
///
/// A default instance does not describe a valid device.
/// </remarks>
public readonly record struct GraphicsDeviceCapabilities(
    GraphicsBackend Backend,
    string AdapterName,
    uint MaxTexture2DDimension,
    uint MaxColorAttachments,
    bool SupportsWireframe,
    bool SupportsTimestampQueries)
{
    /// <summary>
    /// Validates the reported device capabilities.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A numeric capability or backend identifier is invalid.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The adapter name is missing or whitespace.
    /// </exception>
    public void Validate()
    {
        if (Backend == GraphicsBackend.Unknown
            || !Enum.IsDefined(Backend))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Backend),
                Backend,
                "A valid graphics backend is required.");
        }

        if (string.IsNullOrWhiteSpace(AdapterName))
        {
            throw new ArgumentException(
                "An adapter name is required.",
                nameof(AdapterName));
        }

        if (MaxTexture2DDimension == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxTexture2DDimension),
                MaxTexture2DDimension,
                "The maximum texture dimension must be positive.");
        }

        if (MaxColorAttachments == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxColorAttachments),
                MaxColorAttachments,
                "The maximum color attachment count must be positive.");
        }
    }
}
