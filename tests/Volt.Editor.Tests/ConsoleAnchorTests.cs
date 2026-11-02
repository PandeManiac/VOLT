using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the console placing new entries by arrival order rather than
/// by searching the retained log.
/// </summary>
public sealed class ConsoleAnchorTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-console-anchor-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ConsoleAnchorTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ConsoleAnchor");
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
	public void AMessageAfterHiddenOnesStillAppears()
	{
		// A hidden severity must still advance the point the panel has
		// rendered to. Otherwise the information message that follows a
		// warning would be added only when something else forced a full
		// rebuild, which reads as a console that dropped a message.
		_session.ConsoleShowsWarnings = false;

		for (int index = 0; index < 20; index++)
		{
			_session.Log($"Warm-up {index:00}");
		}

		using ConsolePanel console = new(_session);

		_context.Add(console);
		Paint(0.0);

		int infoBefore = _session.LogInfoCount;

		_session.Log("Hidden warning", EditorLogLevel.Warning);
		Paint(0.1);

		_session.Log("Visible after the warning");
		Paint(0.2);

		Assert.Contains(
			Descendants(console).OfType<Label>(),
			label => label.Text == "Visible after the warning");

		Assert.Equal(infoBefore + 1, _session.LogInfoCount);
		Assert.Equal(1, _session.LogWarningCount);
	}

	[Fact]
	public void RetiringTheOldestEntryDropsOnlyItsRow()
	{
		// A full log retires one entry per message, so the panel has to
		// drop exactly one row and keep the rest in place.
		for (int index = 0; index < 500; index++)
		{
			_session.Log($"Message {index:000}");
		}

		using ConsolePanel console = new(_session);

		_context.Add(console);
		Paint(0.0);

		_session.Log("Newest message");
		Paint(0.1);

		List<string> messages = [.. Descendants(console)
			.OfType<Label>()
			.Select(label => label.Text)
			.Where(text => text.StartsWith("Message", StringComparison.Ordinal))];

		Assert.DoesNotContain("Message 000", messages);
		Assert.Contains("Message 001", messages);
		Assert.Contains("Message 499", messages);
		Assert.Contains(
			Descendants(console).OfType<Label>(),
			label => label.Text == "Newest message");
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(900f, 400f), UiPoint.Zero);
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
