using System.Numerics;
using Silk.NET.Vulkan;
using Volt.Editor.Viewport;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Ui;
using Volt.Ui.Vulkan;

namespace Volt.Editor.Rendering;

/// <summary>
/// Renders the editor's viewports into offscreen Vulkan targets and
/// makes them available to Volt.Ui as image textures.
/// </summary>
/// <remarks>
/// <para>
/// Each viewport kind owns one target per frame slot. A frame records
/// its viewport passes into the target for the slot it is submitting,
/// and the UI pass of that same submission samples it, so a
/// two-slot rotation is enough to keep a write from racing the read of
/// a frame that is still in flight.
/// </para>
/// <para>
/// Targets are created on first use at the panel's physical pixel
/// size. A panel resize recreates the target, waits for the device, and
/// re-points the UI's registered image at the new one; that cost is
/// paid once per resize, not per frame.
/// </para>
/// <para>
/// Meshes are generated once and uploaded once: they do not depend on
/// a viewport, a swapchain, or a frame. The pipeline depends only on a
/// target's render pass, so it survives swapchain recreation.
/// </para>
/// <para>
/// This type is only used by the editor application. It is not
/// thread-safe and must be created, used, and disposed on the thread
/// that records frames.
/// </para>
/// </remarks>
internal sealed class EditorSceneRenderer : IViewportRenderer, IDisposable
{
	private const int Slots = 2;

	private static readonly ViewportKind[] s_kinds =
		[ViewportKind.Scene, ViewportKind.Game];

	private readonly VulkanUiHost _host;
	private readonly VulkanInstance _instance;
	private readonly VulkanLogicalDevice _device;
	private readonly PhysicalDevice _physicalDevice;
	private readonly string _vertexShaderPath;
	private readonly string _fragmentShaderPath;
	private readonly Dictionary<string, VulkanMeshBuffers> _meshes = [];
	private readonly Dictionary<ViewportKind, ViewportRenderRequest> _requests = [];
	private readonly Dictionary<(ViewportKind Kind, int Slot), SlotTarget> _targets = [];

	private VulkanSceneMeshPipeline? _pipeline;
	private bool _disposed;

	/// <summary>
	/// Initializes a renderer for one UI host.
	/// </summary>
	/// <param name="host">
	/// The host whose device, images, and frame slots are used.
	/// </param>
	/// <param name="vertexShaderPath">
	/// Path to the compiled scene vertex shader.
	/// </param>
	/// <param name="fragmentShaderPath">
	/// Path to the compiled scene fragment shader.
	/// </param>
	public EditorSceneRenderer(
		VulkanUiHost host,
		string vertexShaderPath,
		string fragmentShaderPath)
	{
		ArgumentNullException.ThrowIfNull(host);
		ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderPath);

		_host = host;
		_instance = host.Instance;
		_device = host.Device;
		_physicalDevice = host.Gpu;
		_vertexShaderPath = vertexShaderPath;
		_fragmentShaderPath = fragmentShaderPath;

		try
		{
			foreach (EditorMeshInfo mesh in EditorMeshLibrary.Meshes)
			{
				_meshes.Add(
					mesh.Id,
					VulkanMeshBuffers.Create(
						_instance,
						_device,
						_physicalDevice,
						EditorMeshLibrary.CreateVertices(mesh.Id).AsSpan()));
			}

			_meshes.Add(
				EditorMeshLibrary.Grid,
				VulkanMeshBuffers.Create(
					_instance,
					_device,
					_physicalDevice,
					EditorMeshLibrary.CreateVertices(
						EditorMeshLibrary.Grid).AsSpan()));
		}

		catch
		{
			DisposeMeshes();
			throw;
		}
	}

	/// <summary>
	/// Gets or sets the frame slot the current frame records into.
	/// </summary>
	/// <remarks>
	/// The editor sets this from
	/// <see cref="VulkanUiHost.CurrentFrameSlot"/> before painting so a
	/// viewport can name the image its frame will fill. Nothing is
	/// allocated until a viewport actually paints.
	/// </remarks>
	public int FrameSlot { get; set; }

	/// <summary>
	/// Publishes what a viewport wants rendered this frame.
	/// </summary>
	/// <param name="request">The request to store.</param>
	/// <remarks>
	/// The request instance is kept, not copied, so a viewport reuses
	/// one instance per frame instead of allocating. A viewport that
	/// stops painting leaves its last request behind only until its
	/// next <see cref="ClearRequest"/> call or its own removal.
	/// </remarks>
	public void PublishRequest(ViewportRenderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		_requests[request.Kind] = request;
	}

	/// <summary>
	/// Removes a viewport's request, so nothing is rendered for it.
	/// </summary>
	public void ClearRequest(ViewportKind kind)
	{
		_requests.Remove(kind);
	}

	/// <summary>
	/// Gets the image texture identifier a viewport draws for a slot,
	/// preparing the target if it does not exist yet.
	/// </summary>
	/// <param name="kind">The viewport kind.</param>
	/// <param name="slot">The frame slot.</param>
	/// <returns>
	/// The identifier to draw, or null when there is nothing to draw
	/// yet: no request, a zero-sized panel, or a target that could not
	/// be created.
	/// </returns>
	/// <remarks>
	/// This is called while the widget paints, which is before the
	/// frame's command buffer is recorded, so the target it prepares is
	/// the one that frame fills.
	/// </remarks>
	public UiTextureId? PrepareTexture(ViewportKind kind, int slot)
	{
		ThrowIfDisposed();

		if (!_requests.TryGetValue(kind, out ViewportRenderRequest? request) ||
			!request.HasContent ||
			request.PixelWidth <= 0 ||
			request.PixelHeight <= 0)
		{
			return null;
		}

		int normalized = NormalizeSlot(slot);

		return EnsureTarget(kind, normalized, request).TextureId;
	}

	/// <summary>
	/// Records every viewport pass for the current frame.
	/// </summary>
	/// <param name="commandBuffer">
	/// A recording command buffer outside any render pass.
	/// </param>
	/// <remarks>
	/// This is the host's <c>BeforeUiPass</c> callback: everything it
	/// records is ordered before the UI pass that samples these
	/// targets.
	/// </remarks>
	public void RecordFrame(CommandBuffer commandBuffer)
	{
		ThrowIfDisposed();

		int slot = NormalizeSlot(FrameSlot);

		foreach (ViewportKind kind in s_kinds)
		{
			if (!_requests.TryGetValue(kind, out ViewportRenderRequest? request) ||
				!request.HasContent)
			{
				continue;
			}

			if (!_targets.TryGetValue((kind, slot), out SlotTarget? target))
			{
				continue;
			}

			RecordViewport(commandBuffer, target, request, slot);
		}
	}

	/// <summary>
	/// Releases every mesh, target, and pipeline.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The caller must stop presenting before disposing, because the UI
	/// renderer's descriptor sets reference these targets.
	/// </para>
	/// <para>
	/// Disposal waits for the device first. The host submits a frame
	/// whose command buffer records this pipeline, the pipeline is
	/// destroyed here, and the host's own idle only runs later when the
	/// host is disposed. Without this wait the GPU can still be
	/// executing a recorded draw when the pipeline is destroyed, which
	/// is exactly what <c>VUID-vkDestroyPipeline-pipeline-00765</c>
	/// reports at shutdown.
	/// </para>
	/// </remarks>
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
			// A lost device cannot accept the wait, but the remaining
			// native resources still have to be released.
		}

		_pipeline?.Dispose();
		_pipeline = null;

		foreach (SlotTarget target in _targets.Values)
		{
			_host.UnregisterImage(target.TextureId);
			target.Target.Dispose();
		}

		_targets.Clear();
		DisposeMeshes();
	}

	private void RecordViewport(
		CommandBuffer commandBuffer,
		SlotTarget slotTarget,
		ViewportRenderRequest request,
		int slot)
	{
		VulkanOffscreenRenderTarget target = slotTarget.Target;

		target.EnsureRenderingLayout(commandBuffer);

		ClearColorValue clear = new(
			float32_0: request.ClearColor.X,
			float32_1: request.ClearColor.Y,
			float32_2: request.ClearColor.Z,
			float32_3: request.ClearColor.W);

		target.RecordBegin(commandBuffer, in clear);

		VulkanSceneMeshPipeline pipeline = EnsurePipeline(target);

		pipeline.BeginViewport(
			commandBuffer,
			slot,
			new VulkanSceneLight(
				request.Light.UnitDirection,
				request.Light.DiffuseColor));

		Extent2D extent = target.Extent;

		if (request.ShowGrid &&
			_meshes.TryGetValue(EditorMeshLibrary.Grid, out VulkanMeshBuffers? grid))
		{
			pipeline.Record(
				commandBuffer,
				extent,
				grid,
				request.View * request.Projection,
				Matrix4x4.Identity);
		}

		foreach (ViewportMeshInstance instance in request.Instances)
		{
			if (!_meshes.TryGetValue(
					instance.MeshId,
					out VulkanMeshBuffers? mesh))
			{
				continue;
			}

			pipeline.Record(
				commandBuffer,
				extent,
				mesh,
				instance.World * request.View * request.Projection,
				instance.World);
		}

		target.RecordEnd(commandBuffer);
	}

	private VulkanSceneMeshPipeline EnsurePipeline(
		VulkanOffscreenRenderTarget target)
	{
		if (_pipeline is not null)
		{
			return _pipeline;
		}

		_pipeline = new VulkanSceneMeshPipeline(
			_instance,
			_device,
			_physicalDevice,
			target.RenderPass,
			_vertexShaderPath,
			_fragmentShaderPath);

		return _pipeline;
	}

	private SlotTarget EnsureTarget(
		ViewportKind kind,
		int slot,
		ViewportRenderRequest request)
	{
		Extent2D extent = new(
			checked((uint)request.PixelWidth),
			checked((uint)request.PixelHeight));

		(int Width, int Height) requested =
			((int)extent.Width, (int)extent.Height);

		if (_targets.TryGetValue((kind, slot), out SlotTarget? existing))
		{
			if (existing.Extent == requested)
			{
				return existing;
			}

			// A resize waits for the device and re-points the UI's
			// registered image at the recreated target. The frame
			// after the resize draws it.
			existing.Target.Resize(extent);

			_host.UpdateImage(existing.TextureId, existing.Target);

			existing.Extent = requested;

			return existing;
		}

		VulkanOffscreenRenderTarget created = new(
			_instance,
			_device,
			_physicalDevice,
			extent);

		UiTextureId id = _host.RegisterImage(created);

		SlotTarget createdSlot = new(
			created,
			id,
			requested);

		_targets.Add((kind, slot), createdSlot);

		return createdSlot;
	}

	private static int NormalizeSlot(int slot)
	{
		int normalized = slot % Slots;

		return normalized < 0 ? normalized + Slots : normalized;
	}

	private void DisposeMeshes()
	{
		foreach (VulkanMeshBuffers mesh in _meshes.Values)
		{
			mesh.Dispose();
		}

		_meshes.Clear();
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	/// <summary>
	/// One viewport's target for one frame slot, plus the UI identifier
	/// its image is registered under.
	/// </summary>
	private sealed class SlotTarget(
		VulkanOffscreenRenderTarget target,
		UiTextureId textureId,
		(int Width, int Height) extent)
	{
		public VulkanOffscreenRenderTarget Target { get; } = target;

		public UiTextureId TextureId { get; } = textureId;

		public (int Width, int Height) Extent { get; set; } = extent;
	}
}
