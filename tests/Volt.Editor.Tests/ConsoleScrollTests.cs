using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the console following the newest message without taking the
/// view away from an author who scrolled back to read an earlier one.
/// </summary>
public sealed class ConsoleScrollTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-console-scroll-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ConsoleScrollTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ConsoleScroll");

		for (int index = 0; index < 80; index++)
		{
			_session.Log($"Diagnostic message {index:00}");
		}
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
	public void ConsoleOpensAtTheNewestMessage()
	{
		ScrollView scroll = BuildConsole();

		Assert.True(scroll.MaximumScroll > 0f, "The log must overflow.");

		Assert.Equal(
			scroll.MaximumScroll,
			scroll.ScrollOffset.Y,
			1);
	}

	[Fact]
	public void NewMessageDoesNotTakeTheViewFromAnEarlierMessage()
	{
		ScrollView scroll = BuildConsole();

		_context.Wheel(new UiPoint(0f, 3f), scroll.GlobalContentBounds.Center);
		Frame(0.1);

		float scrolledUp = scroll.ScrollOffset.Y;

		Assert.True(
			scrolledUp < scroll.MaximumScroll,
			"The test must scroll away from the newest message.");

		_session.Log("A new diagnostic message");
		Frame(0.2);

		Assert.Equal(scrolledUp, scroll.ScrollOffset.Y, 1);
	}

	[Fact]
	public void ConsoleFollowsNewMessagesWhenItIsAlreadyAtTheEnd()
	{
		ScrollView scroll = BuildConsole();

		scroll.SetScrollOffset(scroll.MaximumScroll);
		Frame(0.1);

		_session.Log("A new diagnostic message");
		Frame(0.2);

		Assert.True(
			scroll.ScrollOffset.Y >= scroll.MaximumScroll - 1f,
			"A console that was already at the end must follow the tail.");
	}

	private ScrollView BuildConsole()
	{
		ConsolePanel console = new(_session);

		_context.Add(console);
		Frame(0.0);

		return Descendants(console).OfType<ScrollView>().Single();
	}

	private void Frame(double time)
	{
		_context.BeginFrame(
			time,
			new UiSize(800f, 300f),
			UiPoint.Zero);

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
