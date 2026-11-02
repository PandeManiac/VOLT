using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

public sealed class InspectorUndoRefreshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(), $"volt-inspector-undo-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public InspectorUndoRefreshTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "UndoRefresh");
		_session.CreateEntity("Target");
	}

	[Fact]
	public void UndoWhileNumericInspectorFieldIsFocused_RefreshesCommittedValue()
	{
		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		NumericField positionX = Descendants(inspector)
			.OfType<NumericField>()
			.First();
		Assert.True(positionX.Focus());

		positionX.Value = 12f;
		Assert.Equal(12f, _session.ActiveEntity!.Transform.Position.X);

		_session.UndoLast();

		Assert.Equal(0f, _session.ActiveEntity!.Transform.Position.X);
		Assert.Equal(0f, positionX.Value);
		Assert.True(positionX.HasFocus);
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
		}
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
