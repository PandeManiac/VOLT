using System.Diagnostics;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Commands;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;
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
	private readonly List<ImageRegistration> _images = [];
	private readonly List<string> _diagnostics = [];

	private int _nextImageId = 1;

	private VulkanInstance _instance;
	private VulkanSurface _surface;
	private PhysicalDevice _physicalDevice;
	private VulkanLogicalDevice _device;
	private VulkanCommandPool _commandPool;
	private VulkanCommandBuffers _commandBuffers;
	private SwapchainResources? _resources;
	private UiFontSet? _activeFontSet;

	private UiPoint _pointer;
	private UiKeyModifiers _modifiers;
	private double _lastFrameSeconds;
	private int _frameSlot;
	private float _dpiScale = 1f;
	private bool _exitRequested;
	private WindowSize? _pendingWindowSize;
	private WindowPosition? _pendingWindowPosition;
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
				HighPixelDensity: true)
			{
				Position = options.Position
			});

		// SDL knows both sizes as soon as the window exists, so startup
		// diagnostics do not have to wait for the first UI frame.
		SurfaceMetrics = UiSurfaceMetrics.FromWindowSizes(
			_window.Size,
			_window.PixelSize);

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
	/// Gets the Vulkan instance the host created.
	/// </summary>
	/// <remarks>
	/// The host owns it; an application only borrows it to create
	/// resources that must outlive a swapchain generation. It must not
	/// be disposed by the application.
	/// </remarks>
	public VulkanInstance Instance => _instance;

	/// <summary>
	/// Gets the logical device the host created.
	/// </summary>
	/// <remarks>
	/// The host owns it. The application may create and record its own
	/// resources with it, but anything submitted must be complete before
	/// the host is disposed.
	/// </remarks>
	public VulkanLogicalDevice Device => _device;

	/// <summary>
	/// Gets the physical device the host selected.
	/// </summary>
	/// <remarks>
	/// Named for the hardware rather than the handle type so it cannot
	/// be confused with <see cref="Device"/>, which is the logical
	/// device.
	/// </remarks>
	public PhysicalDevice Gpu => _physicalDevice;

	/// <summary>
	/// Gets the frame slot the current frame records into.
	/// </summary>
	/// <remarks>
	/// Frames in flight alternate between slots, so an application that
	/// renders into its own offscreen image should keep one image per
	/// slot: the image a frame samples is then written by the same
	/// submission that samples it, and the other slot cannot still be
	/// reading it. The value only advances when a frame is submitted.
	/// </remarks>
	public int CurrentFrameSlot { get; private set; }

	/// <summary>
	/// Gets or sets a callback that records application passes into the
	/// frame before the UI pass samples them.
	/// </summary>
	/// <remarks>
	/// The callback receives a command buffer that is recording and is
	/// outside any render pass. Anything it records is ordered before
	/// the UI pass, which is where a viewport render target is filled.
	/// Registered images are transitioned to their sampled layout by the
	/// UI renderer, so the callback only has to leave them in a layout
	/// the UI renderer can name. An exception thrown here aborts the
	/// frame loop exactly as an exception from the update callback does.
	/// </remarks>
	public Action<CommandBuffer>? BeforeUiPass { get; set; }

	/// <summary>
	/// Registers a render target as a UI image texture.
	/// </summary>
	/// <param name="image">The image to sample.</param>
	/// <returns>
	/// The identifier draw commands use. It stays valid for the life of
	/// the host, including across swapchain recreation, because the host
	/// re-registers every image with each new renderer.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="image"/> is null.
	/// </exception>
	public UiTextureId RegisterImage(IVulkanSampledImage image)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);

		UiTextureId id = UiTextureId.FromIndex(_nextImageId++);

		_images.Add(new ImageRegistration(id, image));

		_resources?.Renderer.RegisterImage(image, id);

		return id;
	}

	/// <summary>
	/// Re-points a registered image identifier at a different image.
	/// </summary>
	/// <param name="id">The identifier returned by registration.</param>
	/// <param name="image">The image the identifier should sample.</param>
	/// <returns>
	/// True when the identifier was registered, false otherwise.
	/// </returns>
	/// <remarks>
	/// This exists for render targets that are recreated at a new size,
	/// which changes their image view. It waits for the device, because
	/// a descriptor set must not be rewritten while a submitted frame
	/// could still be reading it. A resize is a rare event, so paying one
	/// idle to keep the composition correct is the right trade.
	/// </remarks>
	public bool UpdateImage(UiTextureId id, IVulkanSampledImage image)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(image);

		int index = _images.FindIndex(entry => entry.Id == id);

		if (index < 0)
		{
			return false;
		}

		_device.WaitIdle();

		_images[index] = new ImageRegistration(id, image);

		_resources?.Renderer.UpdateImage(id, image);

		return true;
	}

	/// <summary>
	/// Releases a registered image identifier.
	/// </summary>
	/// <param name="id">The identifier returned by registration.</param>
	/// <returns>
	/// True when the identifier was registered, false otherwise.
	/// </returns>
	/// <remarks>
	/// The identifier is never reused, so geometry that still names it
	/// falls back to the white texel rather than sampling a different
	/// image.
	/// </remarks>
	public bool UnregisterImage(UiTextureId id)
	{
		ThrowIfDisposed();

		int index = _images.FindIndex(entry => entry.Id == id);

		if (index < 0)
		{
			return false;
		}

		_resources?.Renderer.UnregisterImage(id);
		_images.RemoveAt(index);

		return true;
	}

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
	/// Gets the window geometry from the most recent frame, or from
	/// window creation before the first frame.
	/// </summary>
	public UiSurfaceMetrics SurfaceMetrics { get; private set; } =
		new(1, 1, 1, 1, 1f);

	/// <summary>
	/// Gets the problems reported while native resources were released,
	/// oldest first.
	/// </summary>
	public IReadOnlyList<string> Diagnostics => _diagnostics;

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
	/// Queues a logical window resize for the next frame.
	/// </summary>
	/// <param name="size">The new client-area size.</param>
	/// <remarks>
	/// The host applies the resize before it polls the next frame, so
	/// the surface, swapchain, and layout all see the new size in the
	/// same frame. Resizing immediately from an input handler would
	/// invalidate sizes the current frame is still using.
	/// </remarks>
	public void RequestWindowSize(WindowSize size)
	{
		ThrowIfDisposed();

		if (!size.IsPositive)
		{
			throw new ArgumentOutOfRangeException(
				nameof(size),
				size,
				"Window dimensions must be positive.");
		}

		_pendingWindowSize = size;
	}

	/// <summary>
	/// Queues a window position for the next frame.
	/// </summary>
	/// <param name="position">The desired logical desktop position.</param>
	/// <remarks>
	/// The position is applied with the queued size before the next
	/// poll. A position that no longer lands on a display is ignored by
	/// <see cref="Window.TryRestorePosition"/>.
	/// </remarks>
	public void RequestWindowPosition(WindowPosition position)
	{
		ThrowIfDisposed();
		_pendingWindowPosition = position;
	}

	/// <summary>
	/// Gets or sets a callback asked before a native window close is
	/// accepted.
	/// </summary>
	/// <remarks>
	/// Returning false keeps the window open and cancels that close
	/// request, which lets an application ask about unsaved work. The
	/// application is then responsible for calling
	/// <see cref="RequestExit"/> once the user answers.
	/// </remarks>
	public Func<bool>? ConfirmClose { get; set; }

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

		while (!_exitRequested)
		{
			if (_window.ShouldClose)
			{
				if (ConfirmClose is null || ConfirmClose())
				{
					break;
				}

				_window.CancelCloseRequest();
			}

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

		if (_pendingWindowSize is WindowSize pending)
		{
			_pendingWindowSize = null;
			_window.SetSize(pending);
		}

		if (_pendingWindowPosition is WindowPosition pendingPosition)
		{
			_pendingWindowPosition = null;
			_ = _window.TryRestorePosition(pendingPosition);
		}

		_window.PollEvents();
		DrainWindowEvents();

		if (_window.ShouldClose)
		{
			return;
		}

		UiSurfaceMetrics metrics = UiSurfaceMetrics.FromWindowSizes(
			_window.Size,
			_window.PixelSize);

		if (!metrics.HasPhysicalSurface)
		{
			// A minimized window has no drawable surface. Its logical
			// size and scale stay as they were.
			Thread.Sleep(1);
			return;
		}

		SurfaceMetrics = metrics;

		UiSize surface = metrics.LogicalSize;
		float scale = metrics.DisplayScale;

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

		// Relative motion is pushed once per completed frame and pulled
		// by whichever viewport is navigating, so a captured look uses
		// real native movement instead of differences between absolute
		// positions that the hidden cursor never updates.
		_ui.PointerCapture?.AccumulateMotion(
			new UiPoint(input.RelativeMotion.X, input.RelativeMotion.Y));

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

		Present(new WindowSize(
			metrics.PhysicalWidth,
			metrics.PhysicalHeight));
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

		// Every release is attempted even if an earlier one fails, so a
		// shutdown problem cannot leave the window, device, or instance
		// behind. The failure is reported instead of thrown: throwing
		// here would replace the frame failure that caused the shutdown.
		SwapchainResources? resources = _resources;

		_resources = null;

		Release("the swapchain generation", () => resources?.Dispose());

		if (resources is not null)
		{
			ReportRetirement(resources);
		}

		Release("the UI context", _ui.Dispose);
		Release("the command buffers", _commandBuffers.Dispose);
		Release("the command pool", _commandPool.Dispose);
		Release("the logical device", _device.Dispose);
		Release("the presentation surface", _surface.Dispose);
		Release("the Vulkan instance", _instance.Dispose);
		Release("the native window", _window.Dispose);
	}

	private void Release(string operation, Action release)
	{
		try
		{
			release();
		}

		catch (Exception exception)
		{
			ReportDiagnostic(
				$"Releasing {operation} failed during shutdown: {exception}");
		}
	}

	private void ReportRetirement(SwapchainResources resources)
	{
		string? reason = resources.RetirementReason;

		if (reason is not null)
		{
			ReportDiagnostic(reason);
		}
	}

	private void ReportDiagnostic(string message)
	{
		_diagnostics.Add(message);
		_options.DiagnosticSink?.Invoke(message);
	}

	private void Present(WindowSize pixels)
	{
		UiFontSet themeFonts = _ui.Theme.Fonts;

		// Every atlas layer belongs to the font set the renderer was
		// created with. A new theme font set replaces those font
		// objects, so the generation is rebuilt before anything draws
		// with fonts it cannot map.
		bool fontSetChanged = !ReferenceEquals(themeFonts, _activeFontSet);

		if (_resources is null ||
			fontSetChanged ||
			_resources.Pixels.Width != pixels.Width ||
			_resources.Pixels.Height != pixels.Height)
		{
			Recreate(pixels, themeFonts);
		}

		SwapchainResources active = _resources
			?? throw new InvalidOperationException(
				"The swapchain was not created.");

		int slot = _frameSlot % PixelSlots;

		CurrentFrameSlot = slot;

		VulkanSwapchainFrameResult frame = active.Frames.Draw(
			(commandBuffer, imageIndex) =>
			{
				BeforeUiPass?.Invoke(commandBuffer);

				active.Renderer.Render(
					commandBuffer,
					imageIndex,
					slot,
					_geometry.Vertices,
					_geometry.Indices,
					_geometry.Batches);
			});

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
				Recreate(current, _ui.Theme.Fonts);
			}
		}
	}

	private void Recreate(WindowSize pixels, UiFontSet themeFonts)
	{
		_device.WaitIdle();

		SwapchainResources? previous = _resources;

		_resources = null;

		if (previous is not null)
		{
			previous.Dispose();
			ReportRetirement(previous);
		}

		_activeFontSet = themeFonts;

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
			themeFonts.Enumerate().ToArray(),
			_options.VertexShaderPath ?? DefaultShader("ui.vert.spv"),
			_options.FragmentShaderPath ?? DefaultShader("ui.frag.spv"));

		// Descriptor sets belong to a renderer, and a renderer belongs
		// to a swapchain generation, so every generation re-registers
		// the application's images under their stable identifiers.
		foreach (ImageRegistration registration in _images)
		{
			_resources.Renderer.RegisterImage(
				registration.Image,
				registration.Id);
		}
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

	/// <summary>
	/// One render target registered under a stable UI identifier.
	/// </summary>
	private readonly record struct ImageRegistration(
		UiTextureId Id,
		IVulkanSampledImage Image);

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

		/// <summary>
		/// Gets why the frame scheduler retained its synchronization
		/// objects, or null when they were released.
		/// </summary>
		public string? RetirementReason { get; private set; }

		public void Dispose()
		{
			// The scheduler reports rather than throws when a failed frame
			// cannot be retired safely.
			Frames.Dispose();

			RetirementReason = Frames.UnsafeRetirementReason;

			try
			{
				Renderer.Dispose();
			}

			finally
			{
				try
				{
					_imageViews.Dispose();
				}

				finally
				{
					// The swapchain owns the images the image views were
					// created from, so it is destroyed after them.
					_swapchain.Dispose();
				}
			}
		}
	}
}
