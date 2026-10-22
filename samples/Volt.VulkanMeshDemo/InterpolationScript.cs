using System.Globalization;
using System.Numerics;
using Volt.Core.Timing;
using Volt.Rendering.Extraction;
using Volt.Rendering.Transforms;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Drives the deterministic interpolation and time-scale
/// demonstration from completed application frames.
/// </summary>
/// <remarks>
/// <para>
/// This is test-only orchestration. It injects exact elapsed
/// intervals after each completed frame so the next application frame
/// lands on a known accumulator fraction, and it changes the time
/// scale, pauses, single-steps, and resumes at fixed frame numbers.
/// The production interactive path uses the shared monotonic clock and
/// never reads a frame counter.
/// </para>
/// <para>
/// The script is driven by the loop's frame index rather than by
/// accepted presentations, because the checkpointed accumulator
/// fractions belong to the simulation, not to the swapchain. A
/// rejected present therefore cannot shift a checkpoint, and a
/// scripted run verifies exactly the states its arithmetic predicts.
/// </para>
/// <para>
/// Fractional checkpoints hold for several frames by injecting no
/// elapsed time. The rendered pose is therefore stable long enough
/// for a screenshot, and every held frame keeps the same alpha and
/// interpolated position.
/// </para>
/// </remarks>
internal sealed class InterpolationScript
{
	private const long UnitsPerStep = 1_000_000_000;

	private readonly InterpolationDemo _demo;
	private readonly int _frameLimit;
	private readonly bool _probe;

	private bool _paused;
	private bool _pausedPhase;
	private bool _stepped;
	private bool _resumed;
	private bool _halfSpeedLogged;
	private bool _doubleSpeedLogged;
	private bool _zeroStepLogged;
	private bool _multiStepLogged;

	private long _halfSpeedStartSteps;
	private long _doubleSpeedStartSteps;

	/// <summary>
	/// Creates a script for one bounded run.
	/// </summary>
	/// <param name="demo">The demonstration to drive.</param>
	/// <param name="frameLimit">
	/// Completed frames the run drives before verifying.
	/// </param>
	/// <param name="probe">
	/// <c>true</c> to hold the quarter-step checkpoint for the whole
	/// run instead of replaying the paused, scaled, and resumed
	/// phases.
	/// </param>
	public InterpolationScript(
		InterpolationDemo demo,
		int frameLimit,
		bool probe = false)
	{
		ArgumentNullException.ThrowIfNull(demo);

		if (frameLimit < InterpolationDemo.MinimumScriptedFrames)
		{
			throw new ArgumentOutOfRangeException(
				nameof(frameLimit),
				frameLimit,
				$"The interpolation script requires at least " +
				$"{InterpolationDemo.MinimumScriptedFrames} accepted " +
				$"frames.");
		}

		_demo = demo;
		_frameLimit = frameLimit;
		_probe = probe;
	}

	/// <summary>
	/// Advances the script by one completed application frame.
	/// </summary>
	/// <param name="frame">
	/// The one-based index of the frame that just completed.
	/// </param>
	public void Advance(int frame)
	{
		if (_probe)
		{
			AdvanceProbe(frame);
			return;
		}

		VerifyCompletedFrame(frame);
		InjectForNextFrame(frame);

		if (frame == _frameLimit)
		{
			VerifyFinal();
		}
	}

	/// <summary>
	/// Holds the quarter-step accumulator state for a screenshot run.
	/// </summary>
	/// <param name="frame">The completed frame's index.</param>
	/// <remarks>
	/// The first 120 frames inject exactly one 60 Hz interval each and
	/// then land on an unspent quarter step, so frame 121 renders the
	/// interpolated pose at alpha <c>0.25</c>. No further time is
	/// injected, so the accumulator, the history, and the displayed
	/// pose stay identical for every later frame. That makes the run
	/// safe to capture long after the checkpoint line appears: a
	/// screenshot cannot accidentally show a different pose.
	/// </remarks>
	private void AdvanceProbe(int frame)
	{
		if (frame <= 120)
		{
			InjectForNextFrame(frame);
			return;
		}

		FrameTiming timing = _demo.Loop.LastFrame;

		// Only the checkpoint frame completes a step; every later
		// frame holds the same accumulator state.
		RequireAlpha(timing, 0.25f, frame, steps: frame == 121 ? 1 : 0);
		RequireSteps(121, frame);
		RequireScale(1.0f, frame);

		if (frame == 121)
		{
			WriteAlphaCheckpoint(frame, timing, "0.250000");
		}

		if (frame == _frameLimit)
		{
			VerifyHeldProbe();
			_demo.WriteSummary("Interpolation probe final");
		}
	}

	private void VerifyHeldProbe()
	{
		if (!_demo.History.TryGetPose(
			_demo.MovingEntity,
			out TransformPose previous,
			out TransformPose current))
		{
			throw new InvalidOperationException(
				"The probe run lost the moving entity's history.");
		}

		float expectedPrevious = _demo.MotionX(120);
		float expectedCurrent = _demo.MotionX(121);

		if (Math.Abs(previous.Position.X - expectedPrevious) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The probe's previous pose is " +
				$"{Format(previous.Position.X)}, expected " +
				$"{Format(expectedPrevious)}.");
		}

		if (Math.Abs(current.Position.X - expectedCurrent) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The probe's current pose is " +
				$"{Format(current.Position.X)}, expected " +
				$"{Format(expectedCurrent)}.");
		}

		if (Math.Abs(
			_demo.Scene.GetPosition(_demo.MovingEntity).X -
			expectedCurrent) > 0.0001f)
		{
			throw new InvalidOperationException(
				"The probe run modified the authoritative ECS " +
				"position.");
		}

		InterpolatedRenderExtractor extractor =
			new(_demo.Scene.World);

		RenderInstance[] instances = new RenderInstance[8];

		RenderExtractionResult result = extractor.Extract(
			instances,
			_demo.History,
			0.25f);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				"The probe extraction did not fit its buffer.");
		}

		float expected =
			(expectedPrevious + ((expectedCurrent - expectedPrevious) * 0.25f));

		for (int index = 0; index < result.WrittenCount; index++)
		{
			if (instances[index].Entity != _demo.MovingEntity)
			{
				continue;
			}

			if (Math.Abs(instances[index].Model.M41 - expected) > 0.0001f)
			{
				throw new InvalidOperationException(
					$"The probe's interpolated model is at x=" +
					$"{Format(instances[index].Model.M41)}, expected " +
					$"{Format(expected)}.");
			}

			return;
		}

		throw new InvalidOperationException(
			"The probe extraction did not include the moving entity.");
	}

	private void VerifyCompletedFrame(int frame)
	{
		FrameTiming timing = _demo.Loop.LastFrame;

		switch (frame)
		{
			case 121:
				RequireAlpha(timing, 0.25f, frame, steps: 1);
				WriteAlphaCheckpoint(frame, timing, "0.250000");
				break;

			case 132:
				RequireAlpha(timing, 0.5f, frame, steps: 1);
				WriteAlphaCheckpoint(frame, timing, "0.500000");
				break;

			case 143:
				RequireAlpha(timing, 0.75f, frame, steps: 1);
				WriteAlphaCheckpoint(frame, timing, "0.750000");
				break;

			case 154:
				RequireAlpha(timing, 0.999999f, frame, steps: 1);
				WriteAlphaCheckpoint(frame, timing, "0.999999");
				break;

			case 166:
				RequireAlpha(timing, 0.5f, frame, steps: 1);
				break;

			case 167:
			case 168:
				{
					float expected = frame == 167 ? 0.7f : 0.9f;

					RequireAlpha(timing, expected, frame, steps: 0);

					if (!_zeroStepLogged)
					{
						_zeroStepLogged = true;

						WriteState(
							"Interpolation zero-step checkpoint",
							frame,
							timing);
					}

					break;
				}

			case 169:
				RequireAlpha(timing, 0.5f, frame, steps: 3);

				if (!_multiStepLogged)
				{
					_multiStepLogged = true;

					WriteState(
						"Interpolation multi-step checkpoint",
						frame,
						timing);
				}

				break;

			case 181:
				RequireScale(1.0f, frame);
				_halfSpeedStartSteps = _demo.Loop.TotalFixedSteps;
				break;

			case 271:
				RequireScale(0.5f, frame);
				RequireSteps(_halfSpeedStartSteps + 45, frame);
				_doubleSpeedStartSteps = _demo.Loop.TotalFixedSteps;

				if (!_halfSpeedLogged)
				{
					_halfSpeedLogged = true;

					WriteState("Interpolation half speed", frame, timing);
				}

				break;

			case 272:
				RequireScale(2.0f, frame);
				break;

			case 331:
				RequireScale(2.0f, frame);
				RequireSteps(_doubleSpeedStartSteps + 120, frame);

				if (!_doubleSpeedLogged)
				{
					_doubleSpeedLogged = true;

					WriteState(
						"Interpolation double speed",
						frame,
						timing);
				}

				break;

			case 332:
				RequirePaused(frame);
				break;

			case 352:
				RequirePaused(frame);
				RequireSteps(294, frame);
				break;

			case 362:
				if (_demo.Loop.IsPaused)
				{
					throw new InvalidOperationException(
						$"Frame {frame} was expected after resume.");
				}

				RequireSteps(294, frame);
				break;

			case 363:
				RequireSteps(295, frame);
				RequireAlpha(timing, 0.0f, frame, steps: 1);
				WriteState("Interpolation resumed", frame, timing);
				break;
		}

		if (_paused && !_demo.Loop.IsPaused)
		{
			throw new InvalidOperationException(
				$"Frame {frame} expected the loop to stay paused.");
		}
	}

	private void InjectForNextFrame(int frame)
	{
		// The steady one-step frames and the scaled phases inject one
		// 60 Hz frame of elapsed time.
		if (frame is >= 1 and <= 119 ||
			frame is >= 182 and <= 270 ||
			frame is >= 272 and <= 330 ||
			frame >= 363 && frame < _frameLimit)
		{
			InjectFrame();
			return;
		}

		switch (frame)
		{
			case 120:
				Land(0.25f, steps: 1);
				break;

			case 131:
				Land(0.5f, steps: 1);
				break;

			case 142:
				Land(0.75f, steps: 1);
				break;

			case 153:
				Land(0.999999f, steps: 1);
				break;

			case 165:
				Land(0.5f, steps: 1);
				break;

			case 166:
			case 167:
				InjectUnits(0.2f);
				break;

			case 168:
				Land(0.5f, steps: 3);
				break;

			case 181:
				_demo.SetTimeScale(0.5f);
				InjectFrame();
				break;

			case 271:
				_demo.SetTimeScale(2.0f);
				InjectFrame();
				break;

			case 331:
				_demo.SetTimeScale(1.0f);
				_demo.Pause();
				_paused = true;
				_pausedPhase = true;
				break;

			case 351:
				_stepped = _demo.StepOnce();
				break;

			case 361:
				_demo.Resume();
				_paused = false;
				_resumed = true;
				break;

			case 362:
				InjectFrame();
				break;

			default:
				// A checkpoint hold: the accumulator does not move.
				break;
		}
	}

	private void Land(float alpha, int steps)
	{
		long current = _demo.AccumulatedUnits;
		long target = (long)Math.Round(alpha * UnitsPerStep);
		long delta = (steps * UnitsPerStep) + target - current;

		if (delta < 0)
		{
			delta += UnitsPerStep;
		}

		_demo.AdvanceScriptedNanoseconds(
			delta / InterpolationDemo.FixedUpdatesPerSecond);
	}

	private void InjectUnits(float fraction)
	{
		long units = (long)Math.Round(fraction * UnitsPerStep);

		_demo.AdvanceScriptedNanoseconds(
			units / InterpolationDemo.FixedUpdatesPerSecond);
	}

	private void InjectFrame()
	{
		_demo.AdvanceScriptedNanoseconds(ScriptedFrameClock.FrameTicks);
	}

	private void VerifyFinal()
	{
		if (!_pausedPhase || !_stepped || !_resumed)
		{
			throw new InvalidOperationException(
				"The interpolation script did not pause, single-step, " +
				"and resume before its final verification.");
		}

		long expectedSteps =
			532 + (_frameLimit - InterpolationDemo.MinimumScriptedFrames);

		RequireSteps(expectedSteps, _frameLimit);
		RequireScale(1.0f, _frameLimit);

		if (_demo.Loop.IsPaused)
		{
			throw new InvalidOperationException(
				"The interpolation script ended while paused.");
		}

		if (_demo.Loop.TotalDroppedTime != TimeSpan.Zero ||
			_demo.Loop.TotalDroppedSimulationTime != TimeSpan.Zero)
		{
			throw new InvalidOperationException(
				$"The interpolation script dropped " +
				$"{_demo.Loop.TotalDroppedTime.TotalMilliseconds:F3} ms " +
				$"of real time and " +
				$"{_demo.Loop.TotalDroppedSimulationTime.TotalMilliseconds:F3} " +
				$"ms of simulation time.");
		}

		if (_demo.CapturedSteps != expectedSteps)
		{
			throw new InvalidOperationException(
				$"The interpolation history captured " +
				$"{_demo.CapturedSteps} steps, expected " +
				$"{expectedSteps}.");
		}

		long expectedRotations =
			expectedSteps / (int)(InterpolationDemo.RotationInterval
				.TotalSeconds *
				InterpolationDemo.FixedUpdatesPerSecond);

		if (_demo.ScheduledRotations != expectedRotations)
		{
			throw new InvalidOperationException(
				$"The interpolation script ran " +
				$"{_demo.ScheduledRotations} scheduled rotations, " +
				$"expected {expectedRotations}.");
		}

		if (_demo.History.Count != 4)
		{
			throw new InvalidOperationException(
				$"The interpolation history holds " +
				$"{_demo.History.Count} entities, expected 4.");
		}

		RequireCommittedHistory(expectedSteps);

		_demo.WriteSummary("Interpolation demo final");
	}

	private void RequireCommittedHistory(long totalSteps)
	{
		if (!_demo.History.TryGetPose(
			_demo.MovingEntity,
			out TransformPose previous,
			out TransformPose current))
		{
			throw new InvalidOperationException(
				"The moving entity has no transform history.");
		}

		Vector3 component =
			_demo.Scene.GetPosition(_demo.MovingEntity);

		float expectedCurrent = _demo.MotionX(totalSteps);
		float expectedPrevious = _demo.MotionX(totalSteps - 1);

		if (Math.Abs(current.Position.X - expectedCurrent) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The captured current pose is " +
				$"{current.Position.X:F4}, expected " +
				$"{expectedCurrent:F4}.");
		}

		if (Math.Abs(previous.Position.X - expectedPrevious) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The captured previous pose is " +
				$"{previous.Position.X:F4}, expected " +
				$"{expectedPrevious:F4}.");
		}

		if (Math.Abs(component.X - expectedCurrent) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The authoritative component is " +
				$"{component.X:F4}, expected " +
				$"{expectedCurrent:F4}; interpolation must not " +
				$"write to the ECS.");
		}

		InterpolatedRenderExtractor extractor =
			new(_demo.Scene.World);

		RenderInstance[] instances = new RenderInstance[8];

		RenderExtractionResult result = extractor.Extract(
			instances,
			_demo.History,
			0.5f);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				"The final interpolated extraction did not fit its " +
				"buffer.");
		}

		RenderInstance moving = default;
		bool found = false;

		for (int index = 0; index < result.WrittenCount; index++)
		{
			if (instances[index].Entity == _demo.MovingEntity)
			{
				moving = instances[index];
				found = true;
				break;
			}
		}

		if (!found)
		{
			throw new InvalidOperationException(
				"The moving entity was not extracted.");
		}

		float midpoint =
			(previous.Position.X + current.Position.X) * 0.5f;

		if (Math.Abs(moving.Model.M41 - midpoint) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"The interpolated model is at x=" +
				$"{moving.Model.M41:F4}, expected the midpoint " +
				$"{midpoint:F4}.");
		}

		if (Math.Abs(
			_demo.Scene.GetPosition(_demo.MovingEntity).X -
			component.X) > 0.0001f)
		{
			throw new InvalidOperationException(
				"Interpolated extraction modified the ECS position.");
		}
	}

	private void WriteAlphaCheckpoint(
		int frame,
		in FrameTiming timing,
		string label)
	{
		_demo.History.TryGetPose(
			_demo.MovingEntity,
			out TransformPose previous,
			out TransformPose current);

		float interpolated = Vector3.Lerp(
			previous.Position,
			current.Position,
			timing.InterpolationAlpha).X;

		Console.WriteLine(
			$"Interpolation checkpoint alpha={label} " +
			$"frame={frame} step={timing.TotalFixedSteps} " +
			$"previous.x={Format(previous.Position.X)} " +
			$"current.x={Format(current.Position.X)} " +
			$"interpolated.x={Format(interpolated)}.");
	}

	private void WriteState(string label, int frame, in FrameTiming timing)
	{
		Console.WriteLine(
			$"{label}: frame={frame} " +
			$"steps={timing.FixedSteps} " +
			$"totalSteps={timing.TotalFixedSteps} " +
			$"alpha={Format(timing.InterpolationAlpha)} " +
			$"scale={Format(timing.TimeScale.Value)} " +
			$"paused={timing.IsPaused}.");
	}

	private void RequireAlpha(
		in FrameTiming timing,
		float expected,
		int frame,
		int steps)
	{
		if (timing.FixedSteps != steps)
		{
			throw new InvalidOperationException(
				$"Frame {frame} executed {timing.FixedSteps} fixed " +
				$"steps, expected {steps}.");
		}

		if (Math.Abs(timing.InterpolationAlpha - expected) > 0.000001f)
		{
			throw new InvalidOperationException(
				$"Frame {frame} reported alpha " +
				$"{Format(timing.InterpolationAlpha)}, expected " +
				$"{Format(expected)}.");
		}
	}

	private void RequireSteps(long expected, int frame)
	{
		long actual = _demo.Loop.TotalFixedSteps;

		if (actual != expected)
		{
			throw new InvalidOperationException(
				$"Frame {frame} had executed {actual} fixed steps, " +
				$"expected {expected}.");
		}
	}

	private void RequireScale(float expected, int frame)
	{
		float actual = _demo.Loop.EffectiveTimeScale.Value;

		if (Math.Abs(actual - expected) > 0.0001f)
		{
			throw new InvalidOperationException(
				$"Frame {frame} used scale {Format(actual)}, " +
				$"expected {Format(expected)}.");
		}
	}

	private void RequirePaused(int frame)
	{
		if (!_demo.Loop.IsPaused)
		{
			throw new InvalidOperationException(
				$"Frame {frame} expected the loop to be paused.");
		}
	}

	private static string Format(float value)
	{
		return value.ToString("F6", CultureInfo.InvariantCulture);
	}
}
