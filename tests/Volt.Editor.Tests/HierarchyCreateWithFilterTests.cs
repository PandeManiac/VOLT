using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers creating an object while the hierarchy filter is active.
/// </summary>
/// <remarks>
/// A filter typed for an earlier task hides a new object whose name does
/// not match it, so the create button appears to do nothing. The project
/// browser clears its search before creating a folder for the same
/// reason, and the hierarchy takes the same approach.
/// </remarks>
public sealed class HierarchyCreateWithFilterTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-create-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyCreateWithFilterTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "CreateWithFilter");
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
	public void CreatingAnObjectShowsItEvenWhenAFilterWasHidingIt()
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

		// The new object is named GameObject, which the filter hides.
		Assert.DoesNotContain(tree.Roots, root => root.Text == "GameObject");

		IconButton create = Descendants(panel)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Create an empty object");

		Click(create);
		Paint(0.2);

		Assert.Equal(string.Empty, search.Text);
		Assert.Contains(tree.Roots, root => root.Text == "GameObject");
		Assert.Contains(
			_session.Scene!.Entities,
			entity => entity.Name == "GameObject");
	}

	private void Click(Widget widget)
	{
		UiPoint center = widget.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
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
