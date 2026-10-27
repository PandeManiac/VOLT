using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanSwapchainMaintenance1InstanceExtensionTests
{
	private const string Capabilities2 =
		VulkanSwapchainMaintenance1Extensions.GetSurfaceCapabilities2;

	private const string KhrSurface =
		VulkanSwapchainMaintenance1Extensions.KhrSurfaceMaintenance1;

	private const string ExtSurface =
		VulkanSwapchainMaintenance1Extensions.ExtSurfaceMaintenance1;

	[Fact]
	public void NoCapabilityQuery_SelectsNothing()
	{
		string[] extensions = Select(KhrSurface, ExtSurface);

		Assert.Empty(extensions);
	}

	[Fact]
	public void NoSurfaceMaintenance_SelectsNothing()
	{
		string[] extensions = Select(Capabilities2);

		Assert.Empty(extensions);
	}

	[Fact]
	public void KhrSurfaceOnly_SelectsCapabilities2ThenKhr()
	{
		string[] extensions = Select(Capabilities2, KhrSurface);

		Assert.Equal([Capabilities2, KhrSurface], extensions);
	}

	[Fact]
	public void ExtSurfaceOnly_SelectsCapabilities2ThenExt()
	{
		string[] extensions = Select(Capabilities2, ExtSurface);

		Assert.Equal([Capabilities2, ExtSurface], extensions);
	}

	[Fact]
	public void BothSurfaceFlavors_SelectsAllThreeWithoutDuplicates()
	{
		string[] extensions = Select(Capabilities2, KhrSurface, ExtSurface);

		Assert.Equal(
			[Capabilities2, KhrSurface, ExtSurface],
			extensions);

		Assert.Equal(
			extensions.Length,
			extensions.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void UnrelatedExtensions_AreNeverSelected()
	{
		string[] extensions = Select(
			Capabilities2,
			KhrSurface,
			ExtSurface,
			"VK_KHR_swapchain",
			"VK_EXT_surface_maintenance1_extra");

		Assert.DoesNotContain("VK_KHR_swapchain", extensions);
		Assert.DoesNotContain(
			"VK_EXT_surface_maintenance1_extra",
			extensions);
	}

	[Fact]
	public void NullPredicate_IsRejected()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanSwapchainMaintenance1Extensions
				.SelectInstanceExtensions(null!));
	}

	private static string[] Select(params string[] supported)
	{
		HashSet<string> available = new(
			supported,
			StringComparer.Ordinal);

		return VulkanSwapchainMaintenance1Extensions
			.SelectInstanceExtensions(available.Contains);
	}
}
