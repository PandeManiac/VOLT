using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the inspector's name field, including what a cleared name does.
/// </summary>
/// <remarks>
/// An object always needs a name: the rename command refuses an empty
/// one. Committing the field with nothing in it must therefore restore
/// the current name and say so in the status bar, rather than surfacing
/// the refusal as an error in the console.
/// </remarks>
public sealed class InspectorNameTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-inspector-name-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public InspectorNameTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Names");
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
	public void ClearingTheNameKeepsItAndSaysWhy()
	{
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		TextField name = Descendants(inspector)
			.OfType<TextField>()
			.First();

		Assert.True(name.Focus());
		name.SelectAll();
		_context.TextInput("   ");
		Paint(0.1);

		int history = _session.Undo.UndoCount;
		int errors = _session.LogEntries.Count(
			entry => entry.Level == EditorLogLevel.Error);

		name.Commit();
		Paint(0.2);

		Assert.Equal("Cube", cube.Name);
		Assert.Equal("Cube", name.Text);
		Assert.Equal(history, _session.Undo.UndoCount);
		Assert.Equal("A name is required", _session.Status);
		Assert.Equal(
			errors,
			_session.LogEntries.Count(
				entry => entry.Level == EditorLogLevel.Error));
	}

	[Fact]
	public void ANewNameIsAppliedAndUndone()
	{
		SceneEntity cube = _session.CreateEntity("Cube")!;

		_session.Select(cube.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		TextField name = Descendants(inspector)
			.OfType<TextField>()
			.First();

		Assert.True(name.Focus());
		name.SelectAll();
		_context.TextInput("Cube Renamed");
		Paint(0.1);

		name.Commit();
		Paint(0.2);

		Assert.Equal("Cube Renamed", cube.Name);
		Assert.Equal("Cube Renamed", name.Text);

		_session.UndoLast();

		Assert.Equal("Cube", cube.Name);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(420f, 800f), UiPoint.Zero);
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
