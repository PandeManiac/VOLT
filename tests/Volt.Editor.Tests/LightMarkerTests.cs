using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Rendering;

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
				out UiPoint center),
			"The light must be in front of the authoring camera.");

		UiRect marker = new(center.X - 10f, center.Y - 10f, 20f, 20f);

		Assert.True(
			Paints(drawList, marker),
			$"Nothing painted the sun marker at {center}.");

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
				out UiPoint tip),
			"The beam arrow must end inside the viewport.");

		Assert.True(
			PaintsBeam(drawList, center, tip),
			$"Nothing painted a beam from {center} to {tip}.");

		Assert.True(
			PaintsText(
				drawList,
				new UiRect(center.X, center.Y - 40f, 180f, 40f)),
			"The light's name must be painted beside its marker.");
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

	private static bool Paints(UiDrawList drawList, UiRect area)
	{
		if (area.IsEmpty)
		{
			return false;
		}

		for (int index = 0; index < drawList.Count; index++)
		{
			UiRect rect = Extent(drawList.GetCommand(index));

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

	private static bool PaintsBeam(
		UiDrawList drawList,
		UiPoint start,
		UiPoint end)
	{
		const float tolerance = 1.5f;

		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind != UiDrawCommandKind.Line)
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

	private static bool PaintsText(UiDrawList drawList, UiRect area)
	{
		for (int index = 0; index < drawList.Count; index++)
		{
			UiDrawCommand command = drawList.GetCommand(index);

			if (command.Kind == UiDrawCommandKind.Text &&
				!command.Rect.Intersect(area).IsEmpty)
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
