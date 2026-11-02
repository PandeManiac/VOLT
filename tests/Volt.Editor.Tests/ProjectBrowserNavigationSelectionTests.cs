using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the project browser's selection when the author browses to
/// another folder.
/// </summary>
/// <remarks>
/// An asset selected in one folder is not visible in another, so keeping
/// it selected leaves the details panel describing a file that is not on
/// screen and the delete and rename commands aimed at it.
/// </remarks>
public sealed class ProjectBrowserNavigationSelectionTests : IDisposable
{
	private const string Main = "Scenes/Main.volt.scene.json";

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-nav-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ProjectBrowserNavigationSelectionTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Navigation");
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
	public void GoingUpEndsTheSelectionOfAnAssetInTheFolderLeftBehind()
	{
		using ProjectPanel panel = new(_session);

		_context.Add(panel);
		Paint(0.0);

		TreeView folders = Descendants(panel)
			.OfType<TreeView>()
			.Single(tree => tree.Roots.Any(
				root => root.Tag is string tag && tag.Length == 0));

		TreeViewItem scenes = folders.Roots[0]
			.Children
			.Single(item => item.Text == "Scenes");

		folders.SetSelection([scenes]);
		Paint(0.1);

		IconTileView main = Descendants(panel)
			.OfType<IconTileView>()
			.Single(tile => tile.Text == "Main.volt.scene.json");

		Click(main);
		Paint(0.2);

		Assert.Equal(Main, panel.CaptureState().SelectedId);

		IconButton up = Descendants(panel)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Go to the parent folder");

		Click(up);
		Paint(0.3);

		ProjectPanelState state = panel.CaptureState();

		Assert.Equal(string.Empty, state.Folder);
		Assert.Equal(string.Empty, state.SelectedId);
		Assert.Empty(state.SelectedAssetIds);
	}

	private void Click(Widget widget)
	{
		UiPoint center = widget.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(900f, 600f), UiPoint.Zero);
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
