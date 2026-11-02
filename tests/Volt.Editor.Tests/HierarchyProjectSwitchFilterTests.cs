using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the hierarchy filter not following the author into another
/// project.
/// </summary>
/// <remarks>
/// Both projects start from a scene at the same relative path, so a
/// scene-path comparison on its own cannot tell the two apart. A filter
/// typed in the first project must not survive into the second, where it
/// would hide objects the author has never seen.
/// </remarks>
public sealed class HierarchyProjectSwitchFilterTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-project-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyProjectSwitchFilterTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "First"), "First");
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
	public void OpeningAnotherProjectClearsTheFilter()
	{
		using HierarchyPanel panel = new(_session);

		_context.Add(panel);
		Paint(0.0);

		TextField search = Descendants(panel)
			.OfType<TextField>()
			.First();

		TreeView tree = Descendants(panel)
			.OfType<TreeView>()
			.First();

		Assert.True(search.Focus());
		Assert.True(_context.TextInput("Cube"));
		Paint(0.1);

		Assert.Equal("Cube", search.Text);

		// The second project opens its own scene at the same path the
		// first one used.
		_session.NewProject(Path.Combine(_root, "Second"), "Second");
		Paint(0.2);

		Assert.NotNull(_session.Scene);
		Assert.Equal(string.Empty, search.Text);
		Assert.NotEmpty(tree.Roots);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(400f, 500f), UiPoint.Zero);
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
