using System.Numerics;
using Volt.Ecs;
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
