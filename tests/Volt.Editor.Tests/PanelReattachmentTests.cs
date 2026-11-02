using Volt.Editor.Panels;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

public sealed class PanelReattachmentTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(), $"volt-panel-reattach-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public PanelReattachmentTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Reattach");
	}

	[Fact]
	public void Inspector_ResubscribesAndShowsCurrentSelectionAfterReattachment()
	{
		SceneEntity[] entities = [.. _session.Scene!.Entities.Take(2)];
		Assert.Equal(2, entities.Length);
		_session.Select(entities[0].Id);

		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		Assert.Equal(entities[0].Name, InspectorName(inspector).Text);

		Assert.True(_context.Root.Remove(inspector));
		_session.Select(entities[1].Id);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);

		Assert.Equal(entities[1].Name, InspectorName(inspector).Text);
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();
		try { Directory.Delete(_root, recursive: true); }
		catch (IOException) { }
	}

	private static TextField InspectorName(Widget root)
	{
		if (root is TextField field) return field;
		foreach (Widget child in root.Children)
		{
			try { return InspectorName(child); }
			catch (InvalidOperationException) { }
		}
		throw new InvalidOperationException("Inspector name field was not found.");
	}
}
