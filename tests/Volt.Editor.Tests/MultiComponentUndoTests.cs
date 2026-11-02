using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers undo and redo of a component change that spanned the whole
/// selection, including the rows the inspector shows afterwards.
/// </summary>
/// <remarks>
/// A group change is a composite command, and undo has to put the same
/// component back on every object it left. The panel also has to notice
/// without being rebuilt by a selection change.
/// </remarks>
public sealed class MultiComponentUndoTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-multi-undo-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public MultiComponentUndoTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "MultiUndo");
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
	public void UndoRestoresTheComponentOnEveryObjectAndTheRow()
	{
		SceneEntity first = _session.CreateDirectionalLight("First")!;
		SceneEntity second = _session.CreateDirectionalLight("Second")!;

		_session.SelectMany([first.Id, second.Id], first.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		Assert.True(_session.RemoveComponentFromSelection(
			LightData.ComponentTypeId) == 2);

		Paint(0.1);

		Assert.Null(first.GetComponent<LightData>());
		Assert.Null(second.GetComponent<LightData>());

		_session.UndoLast();
		Paint(0.2);

		Assert.NotNull(first.GetComponent<LightData>());
		Assert.NotNull(second.GetComponent<LightData>());

		Assert.Contains(
			Descendants(inspector).OfType<Label>(),
			label => label.Text == "Directional Light");

		_session.RedoLast();
		Paint(0.3);

		Assert.Null(first.GetComponent<LightData>());
		Assert.Null(second.GetComponent<LightData>());
	}

	[Fact]
	public void UndoOfAGroupEditRestoresEveryValue()
	{
		SceneEntity first = _session.CreateDirectionalLight("First")!;
		SceneEntity second = _session.CreateDirectionalLight("Second")!;
		float original = first.GetComponent<LightData>()!.Intensity;

		_session.SelectMany([first.Id, second.Id], first.Id);

		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.Get(LightData.ComponentTypeId);

		Assert.Equal(
			2,
			_session.SetSelectionComponentProperty(
				first.Id,
				first.GetComponent<LightData>()!,
				descriptor.Properties.Single(
					property => property.DisplayName == "Intensity"),
				0.25f));

		Assert.Equal(0.25f, second.GetComponent<LightData>()!.Intensity, 3);

		_session.UndoLast();

		Assert.Equal(original, first.GetComponent<LightData>()!.Intensity, 3);
		Assert.Equal(original, second.GetComponent<LightData>()!.Intensity, 3);
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
