using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the console's severity filters, which the author reads as a
/// view onto the log rather than a property of one panel instance.
/// </summary>
public sealed class ConsoleFilterTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-console-filter-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ConsoleFilterTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ConsoleFilter");
		_session.Log("An information message");
		_session.Log("A warning", EditorLogLevel.Warning);
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
	public void TogglingAFilterRecordsTheChoiceOnTheSession()
	{
		ConsolePanel console = Build();
		ToolButton info = Filter(console, "Show information messages");

		Assert.True(info.IsActive);

		Click(info);
		Frame(0.1);

		Assert.False(_session.ConsoleShowsInfo);
		Assert.False(info.IsActive);

		Click(info);
		Frame(0.2);

		Assert.True(_session.ConsoleShowsInfo);
		Assert.True(info.IsActive);
	}

	[Fact]
	public void AFilterChangedElsewhereReachesTheOpenPanel()
	{
		ConsolePanel console = Build();
		ToolButton info = Filter(console, "Show information messages");

		Assert.True(info.IsActive);

		// The preferences reset restores the severities on the session,
		// with no click on this panel at all.
		_session.ConsoleShowsInfo = false;
		Frame(0.1);

		Assert.False(info.IsActive);
	}

	[Fact]
	public void AFreshPanelDrawsItsFiltersFromTheSession()
	{
		_session.ConsoleShowsErrors = false;

		ConsolePanel console = Build();
		ToolButton errors = Filter(console, "Show errors");

		Assert.False(errors.IsActive);
		Assert.True(Filter(console, "Show warnings").IsActive);
	}

	private ConsolePanel Build()
	{
		ConsolePanel console = new(_session);

		_context.Add(console);
		Frame(0.0);

		return console;
	}

	private static ToolButton Filter(ConsolePanel console, string tooltip)
	{
		return Descendants(console)
			.OfType<ToolButton>()
			.Single(button => button.TooltipText == tooltip);
	}

	private void Click(Widget widget)
	{
		UiPoint center = widget.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
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
