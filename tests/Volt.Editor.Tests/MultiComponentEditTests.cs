using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers a component property edit reaching every selected object that
/// has the component, as one undo step.
/// </summary>
public sealed class MultiComponentEditTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-multi-component-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public MultiComponentEditTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "MultiComponent");
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
	public void EditingIntensityChangesEverySelectedLightOnce()
	{
		SceneEntity first = _session.CreateDirectionalLight("First")!;
		SceneEntity second = _session.CreateDirectionalLight("Second")!;
		SceneEntity plain = _session.CreateEntity("Plain")!;

		_session.SelectMany([first.Id, second.Id, plain.Id], first.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		NumericField intensity = IntensityField(inspector);

		int history = _session.Undo.UndoCount;
		float originalFirst = first.GetComponent<LightData>()!.Intensity;
		float originalSecond = second.GetComponent<LightData>()!.Intensity;

		Assert.True(intensity.Focus());
		Assert.True(_context.KeyDown(UiKey.A, UiKeyModifiers.Control));
		_context.TextInput("0.5");
		Assert.True(_context.KeyDown(UiKey.Enter));

		Assert.Equal(0.5f, first.GetComponent<LightData>()!.Intensity, 3);
		Assert.Equal(0.5f, second.GetComponent<LightData>()!.Intensity, 3);
		Assert.Null(plain.GetComponent<LightData>());
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Equal(originalFirst, first.GetComponent<LightData>()!.Intensity, 3);
		Assert.Equal(originalSecond, second.GetComponent<LightData>()!.Intensity, 3);
	}

	[Fact]
	public void AnObjectWithoutTheComponentIsLeftOutOfTheEdit()
	{
		SceneEntity light = _session.CreateDirectionalLight("Light")!;
		SceneEntity other = _session.CreateEntity("Other")!;

		_session.SelectMany([light.Id, other.Id], light.Id);

		int history = _session.Undo.UndoCount;

		int changed = _session.SetSelectionComponentProperty(
			light.Id,
			light.GetComponent<LightData>()!,
			ComponentCatalog.Default.Get(LightData.ComponentTypeId).Properties
				.Single(property => property.DisplayName == "Intensity"),
			1.25f);

		Assert.Equal(1, changed);
		Assert.Equal(1.25f, light.GetComponent<LightData>()!.Intensity, 3);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
	}

	[Fact]
	public void TheSubtitleStatesWhatAGroupEditReaches()
	{
		SceneEntity light = _session.CreateDirectionalLight("Light")!;
		SceneEntity other = _session.CreateDirectionalLight("Other")!;

		_session.SelectMany([light.Id, other.Id], light.Id);

		using InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		Paint(0.0);

		Assert.Contains(
			Descendants(inspector).OfType<Label>(),
			label => label.Text.Contains(
				"edits: every object with the same component",
				StringComparison.Ordinal));
	}

	private static NumericField IntensityField(InspectorPanel inspector)
	{
		Label label = Descendants(inspector)
			.OfType<Label>()
			.Single(candidate => candidate.Text == "Intensity");

		return label.Parent!
			.Children
			.OfType<NumericField>()
			.Single();
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
