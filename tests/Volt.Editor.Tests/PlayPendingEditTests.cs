using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers what Play Mode sees when the author starts it straight from a
/// field that still holds a typed value.
/// </summary>
/// <remarks>
/// A property field commits on Enter or focus loss, and the transport
/// buttons do not take focus, so pressing Play leaves the typed text in
/// the field. Play builds the runtime world from the authored scene, so
/// without a commit the simulation would run the value the field is
/// about to replace.
/// </remarks>
public sealed class PlayPendingEditTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-play-pending-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public PlayPendingEditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "PlayPending");
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
	public void StartingPlayCommitsTheTypedValueFirst()
	{
		SceneEntity entity = _session.Scene!.Entities.First();
		TransformData transform = entity.Transform;

		_session.Select(entity.Id);

		EditorShell shell = new(_session);

		_context.Add(shell);
		Paint(0.0);

		InspectorPanel inspector = Descendants(shell)
			.OfType<InspectorPanel>()
			.Single();

		NumericField positionX = Descendants(inspector)
			.OfType<NumericField>()
			.First();

		Assert.True(positionX.Focus());
		_context.TextInput("7");
		Paint(0.1);

		Assert.True(
			positionX.HasPendingTextEdit,
			"The typed text must still be pending when Play is pressed.");

		IconButton play = Descendants(shell)
			.OfType<IconButton>()
			.Single(button => button.TooltipText == "Play the scene");

		Click(play);
		Paint(0.2);

		Assert.NotNull(_session.Play);
		Assert.Equal(7f, transform.Position.X, 3);
	}

	private void Click(Widget widget)
	{
		UiPoint center = widget.GlobalVisualBounds.Center;

		_context.PointerDown(UiMouseButton.Left, center);
		_context.PointerUp(UiMouseButton.Left, center);
	}

	private void Paint(double time)
	{
		_context.BeginFrame(time, new UiSize(1440f, 900f), UiPoint.Zero);
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
