
namespace Volt.Graphics.Devices;

/// <summary>
/// Represents an initialized graphics device.
/// </summary>
/// <remarks>
/// Implementations own backend-specific graphics resources
/// and are responsible for releasing them during disposal.
///
/// Device creation is handled separately, allowing backend
/// selection and initialization to remain independent of
/// the graphics device contract.
///
/// All device operations must respect the capabilities
/// reported by the selected backend.
/// </remarks>
public interface IGraphicsDevice : IDisposable
{
	/// <summary>
	/// Gets the graphics API used by this device.
	/// </summary>
	GraphicsBackend Backend { get; }

	/// <summary>
	/// Gets the capabilities of the selected graphics adapter
	/// and backend implementation.
	/// </summary>
	GraphicsDeviceCapabilities Capabilities { get; }
}
