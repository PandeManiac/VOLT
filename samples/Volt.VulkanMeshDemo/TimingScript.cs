using System.Globalization;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Drives the deterministic timing demonstration from accepted
/// presentations.
/// </summary>
/// <remarks>
/// <para>
/// This is test-only orchestration. It injects one frame of fake
/// elapsed time after each accepted presentation, pauses the loop at a
/// fixed accepted frame, requests exactly one step, resumes, and
/// verifies the outcome at the end. The production interactive path
/// uses the shared monotonic clock and never reads the presentation
/// count.
/// </para>
/// <para>
/// Advancing only on accepted presentation keeps a dropped frame or a
/// swapchain recreation from adding or skipping simulation steps, so
/// the scripted result is independent of presentation speed.
/// </para>
/// </remarks>
internal sealed class TimingScript
{
	private readonly TimingDemo _demo;
	private readonly int _frameLimit;

	private bool _paused;
	private bool _stepped;
	private bool _resumed;

	/// <summary>
	/// Creates a script for one bounded run.
	/// </summary>
	/// <param name="demo">The demonstration to drive.</param>
	/// <param name="frameLimit">
	/// Accepted frames the run presents before verifying.
	/// </param>
	public TimingScript(TimingDemo demo, int frameLimit)
	{
		ArgumentNullException.ThrowIfNull(demo);

		if (frameLimit < TimingDemo.MinimumScriptedFrames)
		{
			throw new ArgumentOutOfRangeException(
				nameof(frameLimit),
				frameLimit,
				$"The timing script requires at least " +
				$"{TimingDemo.MinimumScriptedFrames} accepted frames.");
		}

		_demo = demo;
		_frameLimit = frameLimit;
	}

	/// <summary>
	/// Advances the script by one accepted presentation.
	/// </summary>
	/// <param name="presentedFrames">
	/// The one-based number of accepted frames presented so far.
	/// </param>
	public void Advance(int presentedFrames)
	{
		if (presentedFrames == TimingDemo.PauseFrame)
		{
			_demo.Pause();
			_paused = true;

			Console.WriteLine(
				$"Timing demo paused at accepted frame " +
				$"{presentedFrames}: step " +
				$"{_demo.Loop.TotalFixedSteps}, alpha=" +
				$"{Format(_demo.Loop.InterpolationAlpha)}.");
		}
		else if (presentedFrames == TimingDemo.SingleStepFrame)
		{
			_stepped = _demo.StepOnce();

			Console.WriteLine(
				$"Timing demo single step requested at accepted frame " +
				$"{presentedFrames}: pending=" +
				$"{_demo.Loop.IsSingleStepPending}, step " +
				$"{_demo.Loop.TotalFixedSteps}.");
		}
		else if (presentedFrames == TimingDemo.ResumeFrame)
		{
			_demo.Resume();
			_resumed = true;

			Console.WriteLine(
				$"Timing demo resumed at accepted frame " +
				$"{presentedFrames}: step " +
				$"{_demo.Loop.TotalFixedSteps}, alpha=" +
				$"{Format(_demo.Loop.InterpolationAlpha)}.");
		}

		// The injected frame is consumed by the next Advance. Doing
		// this after resume means the first frame after resume
		// executes its step, and doing it at the frame limit leaves
		// one unconsumed frame, exactly as documented.
		_demo.AdvanceScriptedFrame();

		if (presentedFrames != _frameLimit)
		{
			return;
		}

		if (!_paused || !_stepped || !_resumed)
		{
			throw new InvalidOperationException(
				"The timing script did not pause, single-step, and " +
				"resume before its final verification.");
		}

		_demo.VerifyScriptedOutcome(presentedFrames);
		_demo.WriteSummary("Timing demo final");
	}

	private static string Format(float value)
	{
		return value.ToString("F6", CultureInfo.InvariantCulture);
	}
}
