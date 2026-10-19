using System.Diagnostics;
using System.Globalization;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Events;
using Volt.Platform.Windows;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Demonstrates three meshes, three draw paths, and one shared
/// graphics pipeline using VOLT's Vulkan mesh APIs.
/// </summary>
internal static class Program
{
	private const string ValidationArgument = "--validation";
	private const string FramesArgument = "--frames";

	private const string SwapchainExtension = "VK_KHR_swapchain";
	private const string GetSurfaceCapabilities2Extension =
		"VK_KHR_get_surface_capabilities2";
	private const string KhrSurfaceMaintenance1Extension =
		"VK_KHR_surface_maintenance1";
	private const string ExtSurfaceMaintenance1Extension =
		"VK_EXT_surface_maintenance1";

	private const int MaxFramesInFlight = 2;
	private const int MinimizedPollMilliseconds = 16;

	/// <summary>
	/// Fails a bounded run when no frame can be presented for this long.
	/// This prevents a minimized or otherwise unavailable window from
	/// hanging an automated run forever.
	/// </summary>
	private static readonly TimeSpan BoundedStallTimeout =
		TimeSpan.FromSeconds(10.0);

	[STAThread]
	private static int Main(string[] args)
	{
		if (!TryParseOptions(args, out SampleOptions options, out string error))
		{
			Console.Error.WriteLine(error);
			Console.Error.WriteLine(
				"Usage: Volt.VulkanMeshDemo [--validation] [--frames N]");

			return 2;
		}

		try
		{
			Run(options);
			return 0;
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(exception);
			return 1;
		}
	}

	private static void Run(SampleOptions options)
	{
		WindowDescription description = new(
			Title: "VOLT - Vulkan Mesh Demo",
			Size: new WindowSize(1280, 720),
			Resizable: true,
			VulkanCompatible: true);

		using Window window = new(description);

		List<string> extensions =
		[
			.. VulkanWindowSupport.GetRequiredInstanceExtensions(window)
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
			ApplicationName: "VOLT Vulkan Mesh Demo",
			EnableValidation: options.Validation);

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

		string maintenanceExtension =
			RequireSwapchainMaintenance1Extension(
				VulkanLogicalDevice
					.GetSupportedSwapchainMaintenance1Extension(
						instance,
						selectedPhysicalDevice));

		string[] deviceExtensions =
		[
			SwapchainExtension,
			maintenanceExtension
		];

		using VulkanLogicalDevice device = new(
			instance,
			selectedPhysicalDevice,
			selectedQueueFamily,
			deviceExtensions,
			enableSwapchainMaintenance1: true);

		using VulkanCommandPool commandPool = new(
			instance,
			device);

		using VulkanCommandBuffers commandBuffers = new(
			instance,
			device,
			commandPool,
			MaxFramesInFlight);

		using DemoMeshes meshes = DemoMeshes.Create(
			instance,
			device,
			selectedPhysicalDevice);

		string shaderDirectory = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders");

		Console.WriteLine("VOLT Vulkan Mesh Demo");
		Console.WriteLine("=====================");
		Console.WriteLine($"GPU: {candidates[selectedIndex].Name}");
		Console.WriteLine($"Validation: {options.Validation}");
		Console.WriteLine(
			$"Swapchain maintenance: {maintenanceExtension}");

		foreach (MeshGeometry geometry in meshes.Geometries)
		{
			string indexDescription = geometry.IndexFormat switch
			{
				MeshIndexFormat.None => "no indices",
				MeshIndexFormat.UInt16 => "uint16 indices",
				MeshIndexFormat.UInt32 => "uint32 indices",
				_ => "unknown indices"
			};

			Console.WriteLine(
				$"  {geometry.Name}: " +
				$"{geometry.Vertices.Length} vertices, " +
				$"{indexDescription}");
		}

		Console.WriteLine(
			options.FrameLimit is int frameLimit
				? $"Rendering {frameLimit} bounded frames."
				: "Rendering interactively. Close the window to exit.");

		RenderLoop(
			window,
			instance,
			selectedPhysicalDevice,
			device,
			surface,
			commandBuffers,
			meshes,
			options,
			Path.Combine(shaderDirectory, "mesh.vert.spv"),
			Path.Combine(shaderDirectory, "mesh.frag.spv"));

		Console.WriteLine("Vulkan mesh demo exited successfully.");
	}

	private static void RenderLoop(
		Window window,
		VulkanInstance instance,
		PhysicalDevice physicalDevice,
		VulkanLogicalDevice device,
		VulkanSurface surface,
		VulkanCommandBuffers commandBuffers,
		DemoMeshes meshes,
		SampleOptions options,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		MeshSwapchainResources? resources = null;

		Stopwatch statisticsClock = Stopwatch.StartNew();
		Stopwatch stallClock = Stopwatch.StartNew();

		WindowSize lastFramebufferSize = default;
		bool recreationRequested = true;

		int framesSinceReport = 0;
		int presentedFrames = 0;

		Exception? failure = null;

		try
		{
			while (true)
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
					if (options.FrameLimit.HasValue)
					{
						throw new InvalidOperationException(
							$"The window closed after {presentedFrames} " +
							$"of {options.FrameLimit.Value} requested frames.");
					}

					break;
				}

				if (options.FrameLimit.HasValue &&
					stallClock.Elapsed >= BoundedStallTimeout)
				{
					throw new TimeoutException(
						$"No frame was presented for " +
						$"{BoundedStallTimeout.TotalSeconds:F0} seconds " +
						$"after {presentedFrames} of " +
						$"{options.FrameLimit.Value} frames. " +
						"The window may be minimized or presentation " +
						"may be unavailable.");
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

					resources = new MeshSwapchainResources(
						instance,
						physicalDevice,
						device,
						surface,
						meshes,
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
						$"format: {resources.Swapchain.ImageFormat}, " +
						$"frames in flight: " +
						$"{resources.Frames.FramesInFlight}");
				}

				MeshSwapchainResources active = resources;

				VulkanSwapchainFrameResult frame =
					active.Frames.Draw(active.RecordFrame);

				if (frame.RequiresRecreation)
				{
					recreationRequested = true;
				}

				if (!frame.Submitted)
				{
					continue;
				}

				if (!frame.PresentationAccepted)
				{
					Console.Error.WriteLine(
						"Presentation was not accepted; " +
						"recreating the swapchain.");

					continue;
				}

				presentedFrames++;
				framesSinceReport++;
				stallClock.Restart();

				if (statisticsClock.Elapsed.TotalSeconds >= 1.0)
				{
					double fps =
						framesSinceReport /
						statisticsClock.Elapsed.TotalSeconds;

					Console.WriteLine(
						$"Frames presented: {presentedFrames:N0}, " +
						$"FPS: {fps:F1}");

					framesSinceReport = 0;
					statisticsClock.Restart();
				}

				if (options.FrameLimit is int frameLimit &&
					presentedFrames >= frameLimit)
				{
					break;
				}
			}

			if (options.FrameLimit is int requiredFrames &&
				presentedFrames != requiredFrames)
			{
				throw new InvalidOperationException(
					$"Presented {presentedFrames} frames, but " +
					$"{requiredFrames} were requested.");
			}

			Console.WriteLine(
				$"Presented {presentedFrames} accepted frames.");
		}

		catch (Exception exception)
		{
			failure = exception;
			throw;
		}

		finally
		{
			if (failure is null)
			{
				device.WaitIdle();
				resources?.Dispose();
			}

			else
			{
				// A failed frame can leave image ownership unresolved.
				// Attempt cleanup, but never hide the original failure.
				try
				{
					device.WaitIdle();
				}

				catch (Exception cleanupException)
				{
					Console.Error.WriteLine(
						$"Cleanup wait after failure: " +
						$"{cleanupException.Message}");
				}

				try
				{
					resources?.Dispose();
				}

				catch (Exception cleanupException)
				{
					Console.Error.WriteLine(
						$"Cleanup after failure: " +
						$"{cleanupException.Message}");
				}
			}
		}
	}

	/// <summary>
	/// Requires a supported swapchain maintenance1 extension with
	/// presentation fences.
	/// </summary>
	/// <remarks>
	/// The frame scheduler does not safely retire outstanding
	/// presentation operations on the legacy path, and device idle
	/// does not establish presentation retirement. This sample
	/// therefore refuses to start without the KHR or EXT variant
	/// rather than using a speculative fallback.
	/// </remarks>
	internal static string RequireSwapchainMaintenance1Extension(
		string? extension)
	{
		if (extension is null)
		{
			throw new NotSupportedException(
				"Volt.VulkanMeshDemo requires a KHR or EXT " +
				"swapchain maintenance1 implementation with " +
				"presentation fences. Neither " +
				"VK_KHR_swapchain_maintenance1 nor " +
				"VK_EXT_swapchain_maintenance1 is available on the " +
				"selected device. The legacy presentation path does " +
				"not establish presentation retirement safely.");
		}

		return extension;
	}

	internal static bool TryParseOptions(
		string[] args,
		out SampleOptions options,
		out string error)
	{
		bool validation = false;
		int? frameLimit = null;

		for (int index = 0; index < args.Length; index++)
		{
			string argument = args[index];

			switch (argument)
			{
				case ValidationArgument:
					if (validation)
					{
						options = default;
						error = "The --validation argument was supplied twice.";
						return false;
					}

					validation = true;
					break;

				case FramesArgument:
					if (frameLimit.HasValue)
					{
						options = default;
						error = "The --frames argument was supplied twice.";
						return false;
					}

					if (index + 1 >= args.Length)
					{
						options = default;
						error = "The --frames argument requires a value.";
						return false;
					}

					index++;

					if (!int.TryParse(
							args[index],
							NumberStyles.None,
							CultureInfo.InvariantCulture,
							out int parsedFrames)
						|| parsedFrames <= 0)
					{
						options = default;
						error = "The --frames value must be a positive integer.";
						return false;
					}

					frameLimit = parsedFrames;
					break;

				default:
					options = default;
					error = $"Unknown argument: {argument}";
					return false;
			}
		}

		options = new SampleOptions(validation, frameLimit);
		error = string.Empty;
		return true;
	}

}

internal readonly record struct SampleOptions(
	bool Validation,
	int? FrameLimit);
