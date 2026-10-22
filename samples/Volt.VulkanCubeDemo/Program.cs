using System.Diagnostics;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Events;
using Volt.Platform.Windows;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Demonstrates continuously rendering a rotating, depth-tested
/// cube using VOLT's Vulkan and platform infrastructure.
/// </summary>
internal static class Program
{
	private const string ValidationArgument = "--validation";
	private const string SwapchainExtension = "VK_KHR_swapchain";
	private const string GetSurfaceCapabilities2Extension =
		"VK_KHR_get_surface_capabilities2";
	private const string KhrSurfaceMaintenance1Extension =
		"VK_KHR_surface_maintenance1";
	private const string ExtSurfaceMaintenance1Extension =
		"VK_EXT_surface_maintenance1";

	private const int MaxFramesInFlight = 2;
	private const int MinimizedPollMilliseconds = 16;

	[STAThread]
	private static int Main(string[] args)
	{
		if (args.Length > 1
			|| (args.Length == 1 && args[0] != ValidationArgument))
		{
			Console.Error.WriteLine(
				"Usage: Volt.VulkanCubeDemo [--validation]");

			return 2;
		}

		try
		{
			Run(args.Length == 1);
			return 0;
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	private static void Run(bool validation)
	{
		WindowDescription description = new(
			Title: "VOLT - Rotating Vulkan Cube",
			Size: new WindowSize(1280, 720),
			Resizable: true,
			VulkanCompatible: true);

		using Window window = new(description);

		List<string> extensions =
		[
			.. VulkanWindowSupport.GetRequiredInstanceExtensions(
				window)
		];

		if (VulkanInstance.IsExtensionSupported(
				GetSurfaceCapabilities2Extension))
		{
			bool khrSurfaceSupported =
				VulkanInstance.IsExtensionSupported(
					KhrSurfaceMaintenance1Extension);

			bool extSurfaceSupported =
				VulkanInstance.IsExtensionSupported(
					ExtSurfaceMaintenance1Extension);

			if (khrSurfaceSupported || extSurfaceSupported)
			{
				if (!extensions.Contains(GetSurfaceCapabilities2Extension))
				{
					extensions.Add(GetSurfaceCapabilities2Extension);
				}

				if (khrSurfaceSupported &&
					!extensions.Contains(KhrSurfaceMaintenance1Extension))
				{
					extensions.Add(KhrSurfaceMaintenance1Extension);
				}

				if (extSurfaceSupported &&
					!extensions.Contains(ExtSurfaceMaintenance1Extension))
				{
					extensions.Add(ExtSurfaceMaintenance1Extension);
				}
			}
		}

		VulkanInstanceDescription instanceDescription = new(
			ApplicationName: "VOLT Vulkan Cube",
			EnableValidation: validation);

		using VulkanInstance instance = new(
			instanceDescription,
			extensions);

		using VulkanSurface surface = new(instance, window);

		PhysicalDevice[] physicalDevices =
			instance.EnumeratePhysicalDevices();

		VulkanPhysicalDeviceInfo[] candidates =
			new VulkanPhysicalDeviceInfo[physicalDevices.Length];

		for (int index = 0; index < physicalDevices.Length; index++)
		{
			PhysicalDevice physicalDevice = physicalDevices[index];

			VulkanPhysicalDeviceInfo information =
				VulkanPhysicalDeviceInspector.Inspect(
					instance,
					physicalDevice);

			uint? queueFamily =
				VulkanPresentationSupport
					.FindGraphicsPresentationQueueFamily(
						instance,
						physicalDevice,
						surface);

			bool presentationSupported = false;

			if (queueFamily.HasValue)
			{
				VulkanSwapchainSupport support =
					VulkanSwapchainSupport.Query(
						instance,
						physicalDevice,
						surface);

				presentationSupported = support.CanCreateColorSwapchain;
			}

			candidates[index] = information with
			{
				GraphicsQueueFamilyIndex =
					presentationSupported ? queueFamily : null
			};
		}

		int selectedIndex =
			VulkanPhysicalDeviceSelector.SelectIndex(
				candidates,
				requiredApiMajor: instanceDescription.ApiMajor,
				requiredApiMinor: instanceDescription.ApiMinor)
			?? throw new NotSupportedException(
				"No graphics and presentation capable GPU was found.");

		PhysicalDevice selectedPhysicalDevice =
			physicalDevices[selectedIndex];

		uint selectedQueueFamily =
			candidates[selectedIndex].GraphicsQueueFamilyIndex
			?? throw new InvalidOperationException(
				"The selected GPU has no presentation queue.");

		Console.WriteLine("VOLT Rotating Vulkan Cube");
		Console.WriteLine("========================");
		Console.WriteLine($"GPU: {candidates[selectedIndex].Name}");
		Console.WriteLine($"Validation: {validation}");

		string? maintenanceExtension =
			VulkanLogicalDevice.GetSupportedSwapchainMaintenance1Extension(
				instance,
				selectedPhysicalDevice);

		string[] deviceExtensions = maintenanceExtension is not null
			? [SwapchainExtension, maintenanceExtension]
			: [SwapchainExtension];

		using VulkanLogicalDevice device = new(
			instance,
			selectedPhysicalDevice,
			selectedQueueFamily,
			deviceExtensions,
			enableSwapchainMaintenance1:
				maintenanceExtension is not null);

		Console.WriteLine(
			$"Swapchain maintenance: " +
			$"{maintenanceExtension ?? "unavailable"}");

		using VulkanCommandPool commandPool = new(
			instance,
			device);

		using VulkanCommandBuffers commandBuffers = new(
			instance,
			device,
			commandPool,
			MaxFramesInFlight);

		string shaderDirectory = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders");

		Console.WriteLine(
			"Rendering cube. Resize, minimize, or close the window.");

		RenderLoop(
			window,
			instance,
			selectedPhysicalDevice,
			device,
			surface,
			commandBuffers,
			Path.Combine(shaderDirectory, "cube.vert.spv"),
			Path.Combine(shaderDirectory, "cube.frag.spv"));

		Console.WriteLine("Vulkan cube demo exited successfully.");
	}

	private static void RenderLoop(
		Window window,
		VulkanInstance instance,
		PhysicalDevice physicalDevice,
		VulkanLogicalDevice device,
		VulkanSurface surface,
		VulkanCommandBuffers commandBuffers,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		CubeSwapchainResources? resources = null;

		Stopwatch clock = Stopwatch.StartNew();
		Stopwatch statisticsClock = Stopwatch.StartNew();

		WindowSize lastFramebufferSize = default;
		bool recreationRequested = true;

		int framesSinceReport = 0;
		long totalFrames = 0;

		try
		{
			while (!window.ShouldClose)
			{
				window.PollEvents();

				while (window.TryDequeueEvent(out WindowEvent windowEvent))
				{
					if (windowEvent.Type is WindowEventType.PixelSizeChanged
						or WindowEventType.Resized)
					{
						recreationRequested = true;
					}
				}

				if (window.ShouldClose)
				{
					break;
				}

				WindowSize framebufferSize = window.PixelSize;

				if (!framebufferSize.IsPositive)
				{
					recreationRequested = true;
					Thread.Sleep(MinimizedPollMilliseconds);
					continue;
				}

				if (resources is null
						|| recreationRequested
						|| framebufferSize != lastFramebufferSize)
				{
					if (resources is not null)
					{
						device.WaitIdle();
						resources.Dispose();
						resources = null;
					}

					resources = new CubeSwapchainResources(
						instance,
						physicalDevice,
						device,
						surface,
						commandBuffers,
						framebufferSize,
						MaxFramesInFlight,
						vertexShaderPath,
						fragmentShaderPath);

					lastFramebufferSize = framebufferSize;
					recreationRequested = false;

					Console.WriteLine(
						$"Swapchain: {resources.Swapchain.Extent.Width}x" +
						$"{resources.Swapchain.Extent.Height}, " +
						$"images: {resources.Swapchain.ImageCount}, " +
						$"depth: {resources.Renderer.DepthFormat}, " +
						$"frames in flight: {resources.Frames.FramesInFlight}");
				}

				CubeSwapchainResources active = resources;

				VulkanSwapchainFrameResult frame =
					active.Frames.Draw(
						(commandBuffer, imageIndex) =>
						{
							active.Renderer.Record(
								commandBuffer,
								imageIndex,
								(float)clock.Elapsed.TotalSeconds * 0.85f);
						});

				if (frame.RequiresRecreation)
				{
					recreationRequested = true;
				}

				if (!frame.Submitted)
				{
					continue;
				}

				totalFrames++;
				framesSinceReport++;

				if (statisticsClock.Elapsed.TotalSeconds >= 1.0)
				{
					double fps =
						framesSinceReport /
						statisticsClock.Elapsed.TotalSeconds;

					Console.WriteLine(
						$"Frames: {totalFrames:N0}, FPS: {fps:F1}");

					framesSinceReport = 0;
					statisticsClock.Restart();
				}
			}
		}

		finally
		{
			// Do not release generation resources if GPU completion
			// cannot be established. Presentation retirement remains
			// the frame scheduler's responsibility.
			device.WaitIdle();
			resources?.Dispose();
		}
	}
}
