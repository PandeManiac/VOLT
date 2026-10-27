using System.Diagnostics;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Surface;
using Volt.Graphics.Vulkan.Swapchain;
using Volt.Graphics.Vulkan.Synchronization;
using Volt.Platform.Events;
using Volt.Platform.Input;
using Volt.Platform.Windows;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Vulkan;

/// <summary>
/// Hosts a Volt.Ui application in an SDL3 window presented by Vulkan.
/// </summary>
/// <remarks>
/// <para>
/// The host owns the window, presentation surface, logical device,
/// swapchain generation, UI context, geometry, and input translation.
/// An application supplies a per-frame callback and nothing else, so
/// the same code path runs the editor and a standalone game.
/// </para>
/// <para>
/// All native work happens on the calling thread. Run must be called on
/// the thread that created SDL's video subsystem and owns the window.
/// Disposal releases every GPU object after the device is idle.
/// </para>
/// <para>
/// Only the window-system extensions and VK_KHR_swapchain are required.
/// Presentation fences from the swapchain maintenance feature are used
/// when the platform exposes them, and swapchain generations otherwise
/// retire through an explicit device idle, so a GPU without that
/// extension still runs the UI.
/// </para>
/// </remarks>
public sealed class VulkanUiHost : IDisposable
{
	private const string SwapchainExtension = "VK_KHR_swapchain";
	private const int MaximumFramesInFlight = 2;
	private const int PixelSlots = 2;

	private static readonly KeyCode[] s_pointerKeys =
	[
		KeyCode.LeftShift,
		KeyCode.RightShift,
		KeyCode.LeftControl,
		KeyCode.RightControl,
		KeyCode.LeftAlt,
		KeyCode.RightAlt
	];

	private readonly VulkanUiHostOptions _options;
	private readonly Window _window;
	private readonly UiContext _ui;
	private readonly UiGeometry _geometry = new();
	private readonly Stopwatch _clock = Stopwatch.StartNew();
	private readonly List<string> _pendingText = [];

	private VulkanInstance _instance;
	private VulkanSurface _surface;
	private PhysicalDevice _physicalDevice;
	private VulkanLogicalDevice _device;
	private VulkanCommandPool _commandPool;
	private VulkanCommandBuffers _commandBuffers;
	private SwapchainResources? _resources;

	private UiPoint _pointer;
	private UiKeyModifiers _modifiers;
	private double _lastFrameSeconds;
	private int _frameSlot;
	private float _dpiScale = 1f;
	private bool _exitRequested;
	private bool _disposed;

	/// <summary>
	/// Opens a window and initializes Vulkan presentation.
	/// </summary>
	public VulkanUiHost(VulkanUiHostOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		_options = options;

		_window = new Window(
			new WindowDescription(
				Title: options.Title,
				Size: options.Size,
				VulkanCompatible: true,
				Resizable: true,
				HighPixelDensity: true));

		VulkanInstance? instance = null;
		VulkanSurface? surface = null;
		VulkanLogicalDevice? device = null;
		VulkanCommandPool? commandPool = null;
		VulkanCommandBuffers? commandBuffers = null;
		UiContext? ui = null;

		try
		{
			List<string> extensions =
			[
				.. VulkanWindowSupport.GetRequiredInstanceExtensions(
					_window)
			];

			// The maintenance feature is only visible to the device
			// query once the instance enables its prerequisites, so
			// add whichever of them this loader supports.
			foreach (string instanceExtension in
				VulkanSwapchainMaintenance1Extensions
					.GetSupportedInstanceExtensions())
			{
				if (!extensions.Contains(instanceExtension))
				{
					extensions.Add(instanceExtension);
				}
			}

			instance = new VulkanInstance(
				new VulkanInstanceDescription(
					ApplicationName: options.Title,
					EnableValidation: options.EnableValidation),
				extensions);

			surface = new VulkanSurface(instance, _window);

			PhysicalDevice physicalDevice =
				SelectPhysicalDevice(instance, surface);

			_physicalDevice = physicalDevice;

			// Presentation fences make retirement cheaper, but the
			// host is correct without them: every generation is
			// retired only after an explicit device idle.
			string? maintenanceExtension =
				VulkanLogicalDevice
					.GetSupportedSwapchainMaintenance1Extension(
						instance,
						physicalDevice);

			string[] deviceExtensions = maintenanceExtension is null
				? [SwapchainExtension]
				: [SwapchainExtension, maintenanceExtension];

			device = new VulkanLogicalDevice(
				instance,
				physicalDevice,
				_selectedQueueFamily,
				deviceExtensions,
				enableSwapchainMaintenance1:
					maintenanceExtension is not null);

			PresentationFencesEnabled =
				device.SwapchainMaintenance1Enabled;

			commandPool = new VulkanCommandPool(instance, device);

			commandBuffers = new VulkanCommandBuffers(
				instance,
				device,
				commandPool,
				MaximumFramesInFlight);

			_ui = ui = new UiContext(options.Theme);

			_window.StartTextInput();
		}

		catch
		{
			ui?.Dispose();
			commandBuffers?.Dispose();
			commandPool?.Dispose();
			device?.Dispose();
			surface?.Dispose();
			instance?.Dispose();
			_window.Dispose();
			throw;
		}

		_instance = instance;
		_surface = surface;
		_device = device;
		_commandPool = commandPool;
		_commandBuffers = commandBuffers;
	}

	private uint _selectedQueueFamily;

	/// <summary>
	/// Gets the UI context owned by this host.
	/// </summary>
	public UiContext Ui => _ui;

	/// <summary>
	/// Gets the native window.
	/// </summary>
	public Window Window => _window;

	/// <summary>
	/// Gets the current physical pixels per logical unit.
	/// </summary>
	public float DpiScale => _dpiScale;

	/// <summary>
	/// Gets whether presentation fences from the swapchain
	/// maintenance feature are in use for new swapchain generations.
	/// </summary>
	/// <remarks>
	/// When this is false, the platform does not expose the feature and
	/// each generation is retired through an explicit device idle
	/// instead.
	/// </remarks>
	public bool PresentationFencesEnabled { get; private set; }

	/// <summary>
	/// Gets the current logical surface size.
	/// </summary>
	public UiSize LogicalSize { get; private set; } = new(1f, 1f);

	/// <summary>
	/// Gets the number of frames presented so far.
	/// </summary>
	public int PresentedFrames { get; private set; }

	/// <summary>
	/// Gets the name of the GPU this host selected.
	/// </summary>
	public string DeviceName { get; private set; } = "unknown";

	/// <summary>
	/// Gets whether the Vulkan validation layer is enabled.
	/// </summary>
	/// <remarks>
	/// This is true only when the layer is actually loaded: requesting
	/// validation without an available layer fails the constructor
	/// rather than running unvalidated.
	/// </remarks>
	public bool ValidationLayersEnabled => _instance.ValidationEnabled;

	/// <summary>
	/// Gets the wall-clock interval of the frame being updated.
	/// </summary>
	/// <remarks>
	/// This is real elapsed time, not a fixed simulation tick. Runtime
	/// play mode advances its own fixed-step clock from this value.
	/// </remarks>
	public double DeltaSeconds { get; private set; }

	/// <summary>
	/// Asks the loop to stop after the current frame.
	/// </summary>
	public void RequestExit()
	{
		_exitRequested = true;
	}

	/// <summary>
	/// Runs the presentation loop until the window closes or the
	/// application requests an exit.
	/// </summary>
	/// <param name="update">
	/// Called once per frame before layout and painting. It must only
	/// change UI or application state; it must not touch Vulkan.
	/// </param>
	public void Run(Action<VulkanUiHost> update)
	{
		ArgumentNullException.ThrowIfNull(update);
		ThrowIfDisposed();

		while (!_exitRequested && !_window.ShouldClose)
		{
			if (_options.FrameInterval is TimeSpan interval)
			{
				Thread.Sleep(interval);
			}

			RunFrame(update);
		}
	}

	/// <summary>
	/// Processes one poll, one update, one layout, and one presented
	/// frame, then returns.
	/// </summary>
	/// <remarks>
	/// Smoke tests use this to drive a fixed number of frames without a
	/// display, and interactive loops call it from <see cref="Run"/>.
	/// </remarks>
	public void RunFrame(Action<VulkanUiHost> update)
	{
		ArgumentNullException.ThrowIfNull(update);
		ThrowIfDisposed();

		_window.PollEvents();
		DrainWindowEvents();

		if (_window.ShouldClose)
		{
			return;
		}

		WindowSize pixels = _window.PixelSize;

		if (pixels.Width <= 0 || pixels.Height <= 0)
		{
			Thread.Sleep(1);
			return;
		}

		WindowSize logical = _window.Size;

		UiSize surface = new(
			MathF.Max(1f, logical.Width),
			MathF.Max(1f, logical.Height));

		float scale = MathF.Max(
			0.01f,
			pixels.Width / surface.Width);

		if (MathF.Abs(scale - _dpiScale) > 0.001f)
		{
			_dpiScale = scale;
			_ui.SetDpiScale(scale);
		}

		LogicalSize = surface;

		InputSnapshot input = _window.Input.Snapshot;

		double now = _clock.Elapsed.TotalSeconds;

		DeltaSeconds = Math.Clamp(now - _lastFrameSeconds, 0d, 0.1d);
		_lastFrameSeconds = now;

		DispatchInput(input, surface);
		update(this);

		_ui.BeginFrame(
			now,
			LogicalSize,
			_pointer,
			new UiPoint(
				input.MouseMotion.X,
				input.MouseMotion.Y));

		UiDrawList drawList = _ui.Draw();

		_ui.EndFrame();

		_geometry.Build(drawList, _dpiScale);

		Present(pixels);
	}

	/// <summary>
	/// Releases every GPU resource and the native window.
	/// </summary>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;

		try
		{
			_device.WaitIdle();
		}

		catch
		{
			// Device loss during shutdown must not prevent the
			// remaining native resources from being released.
		}

		_resources?.Dispose();
		_resources = null;

		_ui.Dispose();
		_commandBuffers.Dispose();
		_commandPool.Dispose();
		_device.Dispose();
		_surface.Dispose();
		_instance.Dispose();
		_window.Dispose();
	}

	private void Present(WindowSize pixels)
	{
		if (_resources is null ||
			_resources.Pixels.Width != pixels.Width ||
			_resources.Pixels.Height != pixels.Height)
		{
			Recreate(pixels);
		}

		SwapchainResources active = _resources
			?? throw new InvalidOperationException(
				"The swapchain was not created.");

		int slot = _frameSlot % PixelSlots;

		VulkanSwapchainFrameResult frame = active.Frames.Draw(
			(commandBuffer, imageIndex) =>
				active.Renderer.Render(
					commandBuffer,
					imageIndex,
					slot,
					_geometry.Vertices,
					_geometry.Indices,
					_geometry.Batches));

		if (frame.Submitted)
		{
			_frameSlot++;
			PresentedFrames++;
		}

		if (frame.RequiresRecreation)
		{
			WindowSize current = _window.PixelSize;

			if (current.Width > 0 && current.Height > 0)
			{
				Recreate(current);
			}
		}
	}

	private void Recreate(WindowSize pixels)
	{
		_device.WaitIdle();

		_resources?.Dispose();
		_resources = null;

		if (pixels.Width <= 0 || pixels.Height <= 0)
		{
			return;
		}

		_resources = new SwapchainResources(
			_instance,
			_physicalDevice,
			_device,
			_surface,
			_commandBuffers,
			pixels,
			_ui.Theme.Fonts.Enumerate().ToArray(),
			_ui.Textures,
			_options.VertexShaderPath ?? DefaultShader("ui.vert.spv"),
			_options.FragmentShaderPath ?? DefaultShader("ui.frag.spv"));
	}

	private void DispatchInput(InputSnapshot input, UiSize surface)
	{
		_modifiers = UiKeyModifiers.None;

		if (input.IsKeyDown(KeyCode.LeftShift) ||
			input.IsKeyDown(KeyCode.RightShift))
		{
			_modifiers |= UiKeyModifiers.Shift;
		}

		if (input.IsKeyDown(KeyCode.LeftControl) ||
			input.IsKeyDown(KeyCode.RightControl))
		{
			_modifiers |= UiKeyModifiers.Control;
		}

		if (input.IsKeyDown(KeyCode.LeftAlt) ||
			input.IsKeyDown(KeyCode.RightAlt))
		{
			_modifiers |= UiKeyModifiers.Alt;
		}

		if (input.FocusLostDuringFrame)
		{
			_ui.WindowFocusLost();
		}

		if (input.MouseFocus != MouseFocusState.None)
		{
			UiPoint position = new(
				Math.Clamp(input.MousePosition.X, 0f, surface.Width),
				Math.Clamp(input.MousePosition.Y, 0f, surface.Height));

			UiPoint delta = new(input.MouseMotion.X, input.MouseMotion.Y);

			_pointer = position;
			_ui.PointerMove(position, delta, _modifiers);
		}

		if (input.WheelDelta != default)
		{
			_ui.Wheel(
				new UiPoint(input.WheelDelta.X, input.WheelDelta.Y),
				_pointer,
				_modifiers);
		}

		DispatchButton(input, MouseButton.Left, UiMouseButton.Left);
		DispatchButton(input, MouseButton.Middle, UiMouseButton.Middle);
		DispatchButton(input, MouseButton.Right, UiMouseButton.Right);

		foreach (KeyCode key in s_pointerKeys)
		{
			if (input.WasKeyPressed(key) && TryMapKey(key, out UiKey mapped))
			{
				_ui.KeyDown(mapped, _modifiers);
			}
			else if (input.WasKeyReleased(key) &&
				TryMapKey(key, out mapped))
			{
				_ui.KeyUp(mapped, _modifiers);
			}
		}

		for (int value = 1; value <= 80; value++)
		{
			KeyCode key = (KeyCode)value;
			UiKeyModifiers modifiersBefore = _modifiers;

			if (!TryMapKey(key, out UiKey mapped))
			{
				continue;
			}

			if (key is KeyCode.LeftShift or KeyCode.RightShift or
				KeyCode.LeftControl or KeyCode.RightControl or
				KeyCode.LeftAlt or KeyCode.RightAlt)
			{
				continue;
			}

			if (input.WasKeyPressed(key))
			{
				_ui.KeyDown(mapped, modifiersBefore);
			}
			else if (input.WasKeyReleased(key))
			{
				_ui.KeyUp(mapped, modifiersBefore);
			}
		}

		foreach (string text in _pendingText)
		{
			_ui.TextInput(text);
		}

		_pendingText.Clear();
	}

	private void DispatchButton(
		InputSnapshot input,
		MouseButton platformButton,
		UiMouseButton button)
	{
		if (input.WasMouseButtonPressed(platformButton))
		{
			_ui.PointerDown(button, _pointer, _modifiers);
		}
		else if (input.WasMouseButtonReleased(platformButton))
		{
			_ui.PointerUp(button, _pointer, _modifiers);
		}
	}

	private void DrainWindowEvents()
	{
		while (_window.TryDequeueEvent(out WindowEvent windowEvent))
		{
			if (windowEvent.Type == WindowEventType.TextInput &&
				windowEvent.Text is string text)
			{
				_pendingText.Add(text);
			}
		}
	}

	private static bool TryMapKey(KeyCode key, out UiKey mapped)
	{
		if (key is >= KeyCode.A and <= KeyCode.Z)
		{
			mapped = UiKey.A + ((int)key - (int)KeyCode.A);
			return true;
		}

		if (key is >= KeyCode.Number0 and <= KeyCode.Number9)
		{
			mapped = UiKey.D0 + ((int)key - (int)KeyCode.Number0);
			return true;
		}

		if (key is >= KeyCode.F1 and <= KeyCode.F12)
		{
			mapped = UiKey.F1 + ((int)key - (int)KeyCode.F1);
			return true;
		}

		mapped = key switch
		{
			KeyCode.Up => UiKey.Up,
			KeyCode.Down => UiKey.Down,
			KeyCode.Left => UiKey.Left,
			KeyCode.Right => UiKey.Right,
			KeyCode.Escape => UiKey.Escape,
			KeyCode.Space => UiKey.Space,
			KeyCode.Enter => UiKey.Enter,
			KeyCode.Tab => UiKey.Tab,
			KeyCode.Backspace => UiKey.Backspace,
			KeyCode.Delete => UiKey.Delete,
			KeyCode.Insert => UiKey.Insert,
			KeyCode.Home => UiKey.Home,
			KeyCode.End => UiKey.End,
			KeyCode.PageUp => UiKey.PageUp,
			KeyCode.PageDown => UiKey.PageDown,
			KeyCode.Minus => UiKey.Minus,
			KeyCode.Equals => UiKey.Equal,
			KeyCode.LeftBracket => UiKey.LeftBracket,
			KeyCode.RightBracket => UiKey.RightBracket,
			KeyCode.Backslash => UiKey.Backslash,
			KeyCode.Semicolon => UiKey.Semicolon,
			KeyCode.Apostrophe => UiKey.Apostrophe,
			KeyCode.Grave => UiKey.Grave,
			KeyCode.Comma => UiKey.Comma,
			KeyCode.Period => UiKey.Period,
			KeyCode.Slash => UiKey.Slash,
			_ => UiKey.None
		};

		return mapped != UiKey.None;
	}

	private PhysicalDevice SelectPhysicalDevice(
		VulkanInstance instance,
		VulkanSurface surface)
	{
		PhysicalDevice[] devices = instance.EnumeratePhysicalDevices();

		VulkanPhysicalDeviceInfo[] candidates =
			new VulkanPhysicalDeviceInfo[devices.Length];

		for (int index = 0; index < devices.Length; index++)
		{
			VulkanPhysicalDeviceInfo information =
				VulkanPhysicalDeviceInspector.Inspect(
					instance,
					devices[index]);

			uint? queueFamily =
				VulkanPresentationSupport
					.FindGraphicsPresentationQueueFamily(
						instance,
						devices[index],
						surface);

			bool supported = false;

			if (queueFamily.HasValue)
			{
				supported = VulkanSwapchainSupport
					.Query(instance, devices[index], surface)
					.CanCreateColorSwapchain;
			}

			candidates[index] = information with
			{
				GraphicsQueueFamilyIndex = supported
					? queueFamily
					: null
			};
		}

		int selected = VulkanPhysicalDeviceSelector.SelectIndex(
			candidates,
			requiredApiMajor: 1,
			requiredApiMinor: 0)
			?? throw new NotSupportedException(
				"No graphics and presentation capable GPU was found.");

		_selectedQueueFamily =
			candidates[selected].GraphicsQueueFamilyIndex
			?? throw new InvalidOperationException(
				"The selected GPU has no presentation queue.");

		DeviceName = candidates[selected].Name;

		return devices[selected];
	}

	private static string DefaultShader(string fileName)
	{
		return Path.Combine(
			AppContext.BaseDirectory,
			"Shaders",
			fileName);
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	/// <summary>
	/// Owns the presentation resources for one swapchain generation.
	/// </summary>
	private sealed class SwapchainResources : IDisposable
	{
		private readonly VulkanSwapchain _swapchain;
		private readonly VulkanSwapchainImageViews _imageViews;

		public SwapchainResources(
			VulkanInstance instance,
			PhysicalDevice physicalDevice,
			VulkanLogicalDevice device,
			VulkanSurface surface,
			VulkanCommandBuffers commandBuffers,
			WindowSize pixels,
			IReadOnlyList<UiFont> fonts,
			UiTextureRegistry textures,
			string vertexShaderPath,
			string fragmentShaderPath)
		{
			Pixels = pixels;

			VulkanSwapchain? swapchain = null;
			VulkanSwapchainImageViews? imageViews = null;
			VulkanUiRenderer? renderer = null;
			VulkanSwapchainFrameScheduler? frames = null;

			try
			{
				swapchain = new VulkanSwapchain(
					instance,
					physicalDevice,
					device,
					surface,
					checked((uint)pixels.Width),
					checked((uint)pixels.Height));

				imageViews = new VulkanSwapchainImageViews(
					instance,
					device,
					swapchain);

				renderer = new VulkanUiRenderer(
					instance,
					device,
					physicalDevice,
					swapchain,
					imageViews,
					fonts,
					textures,
					vertexShaderPath,
					fragmentShaderPath);

				frames = new VulkanSwapchainFrameScheduler(
					instance,
					device,
					swapchain,
					commandBuffers,
					MaximumFramesInFlight);
			}

			catch
			{
				frames?.Dispose();
				renderer?.Dispose();
				imageViews?.Dispose();
				swapchain?.Dispose();
				throw;
			}

			_swapchain = swapchain;
			_imageViews = imageViews;
			Renderer = renderer;
			Frames = frames;
		}

		public WindowSize Pixels { get; }

		public VulkanUiRenderer Renderer { get; }

		public VulkanSwapchainFrameScheduler Frames { get; }

		public void Dispose()
		{
			Frames.Dispose();
			Renderer.Dispose();
			_imageViews.Dispose();

			// The swapchain owns the images the image views were
			// created from, so it is destroyed after them.
			_swapchain.Dispose();
		}
	}
}
