using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers when the ECS panel rebuilds its rows.
/// </summary>
/// <remarks>
/// The panel refreshes four times a second. Rebuilding every time costs a
/// frame of layout and drops focus from a field the developer just
/// reached, so a refresh whose snapshot matches what is already displayed
/// must leave the rows in place, while a real change must replace them.
/// </remarks>
public sealed class EcsInspectorRefreshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-ecs-refresh-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EcsInspectorRefreshTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Refresh");
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
	public void ARefreshOfAnUnchangedWorld_KeepsTheSameRows()
	{
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Label first = PositionHeading(panel);

		Assert.NotNull(first);

		// A tick with no change in the world, as the refresh timer makes.
		panel.Tick(0.3, 1f);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.Same(first, PositionHeading(panel));
	}

	[Fact]
	public void SelectingAnotherEntity_RebuildsTheRows()
	{
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		Guid otherId = _session.Scene!.Entities
			.First(entity => entity.Id != meshId)
			.Id;

		_session.Select(meshId);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Label first = PositionHeading(panel);

		_session.Select(otherId);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.NotSame(first, PositionHeading(panel));
	}

	[Fact]
	public void ASteadyWorld_KeepsTheQueryRowsInPlace()
	{
		_session.StartPlay();

		using EcsInspectorPanel panel = new(
			_session,
			new EcsInspectorPanelState
			{
				RequiredComponents =
					[ComponentRegistry.Get<PositionComponent>()]
			});

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Label first = QueryRow(panel);

		Assert.NotNull(first);

		panel.Tick(0.3, 1f);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.Same(first, QueryRow(panel));
	}

	[Fact]
	public void FilteringTheQuery_RebuildsItsRows()
	{
		_session.StartPlay();

		using EcsInspectorPanel panel = new(
			_session,
			new EcsInspectorPanelState
			{
				RequiredComponents =
					[ComponentRegistry.Get<PositionComponent>()]
			});

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Label first = QueryRow(panel);

		TextField? filter = QueryFilter(panel);

		Assert.NotNull(filter);
		Assert.True(filter.Focus());

		// Typing, rather than assigning Text, is what raises the change the
		// panel listens to.
		Assert.True(_context.KeyDown(UiKey.A, UiKeyModifiers.Control));
		_context.TextInput("Cube");
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.NotSame(first, QueryRow(panel));
	}

	[Fact]
	public void RenamingAListedEntity_RefreshesTheQueryRows()
	{
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_session.Select(meshId);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(
			_session,
			new EcsInspectorPanelState
			{
				RequiredComponents =
					[ComponentRegistry.Get<PositionComponent>()]
			});

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		string[] before = QueryRowTexts(panel);

		Assert.DoesNotContain("Renamed", before);

		// A rename changes no archetype, so only the scene event can tell
		// the panel that the listed names moved.
		_session.RenameActiveEntity("Renamed");
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		string[] after = QueryRowTexts(panel);

		Assert.True(
			after.Any(text => text.Contains("Renamed", StringComparison.Ordinal)),
			"Rows before: " + string.Join(" | ", before) +
			"\nRows after: " + string.Join(" | ", after));
	}

	[Fact]
	public void RenamingTheSelectedEntity_RefreshesTheHeader()
	{
		Guid meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_session.Select(meshId);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.DoesNotContain("Renamed", LabelTexts(panel));

		// The header names the authored entity, so a rename has to reach
		// it through the scene rather than the runtime world's snapshot.
		_session.RenameActiveEntity("Renamed");
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		Assert.Contains("Renamed", LabelTexts(panel));
	}

	[Fact]
	public void RuntimeUpdates_WaitForTheRefreshTimer()
	{
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);

		int captures = panel.RefreshCount;

		// A fixed step raises a runtime update, as a playing frame does.
		// Capturing on it would rebuild a moving entity's rows at frame
		// rate instead of the four times a second the panel displays at.
		_session.Update(1.0);

		Assert.Equal(captures, panel.RefreshCount);

		// The timer is what schedules the next capture.
		panel.Tick(0.1, 1f);

		Assert.Equal(captures, panel.RefreshCount);

		panel.Tick(0.2, 1f);

		Assert.Equal(captures + 1, panel.RefreshCount);
	}

	private static string[] QueryRowTexts(Widget root)
	{
		List<string> texts = [];
		CollectRows(root, texts);

		return [.. texts];
	}

	private static string[] LabelTexts(Widget root)
	{
		List<string> texts = [];
		CollectLabels(root, texts);

		return [.. texts];
	}

	private static void CollectLabels(Widget root, List<string> texts)
	{
		if (root is Label label)
		{
			texts.Add(label.Text);
		}

		foreach (Widget child in root.Children)
		{
			CollectLabels(child, texts);
		}
	}

	private static void CollectRows(Widget root, List<string> texts)
	{
		if (root is Label label &&
			label.GetType().Name == "QueryResultRow")
		{
			texts.Add(label.Text);
		}

		foreach (Widget child in root.Children)
		{
			CollectRows(child, texts);
		}
	}

	private static Label QueryRow(Widget root)
	{
		if (root is Label label &&
			label.GetType().Name == "QueryResultRow")
		{
			return label;
		}

		foreach (Widget child in root.Children)
		{
			Label? found = QueryRow(child);

			if (found is not null)
			{
				return found;
			}
		}

		return null!;
	}

	private static TextField? QueryFilter(Widget root)
	{
		if (root is TextField { Name: "QueryFilter" } filter)
		{
			return filter;
		}

		foreach (Widget child in root.Children)
		{
			TextField? found = QueryFilter(child);

			if (found is not null)
			{
				return found;
			}
		}

		return null;
	}

	private static Label PositionHeading(Widget root)
	{
		if (root is Label { Text: "Position" } heading)
		{
			return heading;
		}

		foreach (Widget child in root.Children)
		{
			Label? found = PositionHeading(child);

			if (found is not null)
			{
				return found;
			}
		}

		return null!;
	}
}
