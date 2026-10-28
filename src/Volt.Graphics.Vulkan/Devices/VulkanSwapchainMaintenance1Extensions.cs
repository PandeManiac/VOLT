using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Devices;

/// <summary>
/// Selects the instance extensions that the optional swapchain
/// maintenance feature depends on.
/// </summary>
/// <remarks>
/// <para>
/// <c>VK_KHR_swapchain_maintenance1</c> and
/// <c>VK_EXT_swapchain_maintenance1</c> both require
/// <c>VK_KHR_get_surface_capabilities2</c> together with the surface
/// maintenance extension that matches the device extension flavor. A
/// physical device can therefore implement the feature and still be
/// reported as unsupported when the instance never enabled those
/// extensions, so an application that wants presentation fences has to
/// select them before creating the instance.
/// </para>
/// <para>
/// Applications that only need the feature for convenience can enable
/// whatever subset is available and continue without it. This helper
/// reports the subset that is worth enabling; the device-level query
/// then decides whether the feature is actually usable.
/// </para>
/// </remarks>
public static class VulkanSwapchainMaintenance1Extensions
{
	/// <summary>
	/// The instance extension that exposes the surface capability
	/// queries used by the maintenance feature.
	/// </summary>
	public const string GetSurfaceCapabilities2 =
		"VK_KHR_get_surface_capabilities2";

	/// <summary>
	/// The surface extension that pairs with the KHR device
	/// extension.
	/// </summary>
	public const string KhrSurfaceMaintenance1 =
		"VK_KHR_surface_maintenance1";

	/// <summary>
	/// The surface extension that pairs with the EXT device
	/// extension.
	/// </summary>
	public const string ExtSurfaceMaintenance1 =
		"VK_EXT_surface_maintenance1";

	/// <summary>
	/// Selects the instance extensions to enable for the feature.
	/// </summary>
	/// <param name="isSupported">
	/// Reports whether an instance extension is available. It must not
	/// return a result that changes between calls.
	/// </param>
	/// <returns>
	/// The extensions to enable, in dependency order, or an empty array
	/// when the feature cannot be enabled on this platform.
	/// </returns>
	public static string[] SelectInstanceExtensions(
		Func<string, bool> isSupported)
	{
		ArgumentNullException.ThrowIfNull(isSupported);

		if (!isSupported(GetSurfaceCapabilities2))
		{
			return [];
		}

		bool khrSurfaceSupported = isSupported(KhrSurfaceMaintenance1);
		bool extSurfaceSupported = isSupported(ExtSurfaceMaintenance1);

		if (!khrSurfaceSupported && !extSurfaceSupported)
		{
			return [];
		}

		List<string> extensions = [GetSurfaceCapabilities2];

		if (khrSurfaceSupported)
		{
			extensions.Add(KhrSurfaceMaintenance1);
		}

		if (extSurfaceSupported)
		{
			extensions.Add(ExtSurfaceMaintenance1);
		}

		return [.. extensions];
	}

	/// <summary>
	/// Selects the feature's instance extensions that this loader
	/// advertises.
	/// </summary>
	/// <remarks>
	/// Safe to call before creating a Vulkan instance. It does not
	/// enable anything by itself.
	/// </remarks>
	public static string[] GetSupportedInstanceExtensions()
	{
		return SelectInstanceExtensions(
			VulkanInstance.IsExtensionSupported);
	}
}
