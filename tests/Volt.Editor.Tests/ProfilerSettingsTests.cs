using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the profiler following settings it reports on.
/// </summary>
/// <remarks>
/// Grid snapping and the target frame rate are settings rather than
/// scene state, so a panel that only listens for scene changes keeps
/// showing the previous values after the toolbar toggles snapping.
/// </remarks>
public sealed class ProfilerSettingsTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-profiler-settings-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ProfilerSettingsTests()
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
	public void TogglingGridSnapUpdatesThePanelWithoutASceneChange()
	{
		using ProfilerPanel profiler = new(_session);

		_context.Add(profiler);
		Paint(0.0);

		Label history = HistoryLabel(profiler);

		Assert.Contains("Grid snap: off", history.Text, StringComparison.Ordinal);

		// The toolbar's toggle changes only a setting, so nothing else
		// in the panel's event list fires.
		_session.GridSnapping = true;

		Assert.Contains(
			$"Grid snap: {_session.SnapStep:0.###}",
			history.Text,
			StringComparison.Ordinal);

		_session.GridSnapping = false;

		Assert.Contains("Grid snap: off", history.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void ChangingTheTargetFrameRateUpdatesTheGraphBudget()
	{
		using ProfilerPanel profiler = new(_session);

		_context.Add(profiler);
		Paint(0.0);

		TimelineGraphView frames = Descendants(profiler)
			.OfType<TimelineGraphView>()
			.First();

		Assert.Equal(
			_session.TargetFrameMilliseconds,
			frames.BudgetMilliseconds);

		// A budget change is a setting too, so the reference line the
		// graph draws must move with it before any frame is recorded.
		_session.TargetFrameRate = 30;

		Assert.Equal(1000.0 / 30.0, _session.TargetFrameMilliseconds, 6);
		Assert.Equal(
			_session.TargetFrameMilliseconds,
			frames.BudgetMilliseconds);
	}

	private static Label HistoryLabel(Widget root)
	{
		return Descendants(root)
			.OfType<Label>()
			.First(label => label.Text.StartsWith("Undo:", StringComparison.Ordinal));
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(520f, 800f), UiPoint.Zero);
		_context.Draw();
	}

	private static IEnumerable<Widget> Descendants(Widget root)
	{
		foreach (Widget child in root.Children)
		{
			yield return child;

			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}
}
