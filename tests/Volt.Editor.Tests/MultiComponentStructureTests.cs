using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the inspector's component row and add bar following the
/// selection, the way its component fields already do.
/// </summary>
public sealed class MultiComponentStructureTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-multi-structure-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public MultiComponentStructureTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "MultiStructure");
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
	public void RemovingAComponentTakesItFromEverySelectedObject()
	{
		SceneEntity first = _session.CreateDirectionalLight("First")!;
		SceneEntity second = _session.CreateDirectionalLight("Second")!;
		SceneEntity plain = _session.CreateEntity("Plain")!;

		_session.SelectMany([first.Id, second.Id, plain.Id], first.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		int history = _session.Undo.UndoCount;

		IconButton remove = Descendants(inspector)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Remove Directional Light");

		Click(remove);
		Paint(0.1);

		Assert.Null(first.GetComponent<LightData>());
		Assert.Null(second.GetComponent<LightData>());
		Assert.Null(plain.GetComponent<LightData>());
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.NotNull(first.GetComponent<LightData>());
		Assert.NotNull(second.GetComponent<LightData>());
	}

	[Fact]
	public void TheAddBarOffersAComponentTheRestOfTheSelectionLacks()
	{
		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.All.Single(candidate =>
				candidate.Create() is MeshRendererData);

		SceneEntity has = _session.CreateEntity("Has")!;
		SceneEntity lacks = _session.CreateEntity("Lacks")!;

		_session.AddComponent(has.Id, descriptor.TypeId);
		_session.SelectMany([has.Id, lacks.Id], has.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		// The active object already has the renderer, but the other
		// selected object does not. Hiding the option here would make
		// the Component menu the only way to finish the group add.
		Dropdown types = Descendants(inspector)
			.OfType<Dropdown>()
			.Single();

		int index = types.Items
			.ToList()
			.IndexOf(descriptor.DisplayName);

		Assert.True(
			index >= 0,
			"A component the rest of the selection lacks must be offered.");

		types.SelectedIndex = index;

		int history = _session.Undo.UndoCount;

		Button add = Descendants(inspector)
			.OfType<Button>()
			.Single(button => button.Text == "Add Component");

		Click(add);
		Paint(0.1);

		Assert.True(lacks.HasComponent(descriptor.TypeId));
		Assert.Single(
			has.Components,
			component => component.TypeId == descriptor.TypeId);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
	}

	[Fact]
	public void TheAddBarGivesTheComponentToEverySelectedObject()
	{
		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;

		EditorComponentDescriptor expected =
			ComponentCatalog.Default.All.First();

		_session.SelectMany([first.Id, second.Id], first.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		int history = _session.Undo.UndoCount;

		Button add = Descendants(inspector)
			.OfType<Button>()
			.Single(button => button.Text == "Add Component");

		Click(add);
		Paint(0.1);

		Assert.True(first.HasComponent(expected.TypeId));
		Assert.True(second.HasComponent(expected.TypeId));
		Assert.Equal(history + 1, _session.Undo.UndoCount);
	}

	private void Click(Widget widget)
	{
		UiPoint center = widget.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(520f, 900f), UiPoint.Zero);
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
