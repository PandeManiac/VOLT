using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the Component menu applying to the whole selection, the way
/// Delete, Duplicate, Cut and Paste already do.
/// </summary>
public sealed class ComponentMenuSelectionTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-component-selection-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public ComponentMenuSelectionTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ComponentSelection");
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
	public void AddingAComponent_SkipsObjectsThatAlreadyHaveIt()
	{
		EditorComponentDescriptor descriptor = ComponentCatalog.Default.All
			.First();

		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;

		_session.AddComponent(second.Id, descriptor.TypeId);
		_session.SelectMany([first.Id, second.Id], first.Id);

		int history = _session.Undo.UndoCount;

		Assert.Equal(1, _session.AddComponentToSelection(descriptor.TypeId));

		SceneEntity reloadedFirst =
			_session.Scene!.Find(first.Id)!;
		SceneEntity reloadedSecond =
			_session.Scene!.Find(second.Id)!;

		Assert.True(reloadedFirst.HasComponent(descriptor.TypeId));
		Assert.Single(
			reloadedSecond.Components,
			component => component.TypeId == descriptor.TypeId);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.False(
			_session.Scene!.Find(first.Id)!.HasComponent(descriptor.TypeId));
		Assert.True(
			_session.Scene!.Find(second.Id)!.HasComponent(descriptor.TypeId));
	}

	[Fact]
	public void AddingAComponent_ReachesEverySelectedObjectInOneStep()
	{
		EditorComponentDescriptor descriptor = ComponentCatalog.Default.All
			.First();

		SceneEntity first = _session.CreateEntity("First")!;
		SceneEntity second = _session.CreateEntity("Second")!;

		_session.SelectMany([first.Id, second.Id], first.Id);

		int history = _session.Undo.UndoCount;

		Assert.Equal(2, _session.AddComponentToSelection(descriptor.TypeId));
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.False(
			_session.Scene!.Find(first.Id)!.HasComponent(descriptor.TypeId));
		Assert.False(
			_session.Scene!.Find(second.Id)!.HasComponent(descriptor.TypeId));
	}

	[Fact]
	public void ComponentMenu_StaysAvailableForTheRestOfTheSelection()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		_context.BeginFrame(
			0.0,
			new UiSize(1440f, 900f),
			UiPoint.Zero);

		EditorComponentDescriptor descriptor = ComponentCatalog.Default.All
			.Single(candidate =>
				candidate.Create() is MeshRendererData);

		SceneEntity has = _session.CreateEntity("Has")!;
		SceneEntity lacks = _session.CreateEntity("Lacks")!;

		_session.AddComponent(has.Id, descriptor.TypeId);

		// The active object already has the component; the other
		// selected object does not, so the command still has work.
		_session.SelectMany([has.Id, lacks.Id], has.Id);

		MenuItem add = shell.MenuBarView.Menus
			.Single(menu => menu.Label == "Component")
			.Children
			.Single(item => item.Label == $"Add {descriptor.DisplayName}");

		Assert.True(
			add.IsEnabled,
			"A component the rest of the selection lacks must stay addable.");

		add.Action!.Invoke();

		Assert.True(
			_session.Scene!.Find(lacks.Id)!.HasComponent(descriptor.TypeId));
		Assert.Single(
			_session.Scene!.Find(has.Id)!.Components,
			component => component.TypeId == descriptor.TypeId);
	}
}
