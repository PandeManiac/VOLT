using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what happens to a typed property value when the object it is
/// bound to stops being the selected object.
/// </summary>
/// <remarks>
/// A field commits on Enter or focus loss, but the inspector rebuilds
/// its rows whenever the selection changes. The rebuild must not take
/// the field away first, or the value the author just typed would be
/// dropped while the object it belonged to looks unchanged.
/// </remarks>
public sealed class InspectorPendingEditTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-pending-edit-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public InspectorPendingEditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "PendingEdit");
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
	public void SwitchingSelectionCommitsTheTypedValueToTheOldObject()
	{
		List<SceneEntity> entities = [.. _session.Scene!.Entities];

		Assert.True(
			entities.Count >= 2,
			"The default scene needs two objects.");

		SceneEntity first = entities[0];
		SceneEntity second = entities[1];
		TransformData transform = first.Transform;

		_session.Select(first.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		NumericField positionX = Descendants(inspector)
			.OfType<NumericField>()
			.First();

		float original = positionX.Value;

		Assert.True(positionX.Focus());
		_context.TextInput("7");
		Paint(0.1);

		// No Enter and no Escape: the field still holds the typed text
		// when the author selects a different object.
		Assert.True(
			positionX.HasPendingTextEdit,
			"The typed text must still be pending at this point.");

		Assert.Equal(original, positionX.Value, 3);

		_session.Select(second.Id);
		Paint(0.2);

		Assert.Equal(7f, transform.Position.X, 3);
	}

	[Fact]
	public void ClearingSelectionCommitsTheTypedValueToTheObject()
	{
		SceneEntity entity = _session.Scene!.Entities.First();
		TransformData transform = entity.Transform;

		_session.Select(entity.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		NumericField positionX = Descendants(inspector)
			.OfType<NumericField>()
			.First();

		Assert.True(positionX.Focus());
		_context.TextInput("4");
		Paint(0.1);

		Assert.True(positionX.HasPendingTextEdit);

		// Clearing the selection empties the inspector, so the typed
		// text must reach the object before its rows go away.
		_session.ClearSelection();
		Paint(0.2);

		Assert.Equal(4f, transform.Position.X, 3);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(800f, 900f), UiPoint.Zero);
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
