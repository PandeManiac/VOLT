
namespace Volt.Graphics.Vulkan.Instance;

/// <summary>
/// Describes the configuration requested when creating a Vulkan instance.
/// </summary>
/// <remarks>
/// The requested API version must be supported by the Vulkan loader.
/// Validation layers are requested explicitly rather than enabled implicitly.
/// </remarks>
public readonly record struct VulkanInstanceDescription(
	string ApplicationName,
	bool EnableValidation = false,
	uint ApiMajor = 1,
	uint ApiMinor = 2)
{
	/// <summary>
	/// Validates the instance configuration before native Vulkan initialization.
	/// </summary>
	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(ApplicationName))
		{
			throw new ArgumentException(
				"A nonempty application name is required.",
				nameof(ApplicationName));
		}

		if (ApplicationName.Contains('\0'))
		{
			throw new ArgumentException(
				"The application name cannot contain null characters.",
				nameof(ApplicationName));
		}

		if (ApiMajor is < 1 or > 127)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ApiMajor),
				ApiMajor,
				"The Vulkan API major version must be between 1 and 127.");
		}

		if (ApiMinor > 1023)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ApiMinor),
				ApiMinor,
				"The Vulkan API minor version cannot exceed 1023.");
		}
	}
}
