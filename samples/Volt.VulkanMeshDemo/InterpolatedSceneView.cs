using System.Numerics;
using Volt.Core.Timing;
using Volt.Ecs;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;
using Volt.Rendering.Transforms;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Presents the ECS scene with interpolated transforms while keeping
/// the scene's world, camera, and resource handles unchanged.
/// </summary>
/// <remarks>
/// <para>
/// The view is an <see cref="IEcsRenderScene"/> decorator: the Vulkan
/// renderer keeps one extraction path, and only the model matrices
/// differ. The scene's components remain authoritative; interpolation
/// is computed into the view's own record buffer.
/// </para>
/// <para>
/// The displayed weight comes from <see cref="InterpolationWeight"/>,
/// which holds the committed pose while simulation is paused or
/// frozen and keeps holding it after resume until the next fixed step.
/// That makes pause, single-step, and resume visually continuous.
/// </para>
/// </remarks>
internal sealed class InterpolatedSceneView : IEcsRenderScene
{
	private const int InstanceCapacity = 8;

	private readonly EcsDemoScene _scene;
	private readonly TransformHistory _history;
	private readonly InterpolatedRenderExtractor _extractor;
	private readonly GameLoop _loop;
	private readonly RenderInstance[] _instances =
		new RenderInstance[InstanceCapacity];

	private InterpolationWeight _weight;

	/// <summary>
	/// Creates a view over one scene.
	/// </summary>
	/// <param name="scene">The scene to present.</param>
	/// <param name="history">The transform history to read.</param>
	/// <param name="loop">The loop that drives the history.</param>
	public InterpolatedSceneView(
		EcsDemoScene scene,
		TransformHistory history,
		GameLoop loop)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(history);
		ArgumentNullException.ThrowIfNull(loop);

		_scene = scene;
		_history = history;
		_loop = loop;
		_extractor = new InterpolatedRenderExtractor(scene.World);
	}

	/// <summary>
	/// Gets the weight used by the most recent extraction.
	/// </summary>
	/// <remarks>
	/// Zero normally selects the previous pose and one selects the
	/// committed pose. The value is one while simulation is frozen and
	/// until the frame after resume that completes a fixed step.
	/// </remarks>
	public float CurrentWeight { get; private set; }

	/// <summary>
	/// Gets whether the most recent frame held the committed pose.
	/// </summary>
	public bool IsHoldingCommittedPose =>
		_weight.IsHoldingCommittedPose;

	/// <inheritdoc />
	public World World => _scene.World;

	/// <inheritdoc />
	public Entity CameraEntity => _scene.CameraEntity;

	/// <inheritdoc />
	public MeshHandle? OverlayMesh => _scene.OverlayMesh;

	/// <inheritdoc />
	public ReadOnlySpan<RenderInstance> Extract()
	{
		FrameTiming frame = _loop.LastFrame;

		CurrentWeight = _weight.Resolve(
			frame.InterpolationAlpha,
			frame.FixedSteps > 0,
			_loop.IsPaused || _loop.EffectiveTimeScale.IsZero);

		RenderExtractionResult result = _extractor.Extract(
			_instances,
			_history,
			CurrentWeight);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"The interpolated scene produced " +
				$"{result.RequiredCount} render instances, but its " +
				$"storage holds only {_instances.Length}.");
		}

		return _instances.AsSpan(0, result.WrittenCount);
	}

	/// <inheritdoc />
	public bool TryResolveMesh(
		MeshHandle handle,
		out EcsMeshBinding binding)
	{
		return _scene.TryResolveMesh(handle, out binding);
	}

	/// <inheritdoc />
	public bool ContainsMaterial(MaterialHandle handle)
	{
		return _scene.ContainsMaterial(handle);
	}

	/// <inheritdoc />
	public int CountDrawCommands()
	{
		return _scene.CountDrawCommands();
	}

	/// <inheritdoc />
	public Matrix4x4 CreateOverlayTransform(
		int framebufferWidth,
		int framebufferHeight)
	{
		return _scene.CreateOverlayTransform(
			framebufferWidth,
			framebufferHeight);
	}
}
