using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the hierarchy filter belonging to the scene it was typed for.
/// </summary>
/// <remarks>
/// The project browser already drops its search when the project
/// changes. Without the same rule here, opening another scene while a
/// filter is active shows "No object matches" for a scene that has
/// objects, which reads as an empty scene.
/// </remarks>
public sealed class HierarchyFilterSceneSwitchTests : IDisposable
{
	private const string Main = "Scenes/Main.volt.scene.json";
	private const string Other = "Scenes/Other.volt.scene.json";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-hierarchy-filter-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public HierarchyFilterSceneSwitchTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "FilterSwitch");
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
	public void OpeningAnotherSceneClearsTheFilterTypedForThePreviousOne()
	{
		_session.SaveScene();
		Assert.NotNull(_session.CreateScene(Other, "Other"));
		Assert.NotNull(_session.CreateEntity("Lamp"));
		_session.SaveScene();

		Assert.True(_session.OpenScene(Main));

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

		Assert.True(_session.OpenScene(Other));
		Paint(0.2);

		Assert.Equal(string.Empty, search.Text);
		Assert.Contains(
			tree.Roots,
			root => root.Text == "Lamp");
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
