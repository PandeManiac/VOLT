using System.Globalization;
using System.Numerics;
using Volt.Core.Timing;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Demonstrates the engine timing foundation inside the ECS scene.
/// </summary>
/// <remarks>
/// <para>
/// Three registered behaviors use three different timing mechanisms:
/// </para>
/// <list type="bullet">
/// <item>
/// A fixed-update callback moves triangle A once per simulation
/// tick along a repeating ramp-and-hold path.
/// </item>
/// <item>
/// A 10 Hz scheduled callback rotates triangle B in place, which
/// proves a scheduled frequency derived from the fixed rate.
/// </item>
/// <item>
/// A two-simulation-second recurring callback shifts the hexagon,
/// and a five-second one-shot grows it.
/// </item>
/// </list>
/// <para>
/// Every mutation is an in-place component write on the game thread,
/// before render extraction, so no entity, buffer, pipeline, or
/// material is created during the run. The existing scene is
/// unchanged unless <c>--timing-demo</c> is supplied.
/// </para>
/// </remarks>
internal sealed class TimingDemo
{
	/// <summary>
	/// Fixed simulation rate used by the demonstration.
	/// </summary>
	public const int FixedUpdatesPerSecond =
		GameLoopSettings.DefaultFixedUpdatesPerSecond;

	/// <summary>
	/// Steps in one pulse cycle (two simulation seconds).
	/// </summary>
	public const int PulsePeriodSteps = 120;

	/// <summary>
	/// Maximum horizontal displacement of the pulse entity.
	/// </summary>
	public const float PulseAmplitude = 0.9f;

	/// <summary>
	/// Frequency of the in-place triangle rotation.
	/// </summary>
	public const int TenHertz = 10;

	/// <summary>
	/// Horizontal shift applied by the two-second event.
	/// </summary>
	public const float HexagonShiftX = -0.35f;

	/// <summary>
	/// Hexagon scale applied by the one-shot event.
	/// </summary>
	public const float OneShotHexagonScale = 0.45f;

	/// <summary>
	/// Accepted frame on which the scripted run pauses.
	/// </summary>
	public const int PauseFrame = 360;

	/// <summary>
	/// Accepted frame on which the scripted run requests one step.
	/// </summary>
	public const int SingleStepFrame = 370;

	/// <summary>
	/// Accepted frame on which the scripted run resumes.
	/// </summary>
	public const int ResumeFrame = 380;

	/// <summary>
	/// Smallest bounded run that reaches the scripted final state.
	/// </summary>
	public const int MinimumScriptedFrames = 600;

	private const int RampSteps = 30;

	private readonly EcsDemoScene _scene;
	private readonly Vector3 _triangleARest;
	private readonly Vector3 _hexagonRest;
	private readonly Quaternion _triangleBRestRotation;
	private readonly ScriptedFrameClock? _scriptedClock;

	/// <summary>
	/// Creates the demonstration and registers its timing work.
	/// </summary>
	/// <param name="scene">The ECS scene to mutate.</param>
	/// <param name="scripted">
	/// <c>true</c> to use the deterministic frame clock instead of
	/// the shared monotonic clock.
	/// </param>
	public TimingDemo(EcsDemoScene scene, bool scripted)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;
		_triangleARest = scene.GetPosition(scene.TriangleAEntity);
		_hexagonRest = scene.GetPosition(scene.HexagonEntity);
		_triangleBRestRotation = scene.GetRotation(scene.TriangleBEntity);

		_scriptedClock = scripted ? new ScriptedFrameClock() : null;

		Loop = new GameLoop(
			GameLoopSettings.Default,
			_scriptedClock ?? (IMonotonicClock)MonotonicClock.Shared);

		Loop.RegisterFixedUpdate(OnFixedUpdate);

		Loop.Schedule.EveryHz(TenHertz, OnTenHertz);
		Loop.Schedule.Every(
			TimeSpan.FromSeconds(2),
			OnTwoSecondEvent);
		Loop.Schedule.After(
			TimeSpan.FromSeconds(5),
			OnOneShotEvent);

		// The scripted clock injects one frame before the first
		// Advance, so accepted frame one already executes step one.
		_scriptedClock?.AdvanceFrame();
	}

	/// <summary>
	/// Gets the loop that owns the demonstration's timing work.
	/// </summary>
	public GameLoop Loop { get; }

	/// <summary>
	/// Gets whether the demonstration uses the scripted frame clock.
	/// </summary>
	public bool IsScripted => _scriptedClock is not null;

	/// <summary>
	/// Gets how many fixed steps moved the pulse entity.
	/// </summary>
	public long PulseTicks { get; private set; }

	/// <summary>
	/// Gets how many 10 Hz callbacks have run.
	/// </summary>
	public long TenHertzTicks { get; private set; }

	/// <summary>
	/// Gets how many two-second events have run.
	/// </summary>
	public long TwoSecondEvents { get; private set; }

	/// <summary>
	/// Gets how many one-shot events have run.
	/// </summary>
	public long OneShotEvents { get; private set; }

	/// <summary>
	/// Gets the pulse entity's most recent horizontal position.
	/// </summary>
	public float PulseX => _scene.GetPosition(_scene.TriangleAEntity).X;

	/// <summary>
	/// Gets the injected clock, when the demonstration is scripted.
	/// </summary>
	public ScriptedFrameClock? ScriptedClock => _scriptedClock;

	/// <summary>
	/// Computes the pulse entity's horizontal position for a step.
	/// </summary>
	/// <param name="stepIndex">The one-based fixed step.</param>
	/// <remarks>
	/// The path ramps from the rest position to the maximum over
	/// 30 steps, holds for 30, returns through the rest position to
	/// the minimum over 30, then holds for 30. The holds make a
	/// screenshot checkpoint stable for half a second without
	/// changing the per-tick animation.
	/// </remarks>
	public static float PulseOffset(long stepIndex)
	{
		long phase = stepIndex % PulsePeriodSteps;

		return phase switch
		{
			<= RampSteps => PulseAmplitude * (phase / (float)RampSteps),
			<= RampSteps * 2 => PulseAmplitude,
			<= RampSteps * 3 => PulseAmplitude *
				(1.0f - (2.0f * ((phase - (RampSteps * 2)) /
					(float)RampSteps))),
			_ => -PulseAmplitude
		};
	}

	/// <summary>
	/// Injects one frame of elapsed time in scripted mode.
	/// </summary>
	/// <remarks>
	/// The interactive demonstration does nothing here; only
	/// <c>--timing-script</c> advances a fake clock, and it does so
	/// after an accepted presentation. Production timing never depends
	/// on presentation acceptance.
	/// </remarks>
	public void AdvanceScriptedFrame()
	{
		_scriptedClock?.AdvanceFrame();
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
	/// Verifies the deterministic scripted outcome.
	/// </summary>
	/// <param name="acceptedFrames">
	/// Accepted frames presented before verification.
	/// </param>
	/// <exception cref="InvalidOperationException">
	/// A counter, position, or timing value differs from the value the
	/// scripted mode documents.
	/// </exception>
	public void VerifyScriptedOutcome(int acceptedFrames)
	{
		if (acceptedFrames < MinimumScriptedFrames)
		{
			throw new InvalidOperationException(
				$"The timing script needs at least " +
				$"{MinimumScriptedFrames} accepted frames.");
		}

		long pausedFrames = ResumeFrame - PauseFrame;
		long expectedSteps = acceptedFrames - pausedFrames + 1;

		RequireEqual("fixed steps", expectedSteps, Loop.TotalFixedSteps);
		RequireEqual("pulse ticks", expectedSteps, PulseTicks);
		RequireEqual("10 Hz ticks", expectedSteps / 6, TenHertzTicks);
		RequireEqual(
			"two-second events",
			expectedSteps / PulsePeriodSteps,
			TwoSecondEvents);
		RequireEqual(
			"one-shot events",
			expectedSteps >= 300 ? 1 : 0,
			OneShotEvents);

		if (Loop.TotalDroppedTime != TimeSpan.Zero)
		{
			throw new InvalidOperationException(
				$"The scripted run dropped " +
				$"{Loop.TotalDroppedTime.TotalMilliseconds:F3} ms of " +
				"simulation time.");
		}

		// After resume, each injected frame contributes exactly one
		// step and 20 units of remainder. The last accepted frame's
		// injected time is not consumed until the next Advance.
		double expectedAlpha =
			(acceptedFrames - ResumeFrame) * 20.0 / 1_000_000_000.0;

		if (Math.Abs(Loop.InterpolationAlpha - expectedAlpha) > 0.0000002)
		{
			throw new InvalidOperationException(
				$"The scripted interpolation alpha was " +
				$"{Loop.InterpolationAlpha:F9}, expected " +
				$"{expectedAlpha:F9}.");
		}

		float expectedPulseX =
			_triangleARest.X + PulseOffset(expectedSteps);

		if (Math.Abs(PulseX - expectedPulseX) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The scripted pulse ended at x={PulseX:F3}, expected " +
				$"{expectedPulseX:F3}.");
		}

		if (Math.Abs(
				_scene.GetScale(_scene.HexagonEntity).X -
				OneShotHexagonScale) > 0.0001f)
		{
			throw new InvalidOperationException(
				"The one-shot event did not resize the hexagon.");
		}
	}

	/// <summary>
	/// Writes one summary line with the demonstration's counters.
	/// </summary>
	/// <param name="prefix">A label that starts the line.</param>
	public void WriteSummary(string prefix)
	{
		Console.WriteLine(
			$"{prefix}: steps={Loop.TotalFixedSteps}, " +
			$"pulses={PulseTicks}, tenHz={TenHertzTicks}, " +
			$"twoSecond={TwoSecondEvents}, " +
			$"oneShot={OneShotEvents}, " +
			$"alpha={Loop.InterpolationAlpha.ToString("F6", CultureInfo.InvariantCulture)}, " +
			$"dropped={Loop.TotalDroppedTime.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}ms, " +
			$"triangleA.x={PulseX.ToString("F3", CultureInfo.InvariantCulture)}.");
	}

	private void OnFixedUpdate(in FixedUpdateContext context)
	{
		PulseTicks++;

		long phase = context.StepIndex % PulsePeriodSteps;
		float x = _triangleARest.X + PulseOffset(context.StepIndex);

		_scene.SetPosition(
			_scene.TriangleAEntity,
			new Vector3(x, _triangleARest.Y, _triangleARest.Z));

		if (_scriptedClock is null)
		{
			return;
		}

		if (phase == RampSteps)
		{
			Console.WriteLine(
				$"Timing demo pulse at maximum (step " +
				$"{context.StepIndex}): triangleA.x=" +
				$"{x.ToString("F3", CultureInfo.InvariantCulture)}.");
		}
		else if (phase == RampSteps * 3)
		{
			Console.WriteLine(
				$"Timing demo pulse at minimum (step " +
				$"{context.StepIndex}): triangleA.x=" +
				$"{x.ToString("F3", CultureInfo.InvariantCulture)}.");
		}
	}

	private void OnTenHertz(in ScheduledUpdateContext context)
	{
		TenHertzTicks++;

		float degrees = (TenHertzTicks % 24) * 15.0f;

		_scene.SetRotation(
			_scene.TriangleBEntity,
			Quaternion.Normalize(
				Quaternion.CreateFromAxisAngle(
					Vector3.UnitZ,
					degrees * (MathF.PI / 180.0f)) *
				_triangleBRestRotation));

		if (_scriptedClock is not null &&
			(context.Invocation == 1 || context.Invocation % 10 == 0))
		{
			Console.WriteLine(
				$"Timing demo 10 Hz tick {context.Invocation} at " +
				$"step {context.StepIndex}: triangleB.degrees=" +
				$"{degrees.ToString("F0", CultureInfo.InvariantCulture)}.");
		}
	}

	private void OnTwoSecondEvent(in ScheduledUpdateContext context)
	{
		TwoSecondEvents++;

		float x = _hexagonRest.X +
			((TwoSecondEvents % 2) == 1 ? HexagonShiftX : 0.0f);

		_scene.SetPosition(
			_scene.HexagonEntity,
			new Vector3(x, _hexagonRest.Y, _hexagonRest.Z));

		if (_scriptedClock is not null)
		{
			Console.WriteLine(
				$"Timing demo two-second event {TwoSecondEvents} at " +
				$"step {context.StepIndex}: hexagon.x=" +
				$"{x.ToString("F3", CultureInfo.InvariantCulture)}.");
		}
	}

	private void OnOneShotEvent(in ScheduledUpdateContext context)
	{
		OneShotEvents++;

		_scene.SetScale(
			_scene.HexagonEntity,
			new Vector3(OneShotHexagonScale));

		if (_scriptedClock is not null)
		{
			Console.WriteLine(
				$"Timing demo one-shot event at step " +
				$"{context.StepIndex}: hexagon scale=" +
				$"{OneShotHexagonScale.ToString("F3", CultureInfo.InvariantCulture)}.");
		}
	}

	private static void RequireEqual(
		string name,
		long expected,
		long actual)
	{
		if (expected != actual)
		{
			throw new InvalidOperationException(
				$"The scripted {name} were {actual}, expected " +
				$"{expected}.");
		}
	}

}
