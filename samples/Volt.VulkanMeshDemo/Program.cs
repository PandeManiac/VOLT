using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SDL3;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Events;
using Volt.Platform.Windows;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Demonstrates three meshes, three draw paths, and one shared
/// graphics pipeline using VOLT's Vulkan mesh APIs.
/// </summary>
internal static class Program
{
	private const string ValidationArgument = "--validation";
	private const string FramesArgument = "--frames";
	private const string ResizeAtFrameArgument = "--resize-at-frame";
	private const string ResizeToArgument = "--resize-to";
	private const string SceneArgument = "--scene";
	private const string DisableDepthTestArgument = "--disable-depth-test";
	private const string CameraScriptArgument = "--camera-script";
	private const string EcsScriptArgument = "--ecs-script";
	private const string GallerySceneName = "gallery";
	private const string DepthOcclusionSceneName = "depth-occlusion";
	private const string CameraTransformsSceneName = "camera-transforms";
	private const string EcsSceneName = "ecs-scene";

	/// <summary>
	/// Upper bound for a requested resize dimension. It keeps the
	/// opt-in scenario within the limits every Vulkan surface reports.
	/// </summary>
	internal const int MaximumResizeDimension = 16384;

	/// <summary>
	/// Upper bound for a requested resize trigger frame. Bounded runs
	/// cannot reach it because the trigger must precede the frame limit.
	/// </summary>
	internal const int MaximumResizeFrame = 1_000_000;

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
	/// Accepted frames presented in a recreated swapchain generation
	/// before the post-recreation camera checkpoint is reported.
	/// </summary>
	private const int PostRecreationCheckpointFrames = 30;

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
				"Usage: Volt.VulkanMeshDemo [--validation] [--frames N] " +
				"[--scene gallery|depth-occlusion|camera-transforms|" +
				"ecs-scene] [--camera-script] [--ecs-script] " +
				"[--disable-depth-test] " +
				"[--resize-at-frame N --resize-to WIDTHxHEIGHT]");

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

		// The camera and its input sampler live for the whole
		// application, not for one swapchain generation, so a resize
		// never resets the pose. The sample supplies its documented
		// initial pose; the shared controller has no default.
		CameraController camera = new(CameraTransforms.CameraPosition);

		using CameraInput cameraInput = new(window);

		CameraScript? cameraScript = options.CameraScript
			? new CameraScript()
			: null;

		EcsScript? ecsScript = options.EcsScript
			? new EcsScript()
			: null;

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
			selectedPhysicalDevice,
			options.Scene);

		// The ECS world and its entities outlive swapchain
		// generations, so a resize never resets component values.
		using EcsDemoScene? ecsScene =
			options.Scene == DemoScene.EcsScene
				? new EcsDemoScene(
					EcsMeshResolver.CreateDefault(meshes.Geometries))
				: null;

		string shaderDirectory = Path.Combine(
			AppContext.BaseDirectory,
			"Shaders");

		Console.WriteLine("VOLT Vulkan Mesh Demo");
		Console.WriteLine("=====================");
		Console.WriteLine($"GPU: {candidates[selectedIndex].Name}");
		Console.WriteLine($"Validation: {options.Validation}");
		Console.WriteLine(
			$"Swapchain maintenance: {maintenanceExtension}");
		Console.WriteLine($"Scene: {SceneName(options.Scene)}");
		Console.WriteLine(
			$"Depth test: " +
			$"{(options.DepthTestDisabled ? "disabled" : "enabled")}");

		if (ecsScene is not null)
		{
			Console.WriteLine(
				$"ECS: {ecsScene.Extract().Length} renderable " +
				$"entities from {ecsScene.World.Count} world " +
				$"entities, camera entity {ecsScene.CameraEntity}, " +
				$"{ecsScene.CountDrawCommands()} draw commands per frame.");
		}

		if (options.Scene == DemoScene.CameraTransforms)
		{
			Console.WriteLine(
				cameraScript is null
					? "Camera: interactive (wasd, space/ctrl, mouse " +
						"after click, escape releases, shift sprints)."
					: "Camera: scripted deterministic flight.");
		}

		if (options.Scene == DemoScene.EcsScene)
		{
			Console.WriteLine(
				ecsScript is null
					? "ECS: static entity-driven scene."
					: "ECS: scripted deterministic entity and " +
						"camera mutation.");
		}

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
			camera,
			cameraInput,
			cameraScript,
			ecsScene,
			ecsScript,
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
		CameraController camera,
		CameraInput cameraInput,
		CameraScript? cameraScript,
		EcsDemoScene? ecsScene,
		EcsScript? ecsScript,
		SampleOptions options,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		MeshSwapchainResources? resources = null;

		Stopwatch statisticsClock = Stopwatch.StartNew();
		Stopwatch stallClock = Stopwatch.StartNew();
		Stopwatch frameClock = Stopwatch.StartNew();

		bool interactiveCamera =
			options.Scene == DemoScene.CameraTransforms &&
			cameraScript is null;

		bool postRecreationCheckpointLogged = false;

		WindowSize lastFramebufferSize = default;
		bool recreationRequested = true;

		int swapchainGeneration = 0;
		bool resizeRequested = false;
		bool resizeObserved = false;
		int resizeRecreationFrame = 0;

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

				float deltaSeconds =
					(float)frameClock.Elapsed.TotalSeconds;

				frameClock.Restart();

				if (interactiveCamera)
				{
					cameraInput.Update(camera, deltaSeconds);
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
					WindowSize previousFramebufferSize =
						lastFramebufferSize;

					int previousGeneration = swapchainGeneration;

					if (resources is not null)
					{
						Console.WriteLine(
							$"Retiring swapchain generation " +
							$"{previousGeneration}, " +
							$"{previousFramebufferSize.Width}x" +
							$"{previousFramebufferSize.Height}.");

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
						camera,
						ecsScene,
						commandBuffers,
						framebufferSize,
						MaxFramesInFlight,
						vertexShaderPath,
						fragmentShaderPath,
						depthTestEnabled: !options.DepthTestDisabled);

					swapchainGeneration++;

					if (IsResizeObserved(
							resizeRequested,
							resizeObserved,
							previousFramebufferSize,
							framebufferSize))
					{
						resizeObserved = true;
						resizeRecreationFrame = presentedFrames;

						WindowSize windowSize = window.Size;

						Console.WriteLine(
							$"Resize observed: framebuffer " +
							$"{framebufferSize.Width}x" +
							$"{framebufferSize.Height}, window " +
							$"{windowSize.Width}x{windowSize.Height}, " +
							$"generation {previousGeneration} -> " +
							$"{swapchainGeneration}, after " +
							$"{presentedFrames} presented frames.");
					}

					lastFramebufferSize = framebufferSize;
					recreationRequested = false;

					Console.WriteLine(
						$"Swapchain: generation {swapchainGeneration}, " +
						$"{resources.Swapchain.Extent.Width}x" +
						$"{resources.Swapchain.Extent.Height}, " +
						$"images: {resources.Swapchain.ImageCount}, " +
						$"format: {resources.Swapchain.ImageFormat}, " +
						$"depth: {resources.DepthFormat}, " +
						$"depth attachments: " +
						$"{resources.DepthAttachmentCount}, " +
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

				if (cameraScript is not null)
				{
					LogCameraCheckpoint(
						cameraScript.Advance(camera),
						camera,
						presentedFrames);
				}

				if (ecsScript is not null && ecsScene is not null)
				{
					// The mutation happens only after this frame was
					// accepted, never during command recording.
					LogEcsCheckpoint(
						ecsScript.Advance(ecsScene),
						ecsScene,
						presentedFrames);
				}

				if (ShouldRequestResize(
						presentedFrames,
						options,
						resizeRequested) &&
					options.ResizeTo is WindowSize resizeTarget)
				{
					resizeRequested = true;

					WindowSize windowSize = window.Size;

					Console.WriteLine(
						$"Resize requested at frame {presentedFrames}: " +
						$"{resizeTarget.Width}x{resizeTarget.Height} " +
						$"(window {windowSize.Width}x{windowSize.Height}, " +
						$"framebuffer {framebufferSize.Width}x" +
						$"{framebufferSize.Height}).");

					if (!SDL.SetWindowSize(
							window.NativeHandle,
							resizeTarget.Width,
							resizeTarget.Height))
					{
						throw new InvalidOperationException(
							$"SDL rejected the resize request: " +
							$"{SDL.GetError()}");
					}
				}

				if (cameraScript is not null &&
					resizeObserved &&
					!postRecreationCheckpointLogged &&
					presentedFrames - resizeRecreationFrame >=
						PostRecreationCheckpointFrames)
				{
					postRecreationCheckpointLogged = true;

					Console.WriteLine(
						$"Camera final pose stable after recreation: " +
						$"{FormatCameraPose(camera)} after " +
						$"{presentedFrames - resizeRecreationFrame} " +
						$"accepted frames in generation " +
						$"{swapchainGeneration}.");
				}

				if (ecsScript is not null &&
					ecsScene is not null &&
					resizeObserved &&
					!postRecreationCheckpointLogged &&
					presentedFrames - resizeRecreationFrame >=
						PostRecreationCheckpointFrames)
				{
					postRecreationCheckpointLogged = true;

					Console.WriteLine(
						$"Ecs state stable after recreation: " +
						$"{FormatEcsState(ecsScene)} after " +
						$"{presentedFrames - resizeRecreationFrame} " +
						$"accepted frames in generation " +
						$"{swapchainGeneration}.");
				}

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

			if (options.ResizeAtFrame.HasValue &&
				options.FrameLimit.HasValue)
			{
				if (!resizeObserved)
				{
					throw new InvalidOperationException(
						$"The resize requested at frame " +
						$"{options.ResizeAtFrame.Value} was not " +
						$"applied: the framebuffer remained " +
						$"{lastFramebufferSize.Width}x" +
						$"{lastFramebufferSize.Height} across " +
						$"{swapchainGeneration} swapchain " +
						$"generation(s).");
				}

				Console.WriteLine(
					$"Frames presented after recreation: " +
					$"{presentedFrames - resizeRecreationFrame}.");
			}

			if (cameraScript is not null)
			{
				if (!cameraScript.HasReachedFinalPose)
				{
					throw new InvalidOperationException(
						$"The camera script did not reach its final pose " +
						$"before the run ended at {presentedFrames} " +
						$"accepted frames.");
				}

				Console.WriteLine(
					$"Camera final pose held for " +
					$"{cameraScript.FramesHoldingFinalPose} accepted frames.");
			}

			if (ecsScript is not null)
			{
				if (!ecsScript.HasReachedChangedState)
				{
					throw new InvalidOperationException(
						$"The ECS script did not reach its changed state " +
						$"before the run ended at {presentedFrames} " +
						$"accepted frames.");
				}

				Console.WriteLine(
					$"Ecs changed state held for " +
					$"{ecsScript.FramesHoldingChangedState} " +
					$"accepted frames.");
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
	/// Writes the checkpoint produced by one accepted scripted frame.
	/// </summary>
	/// <remarks>
	/// Checkpoints are only logged from the accepted-presentation
	/// path, so a line always describes a pose that has reached the
	/// swapchain. Numbers use invariant formatting for reproducible
	/// logs.
	/// </remarks>
	private static void LogCameraCheckpoint(
		CameraScriptCheckpoint checkpoint,
		CameraController camera,
		int presentedFrames)
	{
		switch (checkpoint)
		{
			case CameraScriptCheckpoint.InitialPose:
				Console.WriteLine(
					$"Camera initial pose: " +
					$"{FormatCameraPose(camera)} at accepted frame " +
					$"{presentedFrames}.");
				break;

			case CameraScriptCheckpoint.MovementStarted:
				Console.WriteLine(
					$"Camera script started: " +
					$"{FormatCameraPose(camera)} at accepted frame " +
					$"{presentedFrames}.");
				break;

			case CameraScriptCheckpoint.FinalPoseReached:
				Console.WriteLine(
					$"Camera final pose reached: " +
					$"{FormatCameraPose(camera)} at accepted frame " +
					$"{presentedFrames}; holding.");
				break;
		}
	}

	/// <summary>
	/// Formats a camera pose with invariant numeric formatting.
	/// </summary>
	private static string FormatCameraPose(CameraController camera)
	{
		return string.Create(
			CultureInfo.InvariantCulture,
			$"position=({camera.Position.X:F3}, " +
			$"{camera.Position.Y:F3}, {camera.Position.Z:F3}) " +
			$"yaw={camera.YawDegrees:F2}deg " +
			$"pitch={camera.PitchDegrees:F2}deg");
	}

	/// <summary>
	/// Writes the checkpoint produced by one accepted scripted ECS
	/// frame.
	/// </summary>
	/// <remarks>
	/// Checkpoints are only logged from the accepted-presentation
	/// path, so a line always describes state that has reached the
	/// swapchain. The mutation itself is applied here, after the
	/// frame was recorded and presented.
	/// </remarks>
	private static void LogEcsCheckpoint(
		EcsScriptCheckpoint checkpoint,
		EcsDemoScene scene,
		int presentedFrames)
	{
		switch (checkpoint)
		{
			case EcsScriptCheckpoint.InitialState:
				Console.WriteLine(
					$"Ecs initial state: {FormatEcsState(scene)} " +
					$"at accepted frame {presentedFrames}.");
				break;

			case EcsScriptCheckpoint.ChangeApplied:
				Console.WriteLine(
					$"Ecs change applied: {FormatEcsState(scene)} " +
					$"at accepted frame {presentedFrames}.");
				break;

			case EcsScriptCheckpoint.ChangedStateReached:
				Console.WriteLine(
					$"Ecs changed state reached: " +
					$"{FormatEcsState(scene)} at accepted frame " +
					$"{presentedFrames}; holding.");
				break;
		}
	}

	/// <summary>
	/// Formats the ECS scene's scripted component state with
	/// invariant numbers.
	/// </summary>
	private static string FormatEcsState(EcsDemoScene scene)
	{
		Vector3 camera = scene.CameraPosition;
		Vector3 triangle = scene.TriangleAPosition;

		return string.Create(
			CultureInfo.InvariantCulture,
			$"camera=({camera.X:F3}, {camera.Y:F3}, " +
			$"{camera.Z:F3}) triangleA=({triangle.X:F3}, " +
			$"{triangle.Y:F3}, {triangle.Z:F3})");
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
		int? resizeAtFrame = null;
		WindowSize? resizeTo = null;
		DemoScene? scene = null;
		bool depthTestDisabled = false;
		bool cameraScript = false;
		bool ecsScript = false;

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

				case ResizeAtFrameArgument:
					if (resizeAtFrame.HasValue)
					{
						options = default;
						error =
							"The --resize-at-frame argument was " +
							"supplied twice.";
						return false;
					}

					if (index + 1 >= args.Length)
					{
						options = default;
						error =
							"The --resize-at-frame argument requires " +
							"a value.";
						return false;
					}

					index++;

					if (!int.TryParse(
							args[index],
							NumberStyles.None,
							CultureInfo.InvariantCulture,
							out int parsedResizeFrame)
						|| parsedResizeFrame <= 0
						|| parsedResizeFrame > MaximumResizeFrame)
					{
						options = default;
						error =
							"The --resize-at-frame value must be a " +
							$"positive integer no greater than " +
							$"{MaximumResizeFrame}.";
						return false;
					}

					resizeAtFrame = parsedResizeFrame;
					break;

				case ResizeToArgument:
					if (resizeTo.HasValue)
					{
						options = default;
						error =
							"The --resize-to argument was supplied " +
							"twice.";
						return false;
					}

					if (index + 1 >= args.Length)
					{
						options = default;
						error =
							"The --resize-to argument requires a value.";
						return false;
					}

					index++;

					if (!TryParseResizeTarget(
							args[index],
							out WindowSize parsedResizeTarget,
							out error))
					{
						options = default;
						return false;
					}

					resizeTo = parsedResizeTarget;
					break;

				case SceneArgument:
					if (scene.HasValue)
					{
						options = default;
						error =
							"The --scene argument was supplied twice.";
						return false;
					}

					if (index + 1 >= args.Length)
					{
						options = default;
						error = "The --scene argument requires a value.";
						return false;
					}

					index++;

					if (!TryParseScene(args[index], out DemoScene parsedScene))
					{
						options = default;
						error =
							"The --scene value must be " +
							$"{GallerySceneName}, " +
							$"{DepthOcclusionSceneName}, " +
							$"{CameraTransformsSceneName}, or " +
							$"{EcsSceneName}.";
						return false;
					}

					scene = parsedScene;
					break;

				case EcsScriptArgument:
					if (ecsScript)
					{
						options = default;
						error =
							"The --ecs-script argument was supplied twice.";
						return false;
					}

					ecsScript = true;
					break;

				case CameraScriptArgument:
					if (cameraScript)
					{
						options = default;
						error =
							"The --camera-script argument was " +
							"supplied twice.";
						return false;
					}

					cameraScript = true;
					break;

				case DisableDepthTestArgument:
					if (depthTestDisabled)
					{
						options = default;
						error =
							"The --disable-depth-test argument was " +
							"supplied twice.";
						return false;
					}

					depthTestDisabled = true;
					break;

				default:
					options = default;
					error = $"Unknown argument: {argument}";
					return false;
			}
		}

		if (resizeAtFrame.HasValue != resizeTo.HasValue)
		{
			options = default;
			error =
				"The --resize-at-frame and --resize-to arguments " +
				"must be supplied together.";
			return false;
		}

		if (resizeAtFrame is int triggerFrame &&
			frameLimit is int limit &&
			triggerFrame >= limit)
		{
			options = default;
			error =
				"The --resize-at-frame value must be less than " +
				"the --frames limit.";
			return false;
		}

		DemoScene selectedScene = scene ?? DemoScene.Gallery;

		if (depthTestDisabled && selectedScene != DemoScene.DepthOcclusion)
		{
			options = default;
			error =
				"The --disable-depth-test argument requires " +
				$"--scene {DepthOcclusionSceneName}.";
			return false;
		}

		if (cameraScript && selectedScene != DemoScene.CameraTransforms)
		{
			options = default;
			error =
				"The --camera-script argument requires " +
				$"--scene {CameraTransformsSceneName}.";
			return false;
		}

		if (cameraScript && !frameLimit.HasValue)
		{
			options = default;
			error =
				"The --camera-script argument requires a bounded " +
				"--frames run.";
			return false;
		}

		if (cameraScript &&
			frameLimit is int scriptedFrames &&
			scriptedFrames < CameraScript.MinimumFrameLimit)
		{
			options = default;
			error =
				"The --camera-script run requires --frames of at " +
				$"least {CameraScript.MinimumFrameLimit}.";
			return false;
		}

		if (ecsScript && selectedScene != DemoScene.EcsScene)
		{
			options = default;
			error =
				"The --ecs-script argument requires " +
				$"--scene {EcsSceneName}.";
			return false;
		}

		if (ecsScript && !frameLimit.HasValue)
		{
			options = default;
			error =
				"The --ecs-script argument requires a bounded " +
				"--frames run.";
			return false;
		}

		if (ecsScript &&
			frameLimit is int ecsFrames &&
			ecsFrames < EcsScript.MinimumFrameLimit)
		{
			options = default;
			error =
				"The --ecs-script run requires --frames of at " +
				$"least {EcsScript.MinimumFrameLimit}.";
			return false;
		}

		options = new SampleOptions(
			validation,
			frameLimit,
			resizeAtFrame,
			resizeTo,
			selectedScene,
			depthTestDisabled,
			cameraScript,
			ecsScript);

		error = string.Empty;
		return true;
	}

	/// <summary>
	/// Parses a scene name.
	/// </summary>
	internal static bool TryParseScene(string value, out DemoScene scene)
	{
		switch (value)
		{
			case GallerySceneName:
				scene = DemoScene.Gallery;
				return true;

			case DepthOcclusionSceneName:
				scene = DemoScene.DepthOcclusion;
				return true;

			case CameraTransformsSceneName:
				scene = DemoScene.CameraTransforms;
				return true;

			case EcsSceneName:
				scene = DemoScene.EcsScene;
				return true;

			default:
				scene = default;
				return false;
		}
	}

	private static string SceneName(DemoScene scene)
	{
		return scene switch
		{
			DemoScene.Gallery => GallerySceneName,
			DemoScene.DepthOcclusion => DepthOcclusionSceneName,
			DemoScene.CameraTransforms => CameraTransformsSceneName,
			DemoScene.EcsScene => EcsSceneName,
			_ => "unknown"
		};
	}

	/// <summary>
	/// Reports whether the opt-in resize scenario is due after the
	/// given number of accepted frames.
	/// </summary>
	/// <remarks>
	/// The caller passes the current requested state so the scenario
	/// triggers at most once, even when frames are presented faster
	/// than the request is observed.
	/// </remarks>
	internal static bool ShouldRequestResize(
		int presentedFrames,
		SampleOptions options,
		bool resizeRequested)
	{
		return !resizeRequested &&
			options.ResizeAtFrame is int resizeFrame &&
			presentedFrames >= resizeFrame;
	}

	/// <summary>
	/// Reports whether a requested resize has now been applied.
	/// </summary>
	/// <remarks>
	/// The framebuffer extent must actually differ from the previous
	/// generation's extent. A denied request, a same-size recreation,
	/// or an internal state change alone never satisfies this test.
	/// </remarks>
	internal static bool IsResizeObserved(
		bool resizeRequested,
		bool alreadyObserved,
		WindowSize previousExtent,
		WindowSize currentExtent)
	{
		return resizeRequested &&
			!alreadyObserved &&
			currentExtent != previousExtent;
	}

	/// <summary>
	/// Parses a WIDTHxHEIGHT resize target.
	/// </summary>
	internal static bool TryParseResizeTarget(
		string value,
		out WindowSize size,
		out string error)
	{
		size = default;

		int separator = value.IndexOf('x');

		if (separator <= 0 || separator == value.Length - 1)
		{
			error =
				"The --resize-to value must use the WIDTHxHEIGHT " +
				"format, for example 960x540.";
			return false;
		}

		string widthText = value[..separator];
		string heightText = value[(separator + 1)..];

		if (!int.TryParse(
				widthText,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out int width) ||
			!int.TryParse(
				heightText,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out int height) ||
			width <= 0 ||
			height <= 0 ||
			width > MaximumResizeDimension ||
			height > MaximumResizeDimension)
		{
			error =
				"The --resize-to dimensions must be positive " +
				$"integers no greater than " +
				$"{MaximumResizeDimension}.";
			return false;
		}

		size = new WindowSize(width, height);
		error = string.Empty;
		return true;
	}
}

internal readonly record struct SampleOptions(
	bool Validation,
	int? FrameLimit,
	int? ResizeAtFrame,
	WindowSize? ResizeTo,
	DemoScene Scene,
	bool DepthTestDisabled,
	bool CameraScript = false,
	bool EcsScript = false);
