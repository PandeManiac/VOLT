using Volt.Core.Timing;
using Volt.Rendering.Cameras;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Maps one engine update onto the sample's camera and gameplay.
/// </summary>
/// <remarks>
/// <para>
/// The driver is registered once as a <see cref="UpdateCallback"/>.
/// It samples the already-completed input frame exactly once no
/// matter how many fixed steps the loop executed, so a
/// <c>WasKeyPressed</c> edge or a short click cannot be replayed for
/// each step.
/// </para>
/// <para>
/// Camera integration uses the update's real elapsed time, not the
/// fixed delta: the camera is a presentation concern, so it stays
/// smooth when a frame executes zero fixed steps. The target range
/// consumes only the one-shot edges the sampler reports.
/// </para>
/// <para>
/// Instances allocate nothing per frame; the only allocation is the
/// delegate created when the loop registration is made.
/// </para>
/// </remarks>
internal sealed class SampleFrameDriver
{
	private readonly CameraController _camera;
	private readonly CameraInput _cameraInput;
	private readonly TargetRangeController? _targetRange;
	private readonly bool _interactiveCamera;

	/// <summary>
	/// Creates a driver for one sample configuration.
	/// </summary>
	/// <param name="camera">The active camera.</param>
	/// <param name="cameraInput">The engine input adapter.</param>
	/// <param name="interactiveCamera">
	/// Whether this scene moves the camera with keyboard and mouse
	/// input.
	/// </param>
	/// <param name="targetRange">
	/// The gameplay controller for the playable range, if any.
	/// </param>
	public SampleFrameDriver(
		CameraController camera,
		CameraInput cameraInput,
		bool interactiveCamera,
		TargetRangeController? targetRange)
	{
		ArgumentNullException.ThrowIfNull(camera);
		ArgumentNullException.ThrowIfNull(cameraInput);

		_camera = camera;
		_cameraInput = cameraInput;
		_interactiveCamera = interactiveCamera;
		_targetRange = targetRange;
	}

	/// <summary>
	/// Gets how many updates the driver has processed.
	/// </summary>
	public int UpdateCount { get; private set; }

	/// <summary>
	/// Gets the input sampled by the most recent update.
	/// </summary>
	public CameraInputSnapshot LastInput { get; private set; }

	/// <summary>
	/// Gets the gameplay result produced by the most recent update.
	/// </summary>
	public TargetRangeFrameResult LastGameplay { get; private set; }

	/// <summary>
	/// Processes one application frame.
	/// </summary>
	/// <param name="context">The completed frame timing.</param>
	public void Update(in UpdateContext context)
	{
		UpdateCount++;

		if (!_interactiveCamera && _targetRange is null)
		{
			return;
		}

		LastInput = _cameraInput.Update(_camera, context.DeltaSeconds);

		if (_targetRange is null)
		{
			return;
		}

		LastGameplay = _targetRange.Update(
			TargetRangeCommand.FromInput(LastInput),
			_camera);
	}
}
