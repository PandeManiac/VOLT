using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

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

	private SceneViewport BuildViewport(out UiDrawList drawList)
	{
		SceneViewport viewport = new(_session);

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
