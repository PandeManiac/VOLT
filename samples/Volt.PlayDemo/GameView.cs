using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.PlayDemo;

/// <summary>
/// Paints the running simulation into the UI draw list.
/// </summary>
/// <remarks>
/// The widget reads runtime positions from the ECS world and projects
/// them through a fixed camera. It never touches the authored scene,
/// so a stopped session leaves the document untouched. A real game
/// would replace the projection and markers with mesh rendering while
/// keeping the same session lifetime.
/// </remarks>
internal sealed class GameView : Widget
{
	private const float GridStep = 2f;
	private const int GridExtent = 10;
	private const int MarkerSize = 12;

	private readonly PlaySession _play;

	public GameView(PlaySession play)
	{
		ArgumentNullException.ThrowIfNull(play);

		_play = play;
		ClipToBounds = true;
		IsFocusable = false;
		IsHitTestVisible = false;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: 0f,
			float.IsFinite(contentAvailable.Height)
				? contentAvailable.Height
				: 0f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiThemePalette palette = new(
			Context?.Theme.Palette.WindowBackground ?? UiColor.Black,
			Context?.Theme.Palette.Border ?? UiColor.White,
			Context?.Theme.Palette.Accent ?? UiColor.White,
			Context?.Theme.Palette.Selection ?? UiColor.White,
			Context?.Theme.Palette.TextMuted ?? UiColor.White);

		// The draw list is in root coordinates.
		drawList.AddRect(GlobalBounds, palette.Background);

		UiRect area = GlobalContentBounds;

		if (area.IsEmpty)
		{
			return;
		}

		Matrix4x4 viewProjection = CreateViewProjection(area);

		DrawGrid(drawList, area, viewProjection, palette);

		RuntimeScene? runtime = _play.Runtime;

		if (runtime is null)
		{
			return;
		}

		UiFont? font = Context?.Theme.Fonts.Small;

		if (font is null)
		{
			return;
		}

		foreach (RuntimeEntityInfo info in runtime.Entities)
		{
			if (!runtime.World.HasComponent<PositionComponent>(info.Entity))
			{
				continue;
			}

			Vector3 position = runtime.World
				.GetComponent<PositionComponent>(info.Entity)
				.Value;

			if (!TryProject(area, viewProjection, position, out UiPoint point))
			{
				continue;
			}

			bool isPlayer = info.Name == "Player";

			UiColor color = isPlayer
				? palette.Accent
				: palette.Selection;

			drawList.AddRect(
				new UiRect(
					point.X - (MarkerSize * 0.5f),
					point.Y - (MarkerSize * 0.5f),
					MarkerSize,
					MarkerSize),
				color);

			drawList.AddBorder(
				new UiRect(
					point.X - (MarkerSize * 0.5f),
					point.Y - (MarkerSize * 0.5f),
					MarkerSize,
					MarkerSize),
				palette.TextMuted,
				1f);

			drawList.AddLine(
				new UiPoint(point.X, point.Y + (MarkerSize * 0.5f)),
				new UiPoint(point.X, point.Y + (MarkerSize * 0.5f) + 8f),
				1f,
				palette.Border);

			UiTextLayoutResult label = UiTextLayout.Create(
				info.Name,
				font,
				area.Width);

			drawList.AddText(
				label,
				new UiPoint(
					point.X - (label.Width * 0.5f),
					point.Y + (MarkerSize * 0.5f) + 9f),
				palette.TextMuted);
		}
	}

	private static void DrawGrid(
		UiDrawList drawList,
		UiRect area,
		Matrix4x4 viewProjection,
		UiThemePalette palette)
	{
		for (int index = -GridExtent; index <= GridExtent; index++)
		{
			float offset = index * GridStep;

			DrawSegment(
				drawList,
				area,
				viewProjection,
				new Vector3(-GridExtent * GridStep, 0f, offset),
				new Vector3(GridExtent * GridStep, 0f, offset),
				index == 0 ? palette.Accent : palette.Border);

			DrawSegment(
				drawList,
				area,
				viewProjection,
				new Vector3(offset, 0f, -GridExtent * GridStep),
				new Vector3(offset, 0f, GridExtent * GridStep),
				index == 0 ? palette.Selection : palette.Border);
		}
	}

	private static void DrawSegment(
		UiDrawList drawList,
		UiRect area,
		Matrix4x4 viewProjection,
		Vector3 start,
		Vector3 end,
		UiColor color)
	{
		if (!TryProject(area, viewProjection, start, out UiPoint a) ||
			!TryProject(area, viewProjection, end, out UiPoint b))
		{
			return;
		}

		drawList.AddLine(a, b, 1f, color);
	}

	private static Matrix4x4 CreateViewProjection(UiRect area)
	{
		Vector3 eye = new(0f, 7f, 13f);
		Vector3 target = new(0f, 0.5f, 0f);

		float aspect = area.Width > 0f && area.Height > 0f
			? area.Width / area.Height
			: 1f;

		return Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY) *
			Matrix4x4.CreatePerspectiveFieldOfView(
				MathF.PI / 3.4f,
				MathF.Max(0.01f, aspect),
				0.1f,
				200f);
	}

	private static bool TryProject(
		UiRect area,
		Matrix4x4 viewProjection,
		Vector3 world,
		out UiPoint point)
	{
		Vector4 clip = Vector4.Transform(
			new Vector4(world, 1f),
			viewProjection);

		if (clip.W <= 0.0001f)
		{
			point = default;
			return false;
		}

		float normalizedX = clip.X / clip.W;
		float normalizedY = clip.Y / clip.W;

		point = new UiPoint(
			area.X + ((normalizedX * 0.5f) + 0.5f) * area.Width,
			area.Y + (0.5f - (normalizedY * 0.5f)) * area.Height);

		return true;
	}

	private readonly record struct UiThemePalette(
		UiColor Background,
		UiColor Border,
		UiColor Accent,
		UiColor Selection,
		UiColor TextMuted);
}
