using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Layout;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers when the profiler panel samples the session.
/// </summary>
/// <remarks>
/// The panel formats labels, samples the GC, and recaptures both graphs on
/// every refresh, so it samples ten times a second rather than on every
/// frame. A refresh driven by the simulation would both cost frame time and
/// inflate the allocation figure the panel exists to report.
/// </remarks>
public sealed class ProfilerRefreshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-profiler-refresh-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ProfilerRefreshTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Profiler");
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void RuntimeUpdates_WaitForTheSampleTimer()
	{
		_session.StartPlay();

		using ProfilerPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		int samples = panel.RefreshCount;

		// A fixed step raises a runtime update, as a playing frame does.
		// Sampling on it would rebuild the labels and graphs at frame rate.
		_session.Update(1.0);

		Assert.Equal(samples, panel.RefreshCount);

		panel.Tick(0.05, 1f);

		Assert.Equal(samples, panel.RefreshCount);

		panel.Tick(0.06, 1f);

		Assert.Equal(samples + 1, panel.RefreshCount);
	}

	[Fact]
	public void APlayStateChange_SamplesImmediately()
	{
		_session.StartPlay();

		using ProfilerPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		int samples = panel.RefreshCount;

		// Starting and stopping is a user action, so the panel shows the
		// new state at once instead of waiting for the timer.
		_session.Stop();

		Assert.Equal(samples + 1, panel.RefreshCount);
	}
}
