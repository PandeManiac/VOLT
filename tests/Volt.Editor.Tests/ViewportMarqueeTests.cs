using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers box selection in the scene view: the drag gesture, the
/// selection it produces, the rectangle it paints, and the fact that
/// the hierarchy and inspector report the result.
/// </summary>
public sealed class ViewportMarqueeTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-viewport-marquee-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ViewportMarqueeTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Marquee");
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
	public void Marquee_SelectsTheObjectsInsideIt()
	{
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;
		SceneEntity cube = SceneWithCube();

		Drag(
			new UiPoint(bounds.X + 3f, bounds.Y + 3f),
			new UiPoint(bounds.Right - 3f, bounds.Bottom - 3f));

		Assert.Contains(cube.Id, _session.Selection);
		Assert.True(
			_session.Selection.Count >= 2,
			"The scene's other objects are inside the box too.");

		Assert.NotNull(_session.ActiveEntityId);
		Assert.Contains(_session.ActiveEntityId!.Value, _session.Selection);
	}

	[Fact]
	public void Marquee_LeavesOutWhatItDoesNotCover()
	{
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;
		SceneEntity cube = SceneWithCube();

		// The bottom-left corner of the view is empty: the camera frames
		// the scene origin in the middle.
		Drag(
			new UiPoint(bounds.X + 4f, bounds.Bottom - 6f),
			new UiPoint(bounds.X + 60f, bounds.Bottom - 40f));

		Assert.Empty(_session.Selection);
		Assert.DoesNotContain(cube.Id, _session.Selection);
	}

	[Fact]
	public void Marquee_ShiftExtendsTheSelection()
	{
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;
		SceneEntity cube = SceneWithCube();

		_session.Select(cube.Id);

		Drag(
			new UiPoint(bounds.X + 3f, bounds.Y + 3f),
			new UiPoint(bounds.Right - 3f, bounds.Bottom - 3f),
			UiKeyModifiers.Shift);

		Assert.Contains(cube.Id, _session.Selection);
		Assert.True(
			_session.Selection.Count >= 2,
			"A shift drag adds to the selection instead of replacing it.");
	}

	[Fact]
	public void Marquee_ClickWithoutMotionClearsTheSelection()
	{
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;
		SceneEntity cube = SceneWithCube();

		_session.Select(cube.Id);

		UiPoint empty = new(bounds.X + 30f, bounds.Bottom - 20f);

		_context.PointerDown(UiMouseButton.Left, empty);
		_context.PointerUp(UiMouseButton.Left, empty);

		Assert.Empty(_session.Selection);
	}

	[Fact]
	public void Marquee_PaintsTheBoxWhileDragging()
	{
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;

		UiPoint from = new(bounds.X + 120f, bounds.Y + 140f);
		UiPoint to = new(bounds.X + 420f, bounds.Y + 380f);

		_context.PointerDown(UiMouseButton.Left, from);
		_context.PointerMove(to, new UiPoint(to.X - from.X, to.Y - from.Y));

		UiDrawList drawList = _context.Draw();
		UiRect expected = UiRect.FromLtrb(from.X, from.Y, to.X, to.Y);

		bool fill = false;
		bool outline = false;

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Rect.Width < expected.Width - 1.5f ||
				command.Rect.Height < expected.Height - 1.5f)
			{
				continue;
			}

			if (!expected.Contains(command.Rect))
			{
				continue;
			}

			fill |= command.Kind == UiDrawCommandKind.Rectangle;
			outline |= command.Kind == UiDrawCommandKind.Border;
		}

		Assert.True(fill, "The box must be filled while it is dragged.");
		Assert.True(outline, "The box must be outlined while it is dragged.");

		_context.PointerUp(UiMouseButton.Left, to);

		drawList = _context.Draw();

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			Assert.False(
				command.Kind == UiDrawCommandKind.Border &&
					expected.Contains(command.Rect) &&
					command.Rect.Width >= expected.Width - 1.5f,
				"The box must disappear once the pointer is released.");
		}
	}

	[Fact]
	public void Viewport_ClickRestoresTheToolHotkeys()
	{
		// A press in the view stands the tool hotkeys down so the
		// camera keys can be used; without a release that clears the
		// flag, the hotkeys would never work again.
		SceneViewport viewport = BuildViewport();
		UiRect bounds = viewport.GlobalVisualBounds;
		UiPoint empty = new(bounds.Center.X, bounds.Bottom - 20f);

		_context.PointerDown(UiMouseButton.Left, empty);

		Assert.True(_session.ViewportNavigating);

		_context.PointerUp(UiMouseButton.Left, empty);

		Assert.False(_session.ViewportNavigating);
	}

	[Fact]
	public void Selection_IsReflectedInTheHierarchyAndInspector()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		SceneDocument scene = _session.Scene!;
		Guid[] ids = [.. scene.Entities.Take(2).Select(entity => entity.Id)];

		Assert.Equal(2, ids.Length);

		_session.SelectMany(ids, ids[^1]);

		_context.BeginFrame(
			0.1,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		HierarchyPanel hierarchy = Descendants(shell)
			.OfType<HierarchyPanel>()
			.Single();

		TreeView tree = Descendants(hierarchy).OfType<TreeView>().Single();

		Assert.Equal(ids.Length, tree.SelectedItems.Count);

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();

		Assert.Contains(
			Descendants(inspector).OfType<Label>(),
			label => label.Text.Contains("selected", StringComparison.Ordinal));
	}

	private SceneEntity SceneWithCube()
	{
		SceneDocument scene = _session.Scene!;

		return scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);
	}

	private SceneViewport BuildViewport()
	{
		SceneViewport viewport = new(_session);

		_context.Add(viewport);
		_context.BeginFrame(
			0.0,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		_context.Draw();

		return viewport;
	}

	private void Drag(
		UiPoint from,
		UiPoint to,
		UiKeyModifiers modifiers = UiKeyModifiers.None)
	{
		_context.PointerDown(UiMouseButton.Left, from, modifiers);

		_context.PointerMove(
			new UiPoint((from.X + to.X) * 0.5f, (from.Y + to.Y) * 0.5f),
			new UiPoint((to.X - from.X) * 0.5f, (to.Y - from.Y) * 0.5f),
			modifiers);

		_context.PointerMove(
			to,
			new UiPoint(to.X - from.X, to.Y - from.Y),
			modifiers);

		_context.PointerUp(UiMouseButton.Left, to, modifiers);
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
