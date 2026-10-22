
namespace Volt.Graphics.Devices;

/// <summary>
/// Identifies the graphics API used by a graphics device.
/// </summary>
public enum GraphicsBackend
{
	/// <summary>
	/// No graphics backend has been selected.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// The Vulkan graphics API.
	/// Supported on Linux and Windows.
	/// </summary>
	Vulkan = 1,

	/// <summary>
	/// The Direct3D 12 graphics API.
	/// Supported on Windows.
	/// </summary>
	Direct3D12 = 2
}
