using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers which surface owns the keyboard while a field is being edited.
/// </summary>
/// <remarks>
/// A focused text field owns Ctrl+Z for its own text, as the guide
/// describes. The scene shortcuts already stand down for that reason;
/// undo and redo must do the same, or a typed value would be reverted in
/// the scene as well.
/// </remarks>
public sealed class FieldShortcutScopeTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-field-shortcuts-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public FieldShortcutScopeTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Shortcuts");
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
	public void UndoWithAFieldFocusedLeavesTheSceneAlone()
	{
		EditorShell shell = BuildShell();
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);
		Layout();

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();
		NumericField field = Descendants(inspector)
			.OfType<NumericField>()
			.First();

		Assert.True(field.Focus());
		_context.TextInput("7");
		Layout();

		Assert.True(field.HasPendingTextEdit);

		int history = _session.Undo.UndoCount;

		_context.KeyDown(UiKey.Z, UiKeyModifiers.Control);

		// The field owns the keystroke: the scene history must not move,
		// or the object would change while it is being typed into.
		Assert.Equal(history, _session.Undo.UndoCount);
		Assert.NotNull(_session.Scene!.Find(cube.Id));
	}

	[Fact]
	public void RedoWithAFieldFocusedLeavesTheSceneAlone()
	{
		EditorShell shell = BuildShell();
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);
		Layout();

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();
		NumericField field = Descendants(inspector)
			.OfType<NumericField>()
			.First();

		Assert.True(field.Focus());
		_context.TextInput("9");
		Layout();

		int history = _session.Undo.UndoCount;

		_context.KeyDown(UiKey.Y, UiKeyModifiers.Control);

		Assert.Equal(history, _session.Undo.UndoCount);
	}

	private EditorShell BuildShell()
	{
		EditorShell shell = new(_session);

		_context.Add(shell);
		Layout();

		return shell;
	}

	private void Layout()
	{
		_context.BeginFrame(0.0, new UiSize(1440f, 900f), UiPoint.Zero);
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
