namespace Volt.VulkanMeshDemo;

/// <summary>
/// Identifies a scripted ECS checkpoint emitted for one accepted
/// frame.
/// </summary>
internal enum EcsScriptCheckpoint
{
	/// <summary>
	/// No checkpoint occurred on this frame.
	/// </summary>
	None,

	/// <summary>
	/// The first accepted frame presented the initial scene.
	/// </summary>
	InitialState,

	/// <summary>
	/// The scripted components were changed after this accepted
	/// frame was presented.
	/// </summary>
	ChangeApplied,

	/// <summary>
	/// The changed state has been presented for a stable number of
	/// accepted frames.
	/// </summary>
	ChangedStateReached
}

/// <summary>
/// Drives deterministic ECS component mutations for headless
/// verification.
/// </summary>
/// <remarks>
/// <para>
/// The script advances once per accepted presented frame, never per
/// attempted frame, so presentation retries and swapchain recreation
/// cannot mutate the world extra times. It presents the initial
/// scene through <see cref="InitialHoldFrames"/> frames, then mutates
/// one mesh entity and the camera entity in place, and holds that
/// state for the rest of the run.
/// </para>
/// <para>
/// The change is applied only from the accepted-presentation path,
/// so it never occurs while a frame is being recorded. The new
/// position is visible on the next recorded frame.
/// </para>
/// </remarks>
internal sealed class EcsScript
{
	/// <summary>
	/// Accepted frames presented at the initial scene state.
	/// </summary>
	public const int InitialHoldFrames = 200;

	/// <summary>
	/// Accepted frames the changed state is presented before its
	/// checkpoint is reported.
	/// </summary>
	public const int ChangedStateSettleFrames = 10;

	/// <summary>
	/// Accepted frame after which the components are changed.
	/// </summary>
	public const int ChangeFrame = InitialHoldFrames;

	/// <summary>
	/// Accepted frame on which the changed-state checkpoint is
	/// reported.
	/// </summary>
	public const int ChangedStateFrame =
		ChangeFrame + ChangedStateSettleFrames;

	/// <summary>
	/// Smallest frame limit that reaches the changed-state
	/// checkpoint.
	/// </summary>
	public const int MinimumFrameLimit = ChangedStateFrame;

	private int _acceptedFrames;
	private bool _initialStateReported;
	private bool _changeApplied;
	private bool _changedStateReported;

	/// <summary>
	/// Gets the number of accepted frames advanced so far.
	/// </summary>
	public int AcceptedFrames => _acceptedFrames;

	/// <summary>
	/// Gets the frame on which the changed-state checkpoint was
	/// emitted, or zero before it is reached.
	/// </summary>
	public int ChangedStateFrameNumber { get; private set; }

	/// <summary>
	/// Gets whether the scripted component change has been applied.
	/// </summary>
	public bool HasAppliedChange => _changeApplied;

	/// <summary>
	/// Gets whether the changed state has been reported.
	/// </summary>
	public bool HasReachedChangedState => _changedStateReported;

	/// <summary>
	/// Gets the number of accepted frames that have used the changed
	/// state, including the frame that reported it.
	/// </summary>
	public int FramesHoldingChangedState =>
		_changedStateReported
			? _acceptedFrames - ChangedStateFrameNumber + 1
			: 0;

	/// <summary>
	/// Advances the script by one accepted frame.
	/// </summary>
	/// <param name="scene">The scene whose components to mutate.</param>
	/// <returns>The checkpoint this frame produced, if any.</returns>
	public EcsScriptCheckpoint Advance(EcsDemoScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_acceptedFrames++;

		if (!_initialStateReported)
		{
			_initialStateReported = true;
			return EcsScriptCheckpoint.InitialState;
		}

		if (_acceptedFrames == ChangeFrame)
		{
			scene.ApplyScriptedChange();
			_changeApplied = true;

			return EcsScriptCheckpoint.ChangeApplied;
		}

		if (_changeApplied &&
			!_changedStateReported &&
			_acceptedFrames >= ChangedStateFrame)
		{
			_changedStateReported = true;
			ChangedStateFrameNumber = _acceptedFrames;

			return EcsScriptCheckpoint.ChangedStateReached;
		}

		return EcsScriptCheckpoint.None;
	}
}
