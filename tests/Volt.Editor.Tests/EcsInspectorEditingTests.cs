using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Drives the ECS inspector's runtime fields through real headless
/// input: pointer focus, typing, the panel's periodic refresh, and the
/// Enter commit.
/// </summary>
/// <remarks>
/// The panel rebuilds its rows four times a second from a live-world
/// snapshot. These tests prove that a focused field stops that rebuild
/// instead of being replaced mid-edit, and that the committed value
/// reaches the runtime world while the authored scene stays untouched.
/// </remarks>
public sealed class EcsInspectorEditingTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-ecs-editing-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public EcsInspectorEditingTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "ECS Editing");
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
	public void TypingInARuntimeField_SurvivesRefreshAndCommitsToTheLiveWorld()
	{
		SceneEntity player = _session.CreateEntity("Player")!;
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.Select(player.Id);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(520f, 900f), UiPoint.Zero);

		NumericField x = PositionXField(panel);

		Assert.True(
			x.GlobalVisualBounds.Width > 0f &&
			x.GlobalVisualBounds.Height > 0f,
			"The runtime field must be laid out before it can be clicked.");

		Click(x.GlobalVisualBounds.Center);

		Assert.True(
			x.HasFocus,
			"The pointer click must focus the runtime field.");

		Assert.True(_context.KeyDown(UiKey.A, UiKeyModifiers.Control));
		_context.TextInput("12.5");

		// The panel refreshes four times a second. While a field is being
		// edited that refresh must not rebuild the row underneath it.
		panel.Tick(0.3, 1f);

		Assert.Same(x, _context.FocusedWidget);
		Assert.True(x.HasFocus);
		Assert.Equal("12.5", x.Text);

		RuntimeScene runtime = _session.Play!.Runtime!;
		Entity entity = runtime.Find(player.Id)!.Entity;

		Assert.Equal(
			new Vector3(1f, 2f, 3f),
			runtime.World.GetComponent<PositionComponent>(entity).Value);

		Assert.True(_context.KeyDown(UiKey.Enter));

		Assert.Equal(
			12.5f,
			runtime.World.GetComponent<PositionComponent>(entity).Value.X,
			3);

		// The write-back rule: live world only, never authored data.
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"applied to the live world",
				StringComparison.Ordinal));
	}

	[Fact]
	public void LeavingTheField_RebuildsTheRowFromTheCommittedValue()
	{
		SceneEntity player = _session.CreateEntity("Player")!;
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.Select(player.Id);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(520f, 900f), UiPoint.Zero);

		NumericField first = PositionXField(panel);

		Click(first.GlobalVisualBounds.Center);
		Assert.True(_context.KeyDown(UiKey.A, UiKeyModifiers.Control));
		_context.TextInput("-4");

		panel.Tick(0.3, 1f);
		Assert.Same(first, _context.FocusedWidget);

		Assert.True(_context.KeyDown(UiKey.Enter));

		first.ClearFocus();
		Assert.Null(_context.FocusedWidget);

		panel.Tick(0.3, 1f);

		NumericField rebuilt = PositionXField(panel);

		Assert.NotSame(first, rebuilt);
		Assert.Equal("-4", rebuilt.Text);

		Assert.Equal(
			-4f,
			_session.Play!.Runtime!.World.GetComponent<PositionComponent>(
				_session.Play.Runtime.Find(player.Id)!.Entity).Value.X,
			3);
	}

	[Fact]
	public void ApplyToScene_CopiesTheRuntimeValueAndUndoRestoresIt()
	{
		SceneEntity player = _session.CreateEntity("Player")!;
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.Select(player.Id);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(520f, 900f), UiPoint.Zero);

		int history = _session.Undo.UndoCount;

		NumericField x = PositionXField(panel);

		Click(x.GlobalVisualBounds.Center);
		Assert.True(_context.KeyDown(UiKey.A, UiKeyModifiers.Control));
		_context.TextInput("6.5");
		Assert.True(_context.KeyDown(UiKey.Enter));

		// Editing the live world is still not an authored change.
		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(history, _session.Undo.UndoCount);

		Button apply = ApplyToSceneButton(x);

		Assert.True(
			apply.GlobalVisualBounds.Width > 0f &&
			apply.GlobalVisualBounds.Height > 0f,
			"The apply button must be laid out before it can be clicked.");

		Click(apply.GlobalVisualBounds.Center);

		Assert.Equal(6.5f, player.Transform.Position.X, 3);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"undo step",
				StringComparison.Ordinal));

		_session.UndoLast();

		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
	}

	[Fact]
	public void ApplyTransformToScene_CopiesPositionAndScaleInOneUndoStep()
	{
		SceneEntity player = _session.CreateEntity("Player")!;
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.Select(player.Id);
		_session.StartPlay();

		RuntimeScene runtime = _session.Play!.Runtime!;

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(4f, 5f, 6f));

		RuntimeWorldEditor.Apply(
			runtime,
			player.Id,
			RuntimeEditChannel.Scale,
			new Vector3(2f, 2f, 2f));

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(520f, 900f), UiPoint.Zero);

		int history = _session.Undo.UndoCount;

		Button apply = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Apply Transform to Scene");

		Assert.True(
			apply.GlobalVisualBounds.Width > 0f &&
			apply.GlobalVisualBounds.Height > 0f,
			"The combined apply button must be laid out.");

		Click(apply.GlobalVisualBounds.Center);

		Assert.Equal(new Vector3(4f, 5f, 6f), player.Transform.Position);
		Assert.Equal(new Vector3(2f, 2f, 2f), player.Transform.Scale);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Equal(new Vector3(1f, 2f, 3f), player.Transform.Position);
		Assert.Equal(Vector3.One, player.Transform.Scale);
	}

	[Fact]
	public void DivergenceHint_ShowsTheAuthoredValueAndTracksTheCopy()
	{
		SceneEntity player = _session.CreateEntity("Player")!;
		player.Transform.Position = new Vector3(1f, 2f, 3f);

		_session.Select(player.Id);
		_session.StartPlay();

		RuntimeWorldEditor.Apply(
			_session.Play!.Runtime!,
			player.Id,
			RuntimeEditChannel.Position,
			new Vector3(7f, 8f, 9f));

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(520f, 900f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Authored: (1, 2, 3) - differs from the runtime value",
				StringComparison.Ordinal));

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Authored: (1, 1, 1) - matches the runtime value",
				StringComparison.Ordinal));

		Button apply = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Apply Transform to Scene");

		Click(apply.GlobalVisualBounds.Center);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Authored: (7, 8, 9) - matches the runtime value",
				StringComparison.Ordinal));

		Assert.DoesNotContain(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"differs from the runtime value",
				StringComparison.Ordinal));

		_session.UndoLast();
		panel.Tick(0.3, 1f);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Authored: (1, 2, 3) - differs from the runtime value",
				StringComparison.Ordinal));
	}

	[Fact]
	public void QueryResults_PageThroughMatchedEntities()
	{
		for (int index = 0; index < 10; index++)
		{
			_session.CreateEntity($"Probe {index}");
		}

		_session.StartPlay();

		int total = _session.Play!.Runtime!.World.Count;
		const int PageSize = 8;
		int pages = (total + PageSize - 1) / PageSize;

		Assert.True(
			total > PageSize,
			"The test needs more entities than one page holds.");

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		CheckBox position = Descendants(panel)
			.OfType<CheckBox>()
			.Single(box => box.Text == "Position");

		Click(position.GlobalVisualBounds.Center);
		Assert.True(position.IsChecked);

		Button run = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Run Query");

		Click(run.GlobalVisualBounds.Center);

		// The pager becomes visible only once a query matches, so the
		// first page has to be laid out before its buttons can be hit.
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Equal(PageSize, QueryRows(panel).Count());

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 1 of {pages}",
				StringComparison.Ordinal));

		Button next = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Next");

		Assert.True(next.IsEnabled);
		Assert.True(
			next.GlobalVisualBounds.Width > 0f,
			"The next-page button must be laid out.");

		Click(next.GlobalVisualBounds.Center);
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Equal(
			Math.Min(PageSize, total - PageSize),
			QueryRows(panel).Count());

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 2 of {pages}",
				StringComparison.Ordinal));

		Button previous = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Prev");

		Assert.True(previous.IsEnabled);

		Click(previous.GlobalVisualBounds.Center);
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Equal(PageSize, QueryRows(panel).Count());
		Assert.False(previous.IsEnabled);
	}

	[Fact]
	public void QueryResults_ClickingARowSelectsTheAuthoredObject()
	{
		SceneEntity alpha = _session.CreateEntity("Alpha")!;
		SceneEntity beta = _session.CreateEntity("Beta")!;

		_session.Select(alpha.Id);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Label betaRow = QueryRows(panel).Single(row =>
			row.Text.StartsWith("Beta", StringComparison.Ordinal));

		Assert.True(
			betaRow.GlobalVisualBounds.Width > 0f,
			"The query row must be laid out before it can be clicked.");

		Click(betaRow.GlobalVisualBounds.Center);

		Assert.Equal(beta.Id, _session.ActiveEntityId);
	}

	[Fact]
	public void QueryResults_RuntimeOnlyRowsCannotSelectAnAuthoredObject()
	{
		_session.CreateEntity("Player");
		_session.StartPlay();

		RuntimeScene runtime = _session.Play!.Runtime!;
		Entity only = runtime.World.CreateEntity();
		runtime.World.AddComponent(only, new PositionComponent(Vector3.One));

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Label row = QueryRows(panel).Single(candidate =>
			candidate.Text.Contains("(runtime only)", StringComparison.Ordinal));

		Assert.False(row.IsEnabled);
		Assert.Equal(UiCursor.Arrow, row.Cursor);

		Guid? before = _session.ActiveEntityId;

		Click(row.GlobalVisualBounds.Center);

		Assert.Equal(before, _session.ActiveEntityId);
	}

	[Fact]
	public void QueryResults_FilterAndSortRewriteTheVisibleRows()
	{
		_session.CreateEntity("Gamma");
		_session.CreateEntity("Alpha");
		_session.CreateEntity("Beta");
		_session.StartPlay();

		int total = _session.Play!.Runtime!.World.Count;

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		int sceneGamma = RowIndex(panel, "Gamma");

		Assert.True(sceneGamma < RowIndex(panel, "Alpha"));
		Assert.True(sceneGamma < RowIndex(panel, "Beta"));

		Button byName = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Name");

		Click(byName.GlobalVisualBounds.Center);
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.True(byName.IsSelected);
		Assert.True(RowIndex(panel, "Alpha") < RowIndex(panel, "Beta"));
		Assert.True(RowIndex(panel, "Beta") < RowIndex(panel, "Gamma"));

		TextField filter = Descendants(panel)
			.OfType<TextField>()
			.Single(field => field.Name == "QueryFilter");

		Click(filter.GlobalVisualBounds.Center);
		Assert.True(filter.HasFocus);

		_context.TextInput("Beta");
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Label only = Assert.Single(QueryRows(panel));

		Assert.StartsWith("Beta", only.Text, StringComparison.Ordinal);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"1 of {total} entities",
				StringComparison.Ordinal));
	}

	[Fact]
	public void QueryResults_CopyButtonWritesTheFilteredRows()
	{
		_session.CreateEntity("Alpha");
		_session.CreateEntity("Beta");
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Label[] rows = [.. QueryRows(panel)];

		Assert.Contains(rows, row => row.Text.StartsWith(
			"Alpha",
			StringComparison.Ordinal));

		Assert.Contains(rows, row => row.Text.StartsWith(
			"Beta",
			StringComparison.Ordinal));

		Button copy = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Copy Results");

		Click(copy.GlobalVisualBounds.Center);

		string? text = _context.Clipboard.GetText();

		Assert.NotNull(text);

		string[] lines = text.Split(Environment.NewLine);

		Assert.Equal(rows.Length, lines.Length);

		for (int index = 0; index < rows.Length; index++)
		{
			Assert.Equal(rows[index].Text, lines[index]);
		}
	}

	[Fact]
	public void QueryResults_ArrowKeysMoveFocusAndSelectionBetweenRows()
	{
		SceneEntity alpha = _session.CreateEntity("Alpha")!;
		SceneEntity beta = _session.CreateEntity("Beta")!;

		_session.Select(alpha.Id);
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		// A new scene already holds a camera and a light, so the authored
		// objects are found by name rather than by a fixed row index.
		List<Label> rows = [.. QueryRows(panel)];
		int alphaRow = rows.FindIndex(row => row.Text.StartsWith(
			"Alpha",
			StringComparison.Ordinal));

		Assert.True(alphaRow >= 0);
		Assert.StartsWith("Beta", rows[alphaRow + 1].Text, StringComparison.Ordinal);

		Assert.True(rows[alphaRow].Focus());

		_context.KeyDown(UiKey.Down);

		Assert.True(rows[alphaRow + 1].HasFocus);
		Assert.Equal(beta.Id, _session.ActiveEntityId);

		_context.KeyDown(UiKey.Up);

		Assert.True(rows[alphaRow].HasFocus);
		Assert.Equal(alpha.Id, _session.ActiveEntityId);
	}

	[Fact]
	public void QueryResults_FilterAndSortSurviveAPanelRebuild()
	{
		_session.StartPlay();

		using EcsInspectorPanel first = new(_session);

		_context.Add(first);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		TextField filter = Descendants(first)
			.OfType<TextField>()
			.Single(field => field.Name == "QueryFilter");

		Click(filter.GlobalVisualBounds.Center);
		_context.TextInput("Bet");

		Button byName = Descendants(first)
			.OfType<Button>()
			.Single(button => button.Text == "Name");

		Click(byName.GlobalVisualBounds.Center);

		EcsInspectorPanelState state = first.CaptureState();

		first.RemoveFromParent();
		first.Dispose();

		using EcsInspectorPanel second = new(_session, state);

		_context.Add(second);
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		TextField restored = Descendants(second)
			.OfType<TextField>()
			.Single(field => field.Name == "QueryFilter");

		Assert.Equal("Bet", restored.Text);

		List<Button> sorts = [.. Descendants(second).OfType<Button>()];

		Assert.True(sorts.Single(button => button.Text == "Name").IsSelected);
		Assert.False(sorts.Single(button => button.Text == "Scene order").IsSelected);
	}

	[Fact]
	public void QueryResults_ArrowKeysContinueOntoTheAdjacentPage()
	{
		for (int index = 0; index < 10; index++)
		{
			_session.CreateEntity($"Probe {index}");
		}

		_session.StartPlay();

		int total = _session.Play!.Runtime!.World.Count;
		const int PageSize = 8;
		int pages = (total + PageSize - 1) / PageSize;

		Assert.True(pages > 1, "The test needs more than one page.");

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		List<Label> firstPage = [.. QueryRows(panel)];

		Assert.Equal(PageSize, firstPage.Count);
		Assert.True(firstPage[0].Focus());

		// One press per row walks off the end of the page instead of
		// stopping on the last row.
		for (int index = 0; index < firstPage.Count; index++)
		{
			_context.KeyDown(UiKey.Down);
		}

		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 2 of {pages}",
				StringComparison.Ordinal));

		List<Label> secondPage = [.. QueryRows(panel)];

		Assert.True(
			secondPage[0].HasFocus,
			"Down at the page edge must focus the first row of the next page.");

		Guid? entered = _session.ActiveEntityId;

		Assert.NotNull(entered);

		_context.KeyDown(UiKey.Up);
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Page 1 of " + pages.ToString(
					System.Globalization.CultureInfo.InvariantCulture),
				StringComparison.Ordinal));

		List<Label> back = [.. QueryRows(panel)];

		Assert.True(
			back[^1].HasFocus,
			"Up at the page edge must focus the last row of the previous page.");

		Assert.NotEqual(entered, _session.ActiveEntityId);
	}

	[Fact]
	public void QueryResults_PageKeysJumpAWholePage()
	{
		for (int index = 0; index < 10; index++)
		{
			_session.CreateEntity($"Probe {index}");
		}

		_session.StartPlay();

		int total = _session.Play!.Runtime!.World.Count;
		const int PageSize = 8;
		int pages = (total + PageSize - 1) / PageSize;

		Assert.True(pages > 1, "The test needs more than one page.");

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		List<Label> firstPage = [.. QueryRows(panel)];

		Assert.True(firstPage[0].Focus());

		_context.KeyDown(UiKey.PageDown);
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 2 of {pages}",
				StringComparison.Ordinal));

		List<Label> secondPage = [.. QueryRows(panel)];

		Assert.True(
			secondPage[0].HasFocus,
			"Page Down must land on the first row of the next page.");

		Assert.NotNull(_session.ActiveEntityId);

		_context.KeyDown(UiKey.PageUp);
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 1 of {pages}",
				StringComparison.Ordinal));

		List<Label> back = [.. QueryRows(panel)];

		Assert.True(
			back[^1].HasFocus,
			"Page Up must land on the last row of the previous page.");
	}

	[Fact]
	public void QueryResults_HomeAndEndJumpToTheEndsOfTheList()
	{
		for (int index = 0; index < 10; index++)
		{
			_session.CreateEntity($"Probe {index}");
		}

		_session.StartPlay();

		int total = _session.Play!.Runtime!.World.Count;
		const int PageSize = 8;
		int pages = (total + PageSize - 1) / PageSize;

		Assert.True(pages > 1, "The test needs more than one page.");

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		List<Label> firstPage = [.. QueryRows(panel)];

		// Start in the middle of the list so both keys have to travel.
		Assert.True(firstPage[1].Focus());

		_context.KeyDown(UiKey.End);
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page {pages} of {pages}",
				StringComparison.Ordinal));

		List<Label> lastPage = [.. QueryRows(panel)];

		Assert.True(
			lastPage[^1].HasFocus,
			"End must focus the last row of the whole list.");

		Assert.NotNull(_session.ActiveEntityId);

		_context.KeyDown(UiKey.Home);
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				$"Page 1 of {pages}",
				StringComparison.Ordinal));

		List<Label> back = [.. QueryRows(panel)];

		Assert.True(
			back[0].HasFocus,
			"Home must focus the first row of the whole list.");
	}

	[Fact]
	public void QueryResults_ResetClearsTheFilterSortAndChecks()
	{
		_session.CreateEntity("Alpha");
		_session.CreateEntity("Beta");
		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Button byName = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Name");

		Click(byName.GlobalVisualBounds.Center);

		TextField filter = Descendants(panel)
			.OfType<TextField>()
			.Single(field => field.Name == "QueryFilter");

		Click(filter.GlobalVisualBounds.Center);
		_context.TextInput("Al");
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Single(QueryRows(panel));

		Button reset = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Reset");

		Click(reset.GlobalVisualBounds.Center);
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Empty(QueryRows(panel));

		TextField cleared = Descendants(panel)
			.OfType<TextField>()
			.Single(field => field.Name == "QueryFilter");

		Assert.Equal(string.Empty, cleared.Text);

		Assert.False(Descendants(panel)
			.OfType<CheckBox>()
			.Single(box => box.Text == "Position")
			.IsChecked);

		Assert.True(Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Scene order")
			.IsSelected);

		EcsInspectorPanelState state = panel.CaptureState();

		Assert.Equal(string.Empty, state.Filter);
		Assert.Equal(EcsQuerySort.SceneOrder, state.Sort);
		Assert.Empty(state.RequiredComponents);
	}

	[Fact]
	public void QueryResults_PagerStaysOutsideTheScrollingDocument()
	{
		// Enough entities to fill a page and enable Next, and enough of
		// them that the document is taller than a short panel.
		for (int index = 0; index < 10; index++)
		{
			_session.CreateEntity($"Probe {index}");
		}

		_session.StartPlay();

		using EcsInspectorPanel panel = new(_session);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		RunPositionQuery(panel);
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		// Shrinking the panel makes the document taller than the view,
		// which is the case a pager inside the scroll would disappear in.
		_context.BeginFrame(0.2, new UiSize(600f, 420f), UiPoint.Zero);

		ScrollView scroll = Descendants(panel).OfType<ScrollView>().Single();
		Button next = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Next");

		Assert.DoesNotContain(next, Descendants(scroll));

		Assert.True(
			next.GlobalVisualBounds.Height > 0f,
			"The pinned pager must be laid out outside the scroll area.");

		Assert.True(next.IsEnabled);
	}

	[Fact]
	public void QueryResults_RememberedComponentsRerunTheQueryOnPlay()
	{
		_session.CreateEntity("Alpha");
		_session.CreateEntity("Beta");

		ComponentType position = ComponentRegistry.GetRegisteredComponents()
			.Single(info => info.ClrType == typeof(PositionComponent))
			.Type;

		EcsInspectorPanelState state = new()
		{
			RequiredComponents = [position]
		};

		using EcsInspectorPanel panel = new(_session, state);

		_context.Add(panel);
		_context.BeginFrame(0.0, new UiSize(600f, 4000f), UiPoint.Zero);

		CheckBox restored = Descendants(panel)
			.OfType<CheckBox>()
			.Single(box => box.Text == "Position");

		Assert.True(
			restored.IsChecked,
			"The remembered components must come back checked.");

		Assert.Empty(QueryRows(panel));

		_session.StartPlay();
		_context.BeginFrame(0.1, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.NotEmpty(QueryRows(panel));

		Assert.Contains(
			Descendants(panel).OfType<Label>(),
			label => label.Text.Contains(
				"Matched",
				StringComparison.Ordinal));

		// Stopping clears results that describe a world that is gone, and
		// the next Play run is expected to answer the query again.
		_session.Stop();
		_context.BeginFrame(0.2, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.Empty(QueryRows(panel));

		_session.StartPlay();
		_context.BeginFrame(0.3, new UiSize(600f, 4000f), UiPoint.Zero);

		Assert.NotEmpty(QueryRows(panel));
	}

	private void RunPositionQuery(EcsInspectorPanel panel)
	{
		CheckBox position = Descendants(panel)
			.OfType<CheckBox>()
			.Single(box => box.Text == "Position");

		if (!position.IsChecked)
		{
			Click(position.GlobalVisualBounds.Center);
		}

		Assert.True(position.IsChecked);

		Button run = Descendants(panel)
			.OfType<Button>()
			.Single(button => button.Text == "Run Query");

		Click(run.GlobalVisualBounds.Center);
	}

	private static int RowIndex(EcsInspectorPanel panel, string name)
	{
		List<Label> rows = [.. QueryRows(panel)];

		return rows.FindIndex(row => row.Text.StartsWith(
			name,
			StringComparison.Ordinal));
	}

	private static IEnumerable<Label> QueryRows(EcsInspectorPanel panel)
	{
		return Descendants(panel)
			.OfType<Label>()
			.Where(label => string.Equals(
				label.Name,
				"QueryResultRow",
				StringComparison.Ordinal));
	}

	private static Button ApplyToSceneButton(NumericField field)
	{
		// The field sits in an axis row, the axis row sits in the
		// Vector3Field, and the vector and its apply button share the
		// component row. Position is the first vector, so this finds
		// the button that belongs to the field being edited rather
		// than the one on the scale row below it.
		Vector3Field editor = (Vector3Field)field.Parent!.Parent!;

		return editor.Parent!.Children
			.OfType<Button>()
			.Single(button => button.Text == "Apply to Scene");
	}

	private static NumericField PositionXField(EcsInspectorPanel panel)
	{
		// Position is the first vector component the snapshot marks
		// editable, so its editor is the first Vector3Field in the tree.
		Vector3Field editor = Descendants(panel)
			.OfType<Vector3Field>()
			.First();

		return Descendants(editor)
			.OfType<NumericField>()
			.First(field => string.Equals(
				field.Name,
				"X",
				StringComparison.Ordinal));
	}

	private void Click(UiPoint point)
	{
		_context.PointerDown(UiMouseButton.Left, point);
		_context.PointerUp(UiMouseButton.Left, point);
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
