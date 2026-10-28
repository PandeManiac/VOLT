using System.Numerics;
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Editor.Viewport;

/// <summary>
/// Identifies one target of the orientation compass.
/// </summary>
internal enum ViewportCompassControl
{
	/// <summary>No target.</summary>
	None = 0,

	/// <summary>Looks at the scene from the positive X side.</summary>
	ViewFromX,

	/// <summary>Looks at the scene from the negative X side.</summary>
	ViewFromNegX,

	/// <summary>Looks at the scene from above.</summary>
	ViewFromY,

	/// <summary>Looks at the scene from below.</summary>
	ViewFromNegY,

	/// <summary>Looks at the scene from the positive Z side.</summary>
	ViewFromZ,

	/// <summary>Looks at the scene from the negative Z side.</summary>
	ViewFromNegZ,

	/// <summary>Restores the default three-quarter angle.</summary>
	Home
}

/// <summary>
/// One axis marker of the orientation compass.
/// </summary>
/// <param name="Control">The view this marker snaps to.</param>
/// <param name="Side">
/// The side of the scene the camera moves to, as a unit direction.
/// </param>
/// <param name="Axis">The letter drawn beside the marker.</param>
/// <param name="Label">The name shown in the marker's tooltip.</param>
/// <param name="Position">The marker's centre in viewport pixels.</param>
/// <param name="TowardViewer">
/// True when the marker points at the viewer, which decides whether it
/// is drawn in front of the compass or behind it.
/// </param>
/// <param name="Positive">
/// True for the positive side of an axis, which is the one drawn in
/// the axis colour rather than muted.
/// </param>
/// <param name="Radius">The marker's radius in pixels.</param>
internal readonly record struct CompassTip(
	ViewportCompassControl Control,
	Vector3 Side,
	string Axis,
	string Label,
	UiPoint Position,
	bool TowardViewer,
	bool Positive,
	float Radius);

/// <summary>
/// The orientation compass in the corner of the scene view.
/// </summary>
/// <remarks>
/// <para>
/// The compass projects the six world axis directions through the
/// editor camera's basis, so it always describes where the camera
/// looks from: markers pointing at the viewer are drawn in front and
/// in full colour, markers on the far side sit behind and dimmed. A
/// click snaps the camera to that side of the current pivot while
/// keeping the distance it already had, which is what an author
/// expects from a view widget. The middle of the widget is the home
/// target.
/// </para>
/// <para>
/// All of it is plain geometry: no widget tree, no GPU resource, and
/// no state beyond one frame's worth of markers, so a marker can be
/// picked and the resulting camera asserted without a window.
/// </para>
/// </remarks>
internal sealed class ViewportCompass
{
	/// <summary>
	/// The distance the compass keeps from the viewport corner.
	/// </summary>
	public const float Margin = 10f;

	/// <summary>
	/// The half-size of the home target at the compass centre.
	/// </summary>
	public const float HomeRadius = 9f;

	private readonly List<CompassTip> _tips = [];

	/// <summary>
	/// Gets whether the compass has room to draw.
	/// </summary>
	public bool IsVisible { get; private set; }

	/// <summary>
	/// Gets the compass centre in viewport pixels.
	/// </summary>
	public UiPoint Center { get; private set; }

	/// <summary>
	/// Gets the compass radius in pixels.
	/// </summary>
	public float Radius { get; private set; }

	/// <summary>
	/// Gets the markers built by the last <see cref="Build"/> call.
	/// </summary>
	public IReadOnlyList<CompassTip> Tips => _tips;

	/// <summary>
	/// Projects the world axes through a camera.
	/// </summary>
	/// <param name="bounds">The viewport rectangle in UI units.</param>
	/// <param name="camera">The camera whose orientation is shown.</param>
	/// <param name="radius">The compass radius in pixels.</param>
	/// <remarks>
	/// The compass hides itself when the viewport is too small to hold
	/// it beside the floating chrome, rather than covering either the
	/// chrome or the scene.
	/// </remarks>
	public void Build(UiRect bounds, EditorCamera camera, float radius)
	{
		ArgumentNullException.ThrowIfNull(camera);

		_tips.Clear();
		IsVisible = false;

		if (radius <= 0f || !float.IsFinite(radius))
		{
			return;
		}

		float needed = radius * 2f;

		if (bounds.Width < needed + (Margin * 2f) + 240f ||
			bounds.Height < needed + (Margin * 2f) + 60f)
		{
			return;
		}

		Center = new UiPoint(
			bounds.Right - Margin - radius,
			bounds.Y + Margin + radius);

		Radius = radius;
		IsVisible = true;

		Add(camera, Vector3.UnitX, ViewportCompassControl.ViewFromX, "X", radius);
		Add(camera, -Vector3.UnitX, ViewportCompassControl.ViewFromNegX, "X", radius);
		Add(camera, Vector3.UnitY, ViewportCompassControl.ViewFromY, "Y", radius);
		Add(camera, -Vector3.UnitY, ViewportCompassControl.ViewFromNegY, "Y", radius);
		Add(camera, Vector3.UnitZ, ViewportCompassControl.ViewFromZ, "Z", radius);
		Add(camera, -Vector3.UnitZ, ViewportCompassControl.ViewFromNegZ, "Z", radius);
	}

	/// <summary>
	/// Finds the marker under a pointer.
	/// </summary>
	/// <remarks>
	/// Markers facing the viewer are tested first, so the marker the
	/// user can see is the one they hit.
	/// </remarks>
	public ViewportCompassControl Pick(UiPoint pointer)
	{
		if (!IsVisible)
		{
			return ViewportCompassControl.None;
		}

		float toCenter = Distance(pointer, Center);

		// The middle of the compass is the home target, and it always
		// wins inside its own footprint: the marker that lands there
		// belongs to the view the camera already has, so snapping to it
		// would do nothing, while home is always useful.
		if (toCenter <= HomeRadius - 2f)
		{
			return ViewportCompassControl.Home;
		}

		ViewportCompassControl hit = ViewportCompassControl.None;
		float bestDistance = float.MaxValue;

		for (int pass = 0; pass < 2; pass++)
		{
			bool front = pass == 0;

			foreach (CompassTip tip in _tips)
			{
				if (tip.TowardViewer != front)
				{
					continue;
				}

				float distance = Distance(pointer, tip.Position);

				if (distance <= tip.Radius + 6f && distance < bestDistance)
				{
					bestDistance = distance;
					hit = tip.Control;
				}
			}

			if (hit != ViewportCompassControl.None && front)
			{
				return hit;
			}
		}

		if (hit != ViewportCompassControl.None)
		{
			return hit;
		}

		return toCenter <= HomeRadius + 4f
			? ViewportCompassControl.Home
			: ViewportCompassControl.None;
	}

	/// <summary>
	/// Gets the tooltip text of a target.
	/// </summary>
	public string LabelOf(ViewportCompassControl control)
	{
		if (control == ViewportCompassControl.Home)
		{
			return "Default view (three-quarter angle)";
		}

		foreach (CompassTip tip in _tips)
		{
			if (tip.Control == control)
			{
				return tip.Label;
			}
		}

		return string.Empty;
	}

	/// <summary>
	/// Gets the rectangle a tooltip hangs from.
	/// </summary>
	public UiRect BoundsOf(ViewportCompassControl control)
	{
		if (control == ViewportCompassControl.Home)
		{
			return new UiRect(
				Center.X - HomeRadius,
				Center.Y - HomeRadius,
				HomeRadius * 2f,
				HomeRadius * 2f);
		}

		foreach (CompassTip tip in _tips)
		{
			if (tip.Control == control)
			{
				return new UiRect(
					tip.Position.X - tip.Radius,
					tip.Position.Y - tip.Radius,
					tip.Radius * 2f,
					tip.Radius * 2f);
			}
		}

		return UiRect.Empty;
	}

	/// <summary>
	/// Paints the compass.
	/// </summary>
	public void Render(
		UiDrawList drawList,
		UiTheme theme,
		ViewportCompassControl hovered)
	{
		ArgumentNullException.ThrowIfNull(drawList);
		ArgumentNullException.ThrowIfNull(theme);

		if (!IsVisible)
		{
			return;
		}

		UiPalette palette = theme.Palette;

		UiRect backing = new(
			Center.X - Radius,
			Center.Y - Radius,
			Radius * 2f,
			Radius * 2f);

		drawList.AddRoundedRect(backing, Radius, palette.Overlay);
		drawList.AddBorder(
			backing,
			palette.OverlayBorder,
			theme.Metrics.BorderWidth,
			Radius);

		// Markers on the far side are painted first and dimmed, which
		// is what makes the compass read as one solid object.
		for (int pass = 0; pass < 2; pass++)
		{
			bool front = pass == 1;

			foreach (CompassTip tip in _tips)
			{
				if (tip.TowardViewer != front)
				{
					continue;
				}

				UiColor color = AxisColor(tip, palette);

				drawList.AddLine(
					Center,
					tip.Position,
					front ? 1.6f : 1.2f,
					color.ScaleAlpha(front ? 0.7f : 0.35f));

				UiRect marker = new(
					tip.Position.X - tip.Radius,
					tip.Position.Y - tip.Radius,
					tip.Radius * 2f,
					tip.Radius * 2f);

				drawList.AddRoundedRect(
					marker,
					tip.Radius,
					color.ScaleAlpha(front ? 1f : 0.5f));

				if (hovered == tip.Control)
				{
					drawList.AddBorder(
						marker.Inset(-3f),
						palette.Accent,
						1.6f,
						tip.Radius + 3f);
				}

				DrawAxisLetter(drawList, theme, tip, color);
			}
		}

		UiRect home = new(
			Center.X - 5f,
			Center.Y - 5f,
			10f,
			10f);

		drawList.AddRoundedRect(
			home,
			2f,
			hovered == ViewportCompassControl.Home
				? palette.Accent
				: palette.TextMuted);

		if (hovered == ViewportCompassControl.Home)
		{
			drawList.AddBorder(
				home.Inset(-3f),
				palette.Accent,
				1.6f,
				5f);
		}
	}

	/// <summary>
	/// Draws the axis letter just outside a marker.
	/// </summary>
	private void DrawAxisLetter(
		UiDrawList drawList,
		UiTheme theme,
		CompassTip tip,
		UiColor color)
	{
		float dx = tip.Position.X - Center.X;
		float dy = tip.Position.Y - Center.Y;
		float length = MathF.Sqrt((dx * dx) + (dy * dy));

		if (length < 0.001f)
		{
			return;
		}

		float offset = tip.Radius + 9f;
		float x = tip.Position.X + (dx / length * offset);
		float y = tip.Position.Y + (dy / length * offset);

		UiTextLayoutResult layout = UiTextLayout.Create(
			tip.Axis,
			theme.Fonts.MonoSmall,
			24f,
			UiTextWrap.NoWrap,
			UiTextAlignment.Center);

		drawList.AddText(
			layout,
			new UiPoint(
				x - (layout.Width * 0.5f),
				y - (layout.Height * 0.5f)),
			color.ScaleAlpha(tip.TowardViewer ? 1f : 0.6f));
	}

	private static UiColor AxisColor(CompassTip tip, UiPalette palette)
	{
		if (!tip.Positive)
		{
			return palette.TextMuted;
		}

		return tip.Axis switch
		{
			"X" => palette.AxisX,
			"Y" => palette.AxisY,
			"Z" => palette.AxisZ,
			_ => palette.TextMuted
		};
	}

	private void Add(
		EditorCamera camera,
		Vector3 side,
		ViewportCompassControl control,
		string axis,
		float radius)
	{
		Vector3 direction = Vector3.Normalize(side);

		float x = Vector3.Dot(direction, camera.Right);
		float y = -Vector3.Dot(direction, camera.Up);
		float depth = Vector3.Dot(direction, camera.Forward);

		bool towardViewer = depth < 0f;
		bool positive = side.X > 0f || side.Y > 0f || side.Z > 0f;

		_tips.Add(new CompassTip(
			control,
			direction,
			axis,
			Label(control),
			new UiPoint(
				Center.X + (x * radius),
				Center.Y + (y * radius)),
			towardViewer,
			positive,
			towardViewer ? radius * 0.155f : radius * 0.1f));
	}

	private static string Label(ViewportCompassControl control)
	{
		return control switch
		{
			ViewportCompassControl.ViewFromX => "View from X",
			ViewportCompassControl.ViewFromNegX => "View from -X",
			ViewportCompassControl.ViewFromY => "View from Y",
			ViewportCompassControl.ViewFromNegY => "View from -Y",
			ViewportCompassControl.ViewFromZ => "View from Z",
			ViewportCompassControl.ViewFromNegZ => "View from -Z",
			_ => "Default view"
		};
	}

	private static float Distance(UiPoint left, UiPoint right)
	{
		float dx = right.X - left.X;
		float dy = right.Y - left.Y;

		return MathF.Sqrt((dx * dx) + (dy * dy));
	}
}
