namespace Volt.Ui.Rendering;

/// <summary>
/// Paints <see cref="UiIcon"/> geometry into a draw list.
/// </summary>
/// <remarks>
/// <para>
/// Icon geometry is authored on a 24-unit square and scaled to the
/// requested bounds, so one definition serves a 12-unit toolbar glyph
/// and a 48-unit empty-state illustration. Fills are scanline
/// converted from polygons; outlines are polylines. Everything goes
/// through the normal draw list, which means icons inherit clipping,
/// batching, and theme tinting.
/// </para>
/// <para>
/// The painter allocates nothing: polygon data is passed as spans and
/// scanline scratch space is a fixed stack buffer.
/// </para>
/// </remarks>
public static class UiIconPainter
{
	private const float Grid = 24f;

	/// <summary>
	/// Draws an icon centered in the supplied square bounds.
	/// </summary>
	public static void Draw(
		UiDrawList drawList,
		UiIcon icon,
		UiRect bounds,
		UiColor color,
		float thickness = 1.5f)
	{
		ArgumentNullException.ThrowIfNull(drawList);

		if (icon == UiIcon.None || color.A == 0)
		{
			return;
		}

		float size = MathF.Min(bounds.Width, bounds.Height);

		if (size <= 0.5f)
		{
			return;
		}

		IconCanvas canvas = new(
			drawList,
			bounds.X + ((bounds.Width - size) * 0.5f),
			bounds.Y + ((bounds.Height - size) * 0.5f),
			size / Grid,
			color,
			thickness);

		DrawCore(ref canvas, icon);
	}

	private static void DrawCore(ref IconCanvas c, UiIcon icon)
	{
		switch (icon)
		{
			case UiIcon.Select:
				c.Fill([(7f, 3f), (7f, 20.5f), (11.3f, 16.2f), (14.4f, 21.5f), (17.1f, 20.1f), (14f, 14.9f), (19.6f, 14.2f)]);
				break;

			case UiIcon.Move:
				c.Line(12f, 4.5f, 12f, 19.5f);
				c.Line(4.5f, 12f, 19.5f, 12f);
				c.Fill([(12f, 2f), (9f, 6.2f), (15f, 6.2f)]);
				c.Fill([(12f, 22f), (9f, 17.8f), (15f, 17.8f)]);
				c.Fill([(2f, 12f), (6.2f, 9f), (6.2f, 15f)]);
				c.Fill([(22f, 12f), (17.8f, 9f), (17.8f, 15f)]);
				break;

			case UiIcon.Rotate:
				c.Arc(12f, 12.5f, 7.4f, -55f, 235f);
				c.Fill([(18.6f, 3.4f), (13.4f, 5.1f), (17.5f, 9f)]);
				c.Disc(12f, 12.5f, 1.6f);
				break;

			case UiIcon.Scale:
				c.Frame(3.5f, 13.5f, 7f, 7f);
				c.Line(11.5f, 12.5f, 18.2f, 5.8f);
				c.Fill([(21f, 2.5f), (14.6f, 4f), (19.6f, 8.9f)]);
				c.Frame(12.5f, 12.5f, 5.5f, 5.5f);
				break;

			case UiIcon.Play:
				c.Fill([(7f, 4f), (20f, 12f), (7f, 20f)]);
				break;

			case UiIcon.Pause:
				c.Box(6.8f, 4f, 4.2f, 16f);
				c.Box(13.6f, 4f, 4.2f, 16f);
				break;

			case UiIcon.Step:
				c.Fill([(4.5f, 4f), (15f, 12f), (4.5f, 20f)]);
				c.Box(16.4f, 4f, 3.4f, 16f);
				break;

			case UiIcon.Stop:
				c.Box(6f, 6f, 12f, 12f);
				break;

			case UiIcon.Sphere:
				c.Ring(12f, 12f, 8.4f);
				c.Ellipse(12f, 12f, 3.7f, 8.4f);
				c.Line(3.9f, 12f, 20.1f, 12f);
				break;

			case UiIcon.Cube:
				c.Stroke(true, [(12f, 2.5f), (20.5f, 7f), (20.5f, 17f), (12f, 21.5f), (3.5f, 17f), (3.5f, 7f)]);
				c.Stroke(true, [(3.5f, 7f), (12f, 11.5f), (20.5f, 7f)]);
				c.Line(12f, 11.5f, 12f, 21.5f);
				break;

			case UiIcon.Plane:
				c.Stroke(true, [(2.5f, 15f), (12f, 9.5f), (21.5f, 15f), (12f, 20.5f)]);
				c.Line(7.5f, 17.3f, 16.5f, 12.4f);
				c.Line(9f, 12.4f, 15f, 17.3f);
				break;

			case UiIcon.Camera:
				c.Frame(2.8f, 7.5f, 18.4f, 12f, 2f);
				c.Ring(12f, 13.5f, 3.6f);
				c.Box(8.4f, 5f, 7.2f, 2.6f);
				break;

			case UiIcon.Light:
				c.Ring(12f, 10.5f, 3.9f);
				c.Line(12f, 2.2f, 12f, 4.2f);
				c.Line(12f, 16.8f, 12f, 18.8f);
				c.Line(4.4f, 10.5f, 2.4f, 10.5f);
				c.Line(21.6f, 10.5f, 19.6f, 10.5f);
				c.Line(6.6f, 5.1f, 5.2f, 3.7f);
				c.Line(17.4f, 5.1f, 18.8f, 3.7f);
				c.Line(6.6f, 15.9f, 5.2f, 17.3f);
				c.Line(17.4f, 15.9f, 18.8f, 17.3f);
				break;

			case UiIcon.Folder:
			case UiIcon.Project:
				c.Stroke(true, [(2.8f, 18.6f), (2.8f, 5.6f), (9.2f, 5.6f), (11.5f, 8.8f), (21.2f, 8.8f), (21.2f, 18.6f)]);
				c.Line(2.8f, 8.8f, 21.2f, 8.8f);
				break;

			case UiIcon.Scene:
				c.Frame(2.8f, 4.5f, 18.4f, 15f, 2f);
				c.Fill([(2.8f, 19.5f), (10f, 10f), (15f, 15.5f), (18f, 12.5f), (21.2f, 15f), (21.2f, 19.5f)]);
				c.Disc(17.2f, 8.4f, 1.5f);
				break;

			case UiIcon.Script:
				c.Stroke(true, [(5.6f, 2.6f), (14.2f, 2.6f), (18.4f, 6.8f), (18.4f, 21.4f), (5.6f, 21.4f)]);
				c.Stroke(false, [(14.2f, 2.6f), (14.2f, 6.8f), (18.4f, 6.8f)]);
				c.Line(8.6f, 11.4f, 15.4f, 11.4f);
				c.Line(8.6f, 14.6f, 15.4f, 14.6f);
				c.Line(8.6f, 17.8f, 12.8f, 17.8f);
				break;

			case UiIcon.Texture:
				c.Frame(2.8f, 4.5f, 18.4f, 15f, 2f);
				c.Fill([(5.8f, 17.8f), (10.5f, 9.6f), (15.2f, 17.8f)]);
				c.Disc(16.6f, 8.4f, 1.7f);
				break;

			case UiIcon.Material:
				c.Ring(12f, 12f, 8.4f);
				c.Arc(12f, 12f, 8.4f, 200f, 260f, 1.8f);
				c.Disc(8.8f, 8.8f, 2.2f);
				break;

			case UiIcon.Mesh:
				c.Stroke(true, [(12f, 3f), (21.4f, 20.4f), (2.6f, 20.4f)]);
				c.Line(7.3f, 11.7f, 16.7f, 11.7f);
				c.Line(7.3f, 11.7f, 16.3f, 20.4f);
				c.Line(16.7f, 11.7f, 7.7f, 20.4f);
				break;

			case UiIcon.Search:
				c.Ring(10.4f, 10.4f, 6.4f, 1.6f);
				c.Line(15.2f, 15.2f, 21.2f, 21.2f, 2.1f);
				break;

			case UiIcon.Plus:
				c.Line(12f, 3.5f, 12f, 20.5f, 1.8f);
				c.Line(3.5f, 12f, 20.5f, 12f, 1.8f);
				break;

			case UiIcon.Minus:
				c.Line(3.5f, 12f, 20.5f, 12f, 1.8f);
				break;

			case UiIcon.Duplicate:
				c.Frame(3f, 7.4f, 11f, 13f, 1.6f);
				c.Frame(10f, 3f, 11f, 13f, 1.6f);
				break;

			case UiIcon.Trash:
				c.Line(3.4f, 6.4f, 20.6f, 6.4f, 1.6f);
				c.Stroke(false, [(8.6f, 6.4f), (8.6f, 3.4f), (15.4f, 3.4f), (15.4f, 6.4f)]);
				c.Stroke(true, [(5.8f, 6.4f), (18.2f, 6.4f), (16.6f, 21f), (7.4f, 21f)]);
				c.Line(10.2f, 10f, 10.2f, 17.6f);
				c.Line(13.8f, 10f, 13.8f, 17.6f);
				break;

			case UiIcon.Refresh:
				c.Arc(12f, 12f, 7.4f, -40f, 215f);
				c.Fill([(20.4f, 3.2f), (14.4f, 4.6f), (18.8f, 9.2f)]);
				break;

			case UiIcon.Check:
				c.Stroke(false, [(4.4f, 12.6f), (9.6f, 17.8f), (19.8f, 6.2f)], 2.1f);
				break;

			case UiIcon.Close:
				c.Line(6.2f, 6.2f, 17.8f, 17.8f, 1.8f);
				c.Line(17.8f, 6.2f, 6.2f, 17.8f, 1.8f);
				break;

			case UiIcon.ChevronRight:
				c.Stroke(false, [(9f, 5f), (16f, 12f), (9f, 19f)], 1.7f);
				break;

			case UiIcon.ChevronDown:
				c.Stroke(false, [(5f, 9f), (12f, 16f), (19f, 9f)], 1.7f);
				break;

			case UiIcon.Grid:
				c.Frame(3f, 3f, 18f, 18f, 1.6f);
				c.Line(12f, 3f, 12f, 21f);
				c.Line(3f, 12f, 21f, 12f);
				break;

			case UiIcon.Frame:
				c.Stroke(false, [(3.4f, 8.6f), (3.4f, 3.4f), (8.6f, 3.4f)]);
				c.Stroke(false, [(15.4f, 3.4f), (20.6f, 3.4f), (20.6f, 8.6f)]);
				c.Stroke(false, [(20.6f, 15.4f), (20.6f, 20.6f), (15.4f, 20.6f)]);
				c.Stroke(false, [(8.6f, 20.6f), (3.4f, 20.6f), (3.4f, 15.4f)]);
				c.Disc(12f, 12f, 2.1f);
				break;

			case UiIcon.Snap:
				c.Stroke(false, [(5.2f, 3.4f), (5.2f, 12.4f)]);
				c.Arc(12f, 12.4f, 6.8f, 0f, 180f);
				c.Stroke(false, [(18.8f, 12.4f), (18.8f, 3.4f)]);
				c.Box(3.5f, 3f, 3.4f, 3.4f);
				c.Box(17.1f, 3f, 3.4f, 3.4f);
				break;

			case UiIcon.World:
				c.Ring(12f, 12f, 8.4f);
				c.Ellipse(12f, 12f, 3.6f, 8.4f);
				c.Line(3.6f, 12f, 20.4f, 12f);
				break;

			case UiIcon.Local:
				c.Stroke(true, [(12f, 5f), (18.4f, 8.4f), (18.4f, 15.2f), (12f, 18.6f), (5.6f, 15.2f), (5.6f, 8.4f)]);
				c.Stroke(true, [(5.6f, 8.4f), (12f, 11.8f), (18.4f, 8.4f)]);
				c.Line(12f, 11.8f, 12f, 18.6f);
				break;

			case UiIcon.Settings:
				c.Ring(12f, 12f, 5.2f);
				for (int index = 0; index < 8; index++)
				{
					float angle = index * MathF.PI / 4f;
					float cos = MathF.Cos(angle);
					float sin = MathF.Sin(angle);

					c.Line(
						12f + (cos * 7f),
						12f + (sin * 7f),
						12f + (cos * 9.6f),
						12f + (sin * 9.6f),
						2.3f);
				}

				break;

			case UiIcon.Undo:
				c.Arc(12.6f, 13f, 6.6f, 200f, 340f);
				c.Fill([(2.6f, 8.4f), (3.4f, 15.6f), (9.4f, 11.6f)]);
				break;

			case UiIcon.Redo:
				c.Arc(11.4f, 13f, 6.6f, -20f, 120f);
				c.Fill([(21.4f, 8.4f), (20.6f, 15.6f), (14.6f, 11.6f)]);
				break;

			case UiIcon.Save:
				c.Frame(3.6f, 3.6f, 16.8f, 16.8f, 1.6f);
				c.Box(8f, 3.6f, 8f, 5.4f);
				c.Frame(6.8f, 12.8f, 10.4f, 7.6f, 1.6f);
				break;

			case UiIcon.Warning:
				c.Stroke(true, [(12f, 3f), (21.8f, 20.6f), (2.2f, 20.6f)]);
				c.Line(12f, 9f, 12f, 15f, 1.9f);
				c.Disc(12f, 17.8f, 1.2f);
				break;

			case UiIcon.Error:
				c.Ring(12f, 12f, 8.6f);
				c.Line(9f, 9f, 15f, 15f, 1.8f);
				c.Line(15f, 9f, 9f, 15f, 1.8f);
				break;

			case UiIcon.Info:
				c.Ring(12f, 12f, 8.6f);
				c.Line(12f, 11f, 12f, 17f, 1.8f);
				c.Disc(12f, 7.6f, 1.2f);
				break;

			case UiIcon.Layers:
				c.Stroke(true, [(12f, 2.6f), (20.6f, 7.2f), (12f, 11.8f), (3.4f, 7.2f)]);
				c.Stroke(false, [(3.4f, 12.2f), (12f, 16.8f), (20.6f, 12.2f)]);
				c.Stroke(false, [(3.4f, 16.6f), (12f, 21.2f), (20.6f, 16.6f)]);
				break;

			case UiIcon.ListView:
				for (int row = 0; row < 3; row++)
				{
					float y = 6f + (row * 6f);

					c.Disc(4.6f, y, 1.3f);
					c.Line(8.4f, y, 20.4f, y, 1.6f);
				}

				break;

			case UiIcon.TileView:
				c.Box(3.2f, 3.2f, 7.4f, 7.4f);
				c.Box(13.4f, 3.2f, 7.4f, 7.4f);
				c.Box(3.2f, 13.4f, 7.4f, 7.4f);
				c.Box(13.4f, 13.4f, 7.4f, 7.4f);
				break;

			case UiIcon.Hierarchy:
				c.Line(6.6f, 4.6f, 6.6f, 19.4f, 1.6f);
				c.Line(6.6f, 8.6f, 16.4f, 8.6f, 1.6f);
				c.Line(6.6f, 16.6f, 16.4f, 16.6f, 1.6f);
				c.Disc(18f, 8.6f, 1.9f);
				c.Disc(18f, 16.6f, 1.9f);
				c.Disc(6.6f, 3.4f, 1.9f);
				break;

			case UiIcon.Inspector:
				c.Line(3.2f, 6f, 20.8f, 6f, 1.5f);
				c.Box(14.2f, 4.2f, 3.6f, 3.6f);
				c.Line(3.2f, 12f, 20.8f, 12f, 1.5f);
				c.Box(7.4f, 10.2f, 3.6f, 3.6f);
				c.Line(3.2f, 18f, 20.8f, 18f, 1.5f);
				c.Box(15.6f, 16.2f, 3.6f, 3.6f);
				break;

			case UiIcon.Console:
				c.Frame(2.8f, 4.2f, 18.4f, 15.6f, 1.6f);
				c.Stroke(false, [(6.6f, 9f), (9.8f, 12f), (6.6f, 15f)], 1.6f);
				c.Line(12.4f, 15f, 17.4f, 15f, 1.6f);
				break;

			case UiIcon.Profiler:
				c.Box(4f, 13.4f, 3.8f, 7.2f);
				c.Box(10.1f, 8.4f, 3.8f, 12.2f);
				c.Box(16.2f, 3.4f, 3.8f, 17.2f);
				break;

			case UiIcon.Visibility:
				c.Stroke(false, [(2.4f, 12f), (6f, 7.4f), (12f, 5.6f), (18f, 7.4f), (21.6f, 12f)]);
				c.Stroke(false, [(2.4f, 12f), (6f, 16.6f), (12f, 18.4f), (18f, 16.6f), (21.6f, 12f)]);
				c.Disc(12f, 12f, 2.8f);
				break;

			case UiIcon.Dot:
				c.Disc(12f, 12f, 2.2f);
				break;
		}
	}

	/// <summary>
	/// A point on the 24-unit icon grid with implicit tuple
	/// construction, so shapes read as coordinate lists.
	/// </summary>
	public readonly record struct IconPoint(float X, float Y)
	{
		public static implicit operator IconPoint((float X, float Y) point)
		{
			return new IconPoint(point.X, point.Y);
		}
	}

	/// <summary>
	/// The drawing surface for one icon: maps grid coordinates to the
	/// device rectangle and offers the primitives icons are made of.
	/// </summary>
	private readonly ref struct IconCanvas(
		UiDrawList drawList,
		float originX,
		float originY,
		float scale,
		UiColor color,
		float thickness)
	{
		private readonly UiDrawList _drawList = drawList;
		private readonly float _originX = originX;
		private readonly float _originY = originY;
		private readonly float _scale = scale;
		private readonly UiColor _color = color;
		private readonly float _thickness = thickness;

		public void Fill(params ReadOnlySpan<IconPoint> points)
		{
			if (points.Length < 3)
			{
				return;
			}

			Span<UiPoint> mapped = stackalloc UiPoint[points.Length];

			for (int index = 0; index < points.Length; index++)
			{
				mapped[index] = Map(points[index].X, points[index].Y);
			}

			FillPolygon(_drawList, mapped, _color);
		}

		public void Stroke(
			bool closed,
			params ReadOnlySpan<IconPoint> points)
		{
			Stroke(closed, points, _thickness);
		}

		public void Stroke(
			bool closed,
			ReadOnlySpan<IconPoint> points,
			float thickness)
		{
			int count = closed ? points.Length : points.Length - 1;

			for (int index = 0; index < count; index++)
			{
				IconPoint from = points[index];
				IconPoint to = points[(index + 1) % points.Length];

				Line(from.X, from.Y, to.X, to.Y, thickness);
			}
		}

		public void Line(
			float x1,
			float y1,
			float x2,
			float y2)
		{
			Line(x1, y1, x2, y2, _thickness);
		}

		public void Line(
			float x1,
			float y1,
			float x2,
			float y2,
			float thickness)
		{
			_drawList.AddLine(
				Map(x1, y1),
				Map(x2, y2),
				thickness,
				_color);
		}

		public void Box(float x, float y, float width, float height)
		{
			UiPoint topLeft = Map(x, y);

			_drawList.AddRect(
				new UiRect(
					topLeft.X,
					topLeft.Y,
					width * _scale,
					height * _scale),
				_color);
		}

		public void Frame(
			float x,
			float y,
			float width,
			float height,
			float radius = 0f)
		{
			UiPoint topLeft = Map(x, y);

			_drawList.AddBorder(
				new UiRect(
					topLeft.X,
					topLeft.Y,
					width * _scale,
					height * _scale),
				_color,
				_thickness,
				radius * _scale);
		}

		public void Disc(float cx, float cy, float radius)
		{
			FillCircle(cx, cy, radius, segments: 20);
		}

		public void Ring(
			float cx,
			float cy,
			float radius,
			float? thickness = null)
		{
			Arc(cx, cy, radius, 0f, 360f, thickness);
		}

		public void Arc(
			float cx,
			float cy,
			float radius,
			float startDegrees,
			float endDegrees,
			float? strokeThickness = null)
		{
			float sweep = MathF.Abs(endDegrees - startDegrees);
			int segments = Math.Clamp(
				(int)MathF.Ceiling(sweep / 12f),
				3,
				48);

			UiPoint previous = default;

			for (int index = 0; index <= segments; index++)
			{
				float angle = (startDegrees +
					((endDegrees - startDegrees) *
						index / segments)) * MathF.PI / 180f;

				UiPoint point = Map(
					cx + (MathF.Cos(angle) * radius),
					cy + (MathF.Sin(angle) * radius));

				if (index > 0)
				{
					_drawList.AddLine(
						previous,
						point,
						strokeThickness ?? _thickness,
						_color);
				}

				previous = point;
			}
		}

		public void Ellipse(
			float cx,
			float cy,
			float radiusX,
			float radiusY)
		{
			const int segments = 24;

			UiPoint previous = default;

			for (int index = 0; index <= segments; index++)
			{
				float angle = index * MathF.Tau / segments;

				UiPoint point = Map(
					cx + (MathF.Cos(angle) * radiusX),
					cy + (MathF.Sin(angle) * radiusY));

				if (index > 0)
				{
					_drawList.AddLine(previous, point, _thickness, _color);
				}

				previous = point;
			}
		}

		private void FillCircle(float cx, float cy, float radius, int segments)
		{
			Span<UiPoint> points = stackalloc UiPoint[segments];

			for (int index = 0; index < segments; index++)
			{
				float angle = index * MathF.Tau / segments;

				points[index] = Map(
					cx + (MathF.Cos(angle) * radius),
					cy + (MathF.Sin(angle) * radius));
			}

			FillPolygon(_drawList, points, _color);
		}

		private UiPoint Map(float x, float y)
		{
			return new UiPoint(
				_originX + (x * _scale),
				_originY + (y * _scale));
		}
	}

	/// <summary>
	/// Scanline-converts a convex or simple polygon into horizontal
	/// spans. Icons are small, so one device row per rectangle is both
	/// accurate and cheap.
	/// </summary>
	private static void FillPolygon(
		UiDrawList drawList,
		ReadOnlySpan<UiPoint> points,
		UiColor color)
	{
		float minimumY = float.MaxValue;
		float maximumY = float.MinValue;

		for (int index = 0; index < points.Length; index++)
		{
			minimumY = MathF.Min(minimumY, points[index].Y);
			maximumY = MathF.Max(maximumY, points[index].Y);
		}

		int firstRow = (int)MathF.Ceiling(minimumY);
		int lastRow = (int)MathF.Floor(maximumY);

		Span<float> crossings = stackalloc float[64];

		for (int row = firstRow; row < lastRow; row++)
		{
			float y = row + 0.5f;
			int count = 0;

			for (int index = 0; index < points.Length; index++)
			{
				UiPoint from = points[index];
				UiPoint to = points[(index + 1) % points.Length];

				if (from.Y == to.Y)
				{
					continue;
				}

				if ((from.Y <= y && to.Y > y) ||
					(to.Y <= y && from.Y > y))
				{
					if (count >= crossings.Length)
					{
						break;
					}

					float amount = (y - from.Y) / (to.Y - from.Y);
					crossings[count++] = from.X + (amount * (to.X - from.X));
				}
			}

			Sort(crossings[..count]);

			for (int index = 0; index + 1 < count; index += 2)
			{
				float left = crossings[index];
				float right = crossings[index + 1];

				if (right - left <= 0f)
				{
					continue;
				}

				drawList.AddRect(
					new UiRect(left, row, right - left, 1f),
					color);
			}
		}
	}

	private static void Sort(Span<float> values)
	{
		for (int index = 1; index < values.Length; index++)
		{
			float value = values[index];
			int position = index - 1;

			while (position >= 0 && values[position] > value)
			{
				values[position + 1] = values[position];
				position--;
			}

			values[position + 1] = value;
		}
	}
}
