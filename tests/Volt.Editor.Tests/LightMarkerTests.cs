using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Rendering;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the Scene view's authored-light marker: the sun sits at the
/// projected light origin, the beam arrow follows the light's world +Z
/// axis, and the label is painted beside it.
/// </summary>
public sealed class LightMarkerTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-light-marker-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public LightMarkerTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Lights");
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
	public void LightMarkerAndBeam_PaintAtTheProjectedLight()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");

		// The marker is an authoring aid even when the light is not
		// selected, so the test paints it in that state.
		_session.ClearSelection();

		SceneViewport viewport = BuildViewport(out UiDrawList drawList);

		Project(viewport, light, out UiPoint center, out UiPoint tip);

		Assert.True(
			Paints(drawList, MarkerArea(center)),
			$"Nothing painted the sun marker at {center}.");

		Assert.True(
			PaintsLine(drawList, center, tip),
			$"Nothing painted a beam from {center} to {tip}.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun"),
			"The light's name must be painted beside its marker.");
	}

	[Fact]
	public void DisabledLightMarker_IsMutedAndCrossedOut()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		data.Enabled = false;
		_session.ClearSelection();

		SceneViewport viewport = BuildViewport(out UiDrawList drawList);
		UiTheme theme = _context.Theme;

		Project(viewport, light, out UiPoint center, out _);

		Assert.True(
			Paints(drawList, MarkerArea(center), theme.Palette.TextDisabled),
			"A switched-off light must keep a muted marker.");

		Assert.True(
			PaintsLine(
				drawList,
				new UiPoint(center.X - 7f, center.Y + 7f),
				new UiPoint(center.X + 7f, center.Y - 7f),
				theme.Palette.TextDisabled),
			"A switched-off light must be crossed out.");

		Assert.True(
			PaintsText(
				drawList,
				LabelArea(center),
				"Sun (off)",
				theme.Palette.TextDisabled),
			"The label must say that the light is off.");
	}

	[Fact]
	public void ZeroIntensityLightMarker_IsMutedWithoutBeingMarkedOff()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		data.Intensity = 0f;
		_session.ClearSelection();

		SceneViewport viewport = BuildViewport(out UiDrawList drawList);
		UiTheme theme = _context.Theme;

		Project(viewport, light, out UiPoint center, out UiPoint tip);

		Assert.True(
			Paints(drawList, MarkerArea(center), theme.Palette.TextDisabled),
			"A light that emits nothing must be drawn muted.");

		Assert.True(
			PaintsLine(
				drawList,
				center,
				tip,
				theme.Palette.TextDisabled.ScaleAlpha(0.9f)),
			"A zero-intensity light keeps its muted beam.");

		Assert.True(
			PaintsText(
				drawList,
				LabelArea(center),
				"Sun",
				theme.Palette.TextDisabled),
			"The light is still enabled, so its label keeps its name.");

		Assert.False(
			PaintsText(drawList, LabelArea(center), "Sun (off)"),
			"Zero intensity is not the same authored state as disabled.");
	}

	[Fact]
	public void InspectorEnabledToggle_UpdatesLightMarkerAndUndo()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		data.Color = new Vector4(1f, 0f, 0f, 1f);
		data.Intensity = 2f;

		using UiContext inspectorContext = new();
		using InspectorPanel inspector = new(_session);

		inspectorContext.Add(inspector);
		inspectorContext.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		inspectorContext.Draw();

		CheckBox toggle = Descendants(inspector)
			.OfType<CheckBox>()
			.Single(box => box.Text == nameof(LightData.Enabled));

		Assert.True(toggle.IsChecked, "The light starts enabled.");

		int history = _session.Undo.UndoCount;

		Click(inspectorContext, toggle.GlobalVisualBounds.Center);

		Assert.False(data.Enabled);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
		Assert.Equal(
			ViewportLight.Default.DiffuseColor,
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		UiTheme theme = _context.Theme;
		SceneViewport viewport = BuildViewport(out UiDrawList drawList);

		Project(viewport, light, out UiPoint center, out _);

		Assert.True(
			PaintsLine(
				drawList,
				new UiPoint(center.X - 7f, center.Y + 7f),
				new UiPoint(center.X + 7f, center.Y - 7f),
				theme.Palette.TextDisabled),
			"Toggling Enabled off must cross out the light marker.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun (off)"),
			"Toggling Enabled off must update the label immediately.");

		_session.UndoLast();

		Assert.True(data.Enabled);
		Assert.Equal(
			new Vector3(2f, 0f, 0f),
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		_context.BeginFrame(
			0.1,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		drawList = _context.Draw();

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun"),
			"Undo must restore the light's normal label.");

		Assert.False(
			PaintsText(drawList, LabelArea(center), "Sun (off)"),
			"Undo must remove the off state from the marker.");
	}

	[Fact]
	public void InspectorIntensityEdit_UpdatesLightingAndMarkerAndUndo()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		data.Intensity = 2f;

		using UiContext inspectorContext = new();
		using InspectorPanel inspector = new(_session);

		inspectorContext.Add(inspector);
		inspectorContext.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		inspectorContext.Draw();

		Label label = Descendants(inspector)
			.OfType<Label>()
			.Single(candidate => candidate.Text == "Intensity");

		NumericField field = label.Parent!
			.Children
			.OfType<NumericField>()
			.Single();

		Assert.Equal(2f, field.Value, 3);

		int history = _session.Undo.UndoCount;

		Click(inspectorContext, field.GlobalVisualBounds.Center);
		Assert.True(inspectorContext.KeyDown(
			UiKey.A,
			UiKeyModifiers.Control));

		inspectorContext.TextInput("0");

		Assert.True(inspectorContext.KeyDown(UiKey.Enter));

		Assert.Equal(0f, data.Intensity);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
		Assert.Equal(
			Vector3.Zero,
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		// The light is no longer selected, so its zero-intensity
		// marker must show the muted state seen by an author.
		_session.ClearSelection();

		UiTheme theme = _context.Theme;
		SceneViewport viewport = BuildViewport(out UiDrawList drawList);

		Project(viewport, light, out UiPoint center, out _);

		Assert.True(
			Paints(drawList, MarkerArea(center), theme.Palette.TextDisabled),
			"Setting intensity to zero must mute the marker.");

		Assert.True(
			PaintsText(
				drawList,
				LabelArea(center),
				"Sun",
				theme.Palette.TextDisabled),
			"The light is still enabled, so its label keeps its name.");

		Assert.False(
			PaintsText(drawList, LabelArea(center), "Sun (off)"),
			"Zero intensity is not the same as disabling the light.");

		_session.UndoLast();

		Assert.Equal(2f, data.Intensity, 3);
		Assert.Equal(
			new Vector3(2f, 2f, 2f),
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		_context.BeginFrame(
			0.1,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		drawList = _context.Draw();

		Assert.True(
			Paints(drawList, MarkerArea(center), UiColor.White),
			"Undo must restore the light's normal marker color.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun", UiColor.White),
			"Undo must restore the light's normal label color.");
	}

	[Fact]
	public void InspectorColorEdit_UpdatesLightingAndMarkerAndUndo()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		using UiContext inspectorContext = new();
		using InspectorPanel inspector = new(_session);

		inspectorContext.Add(inspector);
		inspectorContext.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		inspectorContext.Draw();

		ColorField color = Descendants(inspector)
			.OfType<ColorField>()
			.Single();

		TextField hex = Descendants(color)
			.OfType<TextField>()
			.Single();

		Assert.Equal("#FFFFFF", hex.Text);

		int history = _session.Undo.UndoCount;

		Click(inspectorContext, hex.GlobalVisualBounds.Center);
		Assert.True(inspectorContext.KeyDown(
			UiKey.A,
			UiKeyModifiers.Control));

		inspectorContext.TextInput("#00FF00");

		Assert.True(inspectorContext.KeyDown(UiKey.Enter));

		Assert.Equal(new Vector4(0f, 1f, 0f, 1f), data.Color);
		Assert.Equal(history + 1, _session.Undo.UndoCount);
		Assert.Equal(
			new Vector3(0f, 1f, 0f),
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		// Clear the selection so the marker uses its authored tint
		// rather than the selection accent.
		_session.ClearSelection();

		UiColor green = new(89, 255, 89);
		SceneViewport viewport = BuildViewport(out UiDrawList drawList);

		Project(viewport, light, out UiPoint center, out _);

		Assert.True(
			Paints(drawList, MarkerArea(center), green),
			"The marker must use the new light color.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun", green),
			"The label must use the new light color.");

		_session.UndoLast();

		Assert.Equal(Vector4.One, data.Color);
		Assert.Equal(
			Vector3.One,
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);

		_context.BeginFrame(
			0.1,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		drawList = _context.Draw();

		Assert.True(
			Paints(drawList, MarkerArea(center), UiColor.White),
			"Undo must restore the original marker color.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun", UiColor.White),
			"Undo must restore the original label color.");
	}

	[Fact]
	public void InspectorColorEdit_RejectsInvalidHexWithoutAnUndoStep()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		using UiContext inspectorContext = new();
		using InspectorPanel inspector = new(_session);

		inspectorContext.Add(inspector);
		inspectorContext.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		inspectorContext.Draw();

		ColorField color = Descendants(inspector)
			.OfType<ColorField>()
			.Single();

		TextField hex = Descendants(color)
			.OfType<TextField>()
			.Single();

		int history = _session.Undo.UndoCount;

		Click(inspectorContext, hex.GlobalVisualBounds.Center);
		Assert.True(inspectorContext.KeyDown(
			UiKey.A,
			UiKeyModifiers.Control));

		inspectorContext.TextInput("#GGGGGG");

		Assert.True(inspectorContext.KeyDown(UiKey.Enter));

		Assert.Equal(Vector4.One, data.Color);
		Assert.Equal(history, _session.Undo.UndoCount);
		Assert.Equal("#FFFFFF", hex.Text);
		Assert.Equal(
			Vector3.One,
			ViewportMath.ResolveLight(_session.Scene!).DiffuseColor);
	}

	[Fact]
	public void InspectorToggle_PublishesTheUpdatedLightToTheRenderer()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");
		LightData data = light.GetComponent<LightData>()!;

		data.Color = new Vector4(1f, 0f, 0f, 1f);
		data.Intensity = 2f;

		using UiContext inspectorContext = new();
		using InspectorPanel inspector = new(_session);

		inspectorContext.Add(inspector);
		inspectorContext.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		inspectorContext.Draw();

		CheckBox toggle = Descendants(inspector)
			.OfType<CheckBox>()
			.Single(box => box.Text == nameof(LightData.Enabled));

		Click(inspectorContext, toggle.GlobalVisualBounds.Center);

		RecordingViewportRenderer renderer = new();
		SceneViewport viewport = BuildViewport(out _, renderer);

		Assert.False(data.Enabled);
		Assert.True(renderer.Published);
		Assert.Equal(
			ViewportLight.Default.DiffuseColor,
			renderer.Light.DiffuseColor);

		_session.UndoLast();

		_context.BeginFrame(
			0.1,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		_context.Draw();

		Assert.True(data.Enabled);
		Assert.Equal(new Vector3(2f, 0f, 0f), renderer.Light.DiffuseColor);
	}

	[Fact]
	public void SelectedLightMarker_UsesAccentColor()
	{
		SceneEntity light = _session.CreateDirectionalLight("Sun");

		Assert.Equal(light.Id, _session.ActiveEntityId);

		SceneViewport viewport = BuildViewport(out UiDrawList drawList);
		UiTheme theme = _context.Theme;

		Project(viewport, light, out UiPoint center, out UiPoint tip);

		Assert.True(
			PaintsLine(
				drawList,
				center,
				tip,
				theme.Palette.Accent.ScaleAlpha(0.9f)),
			"A selected light must draw its beam in the accent color.");

		Assert.True(
			PaintsText(drawList, LabelArea(center), "Sun", theme.Palette.Accent),
			"A selected light's label must use the accent color.");
	}

	private SceneViewport BuildViewport(
		out UiDrawList drawList,
		IViewportRenderer? renderer = null)
	{
		SceneViewport viewport = new(_session, renderer);

		_context.Add(viewport);
		_context.BeginFrame(
			0.0,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		drawList = _context.Draw();

		return viewport;
	}

	private static void Project(
		SceneViewport viewport,
		SceneEntity light,
		out UiPoint center,
		out UiPoint tip)
	{
		UiRect bounds = viewport.GlobalVisualBounds;

		Matrix4x4 viewProjection =
			viewport.Camera.CreateView() *
			viewport.Camera.CreateProjection(
				bounds.Width / bounds.Height,
				yDown: false);

		Assert.True(
			ViewportMath.TryProject(
				viewProjection,
				light.Transform.Position,
				bounds,
				out center),
			"The light must be in front of the authoring camera.");

		Vector3 beam = Vector3.Normalize(
			Vector3.Transform(Vector3.UnitZ, light.Transform.Rotation));

		float distance = Vector3.Distance(
			viewport.Camera.Position,
			light.Transform.Position);

		float worldPerPixel = ViewportMath.WorldPerPixel(
			viewport.Camera.FieldOfView,
			bounds.Height,
			distance);

		float length = MathF.Max(0.05f, worldPerPixel * 46f);

		Assert.True(
			ViewportMath.TryProject(
				viewProjection,
				light.Transform.Position + (beam * length),
				bounds,
				out tip),
			"The beam arrow must end inside the viewport.");
	}

	private static void Click(UiContext context, UiPoint point)
	{
		context.PointerDown(UiMouseButton.Left, point);
		context.PointerUp(UiMouseButton.Left, point);
	}

	private static IEnumerable<Widget> Descendants(Widget widget)
	{
		foreach (Widget child in widget.Children)
		{
			yield return child;

			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}

	private static UiRect MarkerArea(UiPoint center)
	{
		return new UiRect(center.X - 10f, center.Y - 10f, 20f, 20f);
	}

	private static UiRect LabelArea(UiPoint center)
	{
		return new UiRect(center.X, center.Y - 40f, 180f, 40f);
	}

	private static bool Paints(
		UiDrawList drawList,
		UiRect area,
		UiColor? color = null)
	{
		if (area.IsEmpty)
		{
			return false;
		}

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (color is not null && command.Color != color)
			{
				continue;
			}

			UiRect rect = Extent(command);

			if (rect.Intersect(area).IsEmpty)
			{
				continue;
			}

			if (rect.Width <= area.Width + 1f &&
				rect.Height <= area.Height + 1f)
			{
				return true;
			}
		}

		return false;
	}

	private static bool PaintsLine(
		UiDrawList drawList,
		UiPoint start,
		UiPoint end,
		UiColor? color = null)
	{
		const float tolerance = 1.5f;

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind != UiDrawCommandKind.Line)
			{
				continue;
			}

			if (color is not null && command.Color != color)
			{
				continue;
			}

			if (MathF.Abs(command.Start.X - start.X) <= tolerance &&
				MathF.Abs(command.Start.Y - start.Y) <= tolerance &&
				MathF.Abs(command.End.X - end.X) <= tolerance &&
				MathF.Abs(command.End.Y - end.Y) <= tolerance)
			{
				return true;
			}
		}

		return false;
	}

	private static bool PaintsText(
		UiDrawList drawList,
		UiRect area,
		string source,
		UiColor? color = null)
	{
		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind != UiDrawCommandKind.Text ||
				command.Text?.Source != source)
			{
				continue;
			}

			if (color is not null && command.Color != color)
			{
				continue;
			}

			if (!command.Rect.Intersect(area).IsEmpty)
			{
				return true;
			}
		}

		return false;
	}

	private sealed class RecordingViewportRenderer : IViewportRenderer
	{
		public int FrameSlot => 0;

		public bool Published { get; private set; }

		public ViewportLight Light { get; private set; } =
			ViewportLight.Default;

		public UiTextureId? PrepareTexture(ViewportKind kind, int slot)
		{
			return null;
		}

		public void PublishRequest(ViewportRenderRequest request)
		{
			Published = true;
			Light = request.Light;
		}

		public void ClearRequest(ViewportKind kind)
		{
			Published = false;
		}
	}

	private static UiRect Extent(UiDrawCommand command)
	{
		if (command.Kind != UiDrawCommandKind.Line)
		{
			return command.Rect;
		}

		return UiRect.FromLtrb(
			MathF.Min(command.Start.X, command.End.X) - command.Thickness,
			MathF.Min(command.Start.Y, command.End.Y) - command.Thickness,
			MathF.Max(command.Start.X, command.End.X) + command.Thickness,
			MathF.Max(command.Start.Y, command.End.Y) + command.Thickness);
	}
}
