using System.Globalization;
using System.Numerics;
using Volt.Core.Timing;
using Volt.Rendering.Transforms;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Demonstrates simulation time scaling and interpolated ECS
/// transforms in the real Vulkan scene.
/// </summary>
/// <remarks>
/// <para>
/// Triangle A moves one fixed-step position every simulation tick
/// along a short ping-pong path. A scheduled callback rotates
/// triangle B every half simulation second. Transform history is
/// captured after every completed step, including the scheduled
/// mutation, and the render path draws the interpolated matrices
/// rather than the plain component matrices.
/// </para>
/// <para>
/// The interactive mode uses the shared monotonic clock and real
/// elapsed time. <c>--interpolation-script</c> injects exact frame
/// deltas from accepted presentation counts, so fractional alphas,
/// time-scale transitions, pause, single step, and resume are
/// reproducible independently of presentation speed.
/// </para>
/// <para>
/// Every mutation is an in-place component write on the game thread
/// before extraction. No entity is created or destroyed, and no GPU
/// resource is touched from a callback.
/// </para>
/// </remarks>
internal sealed class InterpolationDemo
{
	/// <summary>
	/// Fixed simulation rate used by the demonstration.
	/// </summary>
	public const int FixedUpdatesPerSecond =
		GameLoopSettings.DefaultFixedUpdatesPerSecond;

	/// <summary>
	/// World-space distance travelled by the moving entity in one
	/// fixed step.
	/// </summary>
	public const float MotionAmplitude = 0.3f;

	/// <summary>
	/// Steps in one direction of the ping-pong motion.
	/// </summary>
	public const int MotionHalfPeriodSteps = 6;

	/// <summary>
	/// Horizontal offset from the entity's authored rest position.
	/// </summary>
	public const float MotionBaseOffsetX = -0.15f;

	/// <summary>
	/// Uniform scale applied to the moving entity for this mode.
	/// </summary>
	/// <remarks>
	/// A small entity makes the distance between the previous,
	/// interpolated, and current poses visible on screen, which is
	/// what the deterministic screenshot probes check.
	/// </remarks>
	public const float MotionScale = 0.1f;

	/// <summary>
	/// Simulation interval between scheduled rotations.
	/// </summary>
	public static readonly TimeSpan RotationInterval =
		TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// Accepted frame on which the scripted run pauses.
	/// </summary>
	public const int PauseFrame = 331;

	/// <summary>
	/// Accepted frame on which the scripted run requests one step.
	/// </summary>
	public const int SingleStepFrame = 351;

	/// <summary>
	/// Accepted frame on which the scripted run resumes.
	/// </summary>
	public const int ResumeFrame = 361;

	/// <summary>
	/// Smallest bounded run that reaches the scripted final state.
	/// </summary>
	public const int MinimumScriptedFrames = 600;

	private readonly EcsDemoScene _scene;
	private readonly Vector3 _movingRest;
	private readonly Quaternion _rotatingRestRotation;
	private readonly ScriptedFrameClock? _scriptedClock;
	private readonly TransformHistory _history;
	private readonly InterpolatedSceneView _view;

	private long _capturedSteps;

	/// <summary>
	/// Creates the demonstration and registers its timing work.
	/// </summary>
	/// <param name="scene">The ECS scene to mutate.</param>
	/// <param name="scripted">
	/// <c>true</c> to use the deterministic frame clock.
	/// </param>
	public InterpolationDemo(EcsDemoScene scene, bool scripted)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;
		_movingRest = scene.GetPosition(scene.TriangleAEntity);
		_rotatingRestRotation = scene.GetRotation(scene.TriangleBEntity);

		_scriptedClock = scripted ? new ScriptedFrameClock() : null;

		Loop = new GameLoop(
			GameLoopSettings.Default,
			_scriptedClock ?? (IMonotonicClock)MonotonicClock.Shared);

		// The moving entity is smaller than in the static scene so
		// the three interpolated positions are distinguishable.
		_scene.SetScale(
			_scene.TriangleAEntity,
			new Vector3(MotionScale));

		_scene.SetPosition(
			_scene.TriangleAEntity,
			new Vector3(MovingBaseX, _movingRest.Y, _movingRest.Z));

		_history = new TransformHistory(scene.World, initialCapacity: 8);
		_view = new InterpolatedSceneView(scene, _history, Loop);

		Loop.RegisterPostFixedUpdate(OnPostFixedUpdate);
		Loop.RegisterFixedUpdate(OnFixedUpdate);
		Loop.Schedule.Every(RotationInterval, OnScheduledRotation);

		// The scripted clock injects one frame before the first
		// Advance, so accepted frame one already executes step one.
		_scriptedClock?.AdvanceFrame();
	}

	/// <summary>
	/// Gets the loop that owns the demonstration's timing work.
	/// </summary>
	public GameLoop Loop { get; }

	/// <summary>
	/// Gets the transform history captured after every fixed step.
	/// </summary>
	public TransformHistory History => _history;

	/// <summary>
	/// Gets the interpolated render view the sample draws.
	/// </summary>
	public InterpolatedSceneView View => _view;

	/// <summary>
	/// Gets the scene the demonstration mutates.
	/// </summary>
	public EcsDemoScene Scene => _scene;

	/// <summary>
	/// Gets whether the demonstration uses the scripted clock.
	/// </summary>
	public bool IsScripted => _scriptedClock is not null;

	/// <summary>
	/// Gets how many fixed steps captured history.
	/// </summary>
	public long CapturedSteps => _capturedSteps;

	/// <summary>
	/// Gets how many scheduled rotations have run.
	/// </summary>
	public long ScheduledRotations { get; private set; }

	/// <summary>
	/// Gets the moving entity.
	/// </summary>
	public Volt.Ecs.Entity MovingEntity => _scene.TriangleAEntity;

	/// <summary>
	/// Gets the horizontal position at which the motion starts.
	/// </summary>
	public float MovingBaseX => _movingRest.X + MotionBaseOffsetX;

	/// <summary>
	/// Gets the moving entity's current horizontal position.
	/// </summary>
	public float MovingX => _scene.GetPosition(_scene.TriangleAEntity).X;

	/// <summary>
	/// Gets the sub-step time accumulated toward the next fixed
	/// step, in accumulator units of 1/1,000,000,000 step.
	/// </summary>
	public long AccumulatedUnits => (long)Math.Round(
		Loop.LastFrame.AccumulatedRemainderSeconds *
		FixedUpdatesPerSecond *
		1_000_000_000.0);

	/// <summary>
	/// Computes the moving entity's horizontal position for a step.
	/// </summary>
	/// <param name="stepIndex">The one-based fixed step.</param>
	/// <remarks>
	/// The entity moves one amplitude per step for half a period,
	/// then reverses. The turn is a direction change, not a
	/// teleport, so interpolation stays continuous across it.
	/// </remarks>
	public static float MotionOffset(long stepIndex)
	{
		const int period = MotionHalfPeriodSteps * 2;

		long phase = (stepIndex - 1) % period;

		if (phase < 0)
		{
			phase += period;
		}

		long triangle = phase <= MotionHalfPeriodSteps
			? phase
			: period - phase;

		return MotionAmplitude * triangle;
	}

	/// <summary>
	/// Gets the horizontal position the moving entity has at a step.
	/// </summary>
	public float MotionX(long stepIndex)
	{
		return MovingBaseX + MotionOffset(stepIndex);
	}

	/// <summary>
	/// Injects one frame of elapsed time in scripted mode.
	/// </summary>
	public void AdvanceScriptedFrame()
	{
		_scriptedClock?.AdvanceFrame();
	}

	/// <summary>
	/// Injects exact elapsed nanoseconds in scripted mode.
	/// </summary>
	/// <param name="nanoseconds">The elapsed interval.</param>
	public void AdvanceScriptedNanoseconds(long nanoseconds)
	{
		if (_scriptedClock is null)
		{
			throw new InvalidOperationException(
				"Only the scripted interpolation demo accepts injected " +
				"elapsed time.");
		}

		_scriptedClock.AdvanceNanoseconds(nanoseconds);
	}

	/// <summary>
	/// Sets the simulation time scale.
	/// </summary>
	public void SetTimeScale(float scale)
	{
		Loop.TimeScale = scale;
	}

	/// <summary>
	/// Pauses the demonstration's loop.
	/// </summary>
	public void Pause()
	{
		Loop.Pause();
	}

	/// <summary>
	/// Requests exactly one fixed step from the paused loop.
	/// </summary>
	public bool StepOnce()
	{
		return Loop.StepOnce();
	}

	/// <summary>
	/// Resumes the demonstration's loop.
	/// </summary>
	public void Resume()
	{
		Loop.Resume();
	}

	/// <summary>
	/// Writes one summary line with the demonstration's counters.
	/// </summary>
	/// <param name="prefix">A label that starts the line.</param>
	public void WriteSummary(string prefix)
	{
		Console.WriteLine(
			$"{prefix}: steps={Loop.TotalFixedSteps}, " +
			$"scale={Loop.EffectiveTimeScale.Value.ToString("F2", CultureInfo.InvariantCulture)}, " +
			$"paused={Loop.IsPaused}, " +
			$"rotations={ScheduledRotations}, " +
			$"alpha={Loop.InterpolationAlpha.ToString("F6", CultureInfo.InvariantCulture)}, " +
			$"weight={_view.CurrentWeight.ToString("F6", CultureInfo.InvariantCulture)}, " +
			$"triangleA.x={MovingX.ToString("F3", CultureInfo.InvariantCulture)}, " +
			$"dropped={Loop.TotalDroppedTime.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}ms.");
	}

	private void OnFixedUpdate(in FixedUpdateContext context)
	{
		_scene.SetPosition(
			_scene.TriangleAEntity,
			new Vector3(
				MotionX(context.StepIndex),
				_movingRest.Y,
				_movingRest.Z));
	}

	private void OnScheduledRotation(in ScheduledUpdateContext context)
	{
		ScheduledRotations++;

		float degrees = (ScheduledRotations % 12) * 30.0f;

		_scene.SetRotation(
			_scene.TriangleBEntity,
			Quaternion.Normalize(
				Quaternion.CreateFromAxisAngle(
					Vector3.UnitZ,
					degrees * (MathF.PI / 180.0f)) *
				_rotatingRestRotation));
	}

	private void OnPostFixedUpdate(in PostFixedUpdateContext context)
	{
		_capturedSteps++;

		_history.Capture();
	}
}
