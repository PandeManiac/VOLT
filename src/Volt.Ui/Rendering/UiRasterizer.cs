using Volt.Ui.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// Draws a UI draw list into an RGBA image on the CPU.
/// </summary>
/// <remarks>
/// <para>
/// This is the headless counterpart of the Vulkan renderer: it consumes
/// the same commands, the same clips, and the same font coverage, so an
/// interface can be rendered, reviewed, and diffed on a machine with no
/// display server and no GPU.
/// </para>
/// <para>
/// Image commands sample textures supplied by the caller, which is how
/// a CPU-rendered scene image and any other offscreen content reach the
/// frame. A command whose texture was not supplied becomes a neutral
/// placeholder in its tint, so a draw list that references a GPU target
/// still renders without one. Everything a widget draws itself —
/// surfaces, borders, icons, text, gizmos, overlays — is rendered.
/// </para>
/// <para>
/// Geometry is evaluated per pixel with a one-pixel analytic edge, which
/// is close to what the graphics pipeline produces for the same vertices
/// without needing its rasterizer. It is a review tool, not a pixel-
/// exact reference.
/// </para>
/// </remarks>
public static class UiRasterizer
{
	/// <summary>
	/// Renders a draw list.
	/// </summary>
	/// <param name="drawList">The completed frame to draw.</param>
	/// <param name="scale">
	/// Logical units per output pixel. One renders at the draw list's own
	/// surface size.
	/// </param>
	/// <param name="background">
	/// The colour to start from; null leaves the image transparent.
	/// </param>
	/// <param name="textures">
	/// Images for image commands, keyed by the identifier each command
	/// carries. Null, or a missing identifier, draws the placeholder.
	/// </param>
	public static UiRasterImage Render(
		UiDrawList drawList,
		float scale = 1f,
		UiColor? background = null,
		IReadOnlyDictionary<UiTextureId, UiRasterImage>? textures = null)
	{
		ArgumentNullException.ThrowIfNull(drawList);

		if (!float.IsFinite(scale) || scale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(scale));
		}

		UiRect surface = new(
			0f,
			0f,
			drawList.Surface.Width,
			drawList.Surface.Height);

		UiRasterImage image = new(
			Math.Max(1, (int)MathF.Ceiling(surface.Width * scale)),
			Math.Max(1, (int)MathF.Ceiling(surface.Height * scale)));

		image.Clear(background ?? UiColor.Transparent);

		ReadOnlySpan<UiDrawCommand> commands = drawList.Commands;

		for (int index = 0; index < commands.Length; index++)
		{
			UiDrawCommand command = commands[index];

			if (command.IsEmpty)
			{
				continue;
			}

			UiRect clip = ScaleRect(command.Clip, scale);

			if (clip.IsEmpty)
			{
				continue;
			}

			switch (command.Kind)
			{
				case UiDrawCommandKind.Rectangle:
					FillRect(
						image,
						ScaleRect(command.Rect, scale),
						command.Color,
						clip);
					break;

				case UiDrawCommandKind.RoundedRectangle:
					FillRounded(
						image,
						ScaleRect(command.Rect, scale),
						command.Radius * scale,
						command.Color,
						clip);
					break;

				case UiDrawCommandKind.Border:
					StrokeRounded(
						image,
						ScaleRect(command.Rect, scale),
						command.Radius * scale,
						command.Thickness * scale,
						command.Color,
						clip);
					break;

				case UiDrawCommandKind.Line:
					DrawLine(
						image,
						ScalePoint(command.Start, scale),
						ScalePoint(command.End, scale),
						command.Thickness * scale,
						command.Color,
						clip);
					break;

				case UiDrawCommandKind.Text:
					DrawText(image, command, scale, clip);
					break;

				case UiDrawCommandKind.Image:
					if (textures is not null &&
						textures.TryGetValue(
							command.Texture,
							out UiRasterImage? source))
					{
						DrawImage(image, command, source, scale, clip);
					}
					else
					{
						FillRect(
							image,
							ScaleRect(command.Rect, scale),
							Placeholder(command.Color),
							clip);
					}
					break;
			}
		}

		return image;
	}

	private static void FillRect(
		UiRasterImage image,
		UiRect rect,
		UiColor color,
		UiRect clip)
	{
		UiRect area = rect.Intersect(clip);

		if (area.IsEmpty)
		{
			return;
		}

		int left = (int)MathF.Floor(area.X);
		int top = (int)MathF.Floor(area.Y);
		int right = (int)MathF.Ceiling(area.Right);
		int bottom = (int)MathF.Ceiling(area.Bottom);

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				if (InsideRect(rect, x, y))
				{
					image.Blend(x, y, color, 1f);
				}
			}
		}
	}

	private static void FillRounded(
		UiRasterImage image,
		UiRect rect,
		float radius,
		UiColor color,
		UiRect clip)
	{
		UiRect area = rect.Intersect(clip);

		if (area.IsEmpty)
		{
			return;
		}

		int left = (int)MathF.Floor(area.X);
		int top = (int)MathF.Floor(area.Y);
		int right = (int)MathF.Ceiling(area.Right);
		int bottom = (int)MathF.Ceiling(area.Bottom);

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				float coverage = RoundedCoverage(
					x + 0.5f,
					y + 0.5f,
					rect,
					radius);

				image.Blend(x, y, color, coverage);
			}
		}
	}

	private static void StrokeRounded(
		UiRasterImage image,
		UiRect rect,
		float radius,
		float thickness,
		UiColor color,
		UiRect clip)
	{
		float maximum = MathF.Min(rect.Width, rect.Height) * 0.5f;

		thickness = MathF.Min(thickness, maximum);

		if (thickness <= 0f)
		{
			return;
		}

		radius = MathF.Min(MathF.Max(0f, radius), maximum);

		UiRect inner = rect.Inset(thickness);

		if (inner.IsEmpty)
		{
			return;
		}

		float innerRadius = MathF.Max(0f, radius - thickness);
		UiRect area = rect.Intersect(clip);

		if (area.IsEmpty)
		{
			return;
		}

		int left = (int)MathF.Floor(area.X);
		int top = (int)MathF.Floor(area.Y);
		int right = (int)MathF.Ceiling(area.Right);
		int bottom = (int)MathF.Ceiling(area.Bottom);

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				float outer = RoundedCoverage(
					x + 0.5f,
					y + 0.5f,
					rect,
					radius);

				float covered = outer - RoundedCoverage(
					x + 0.5f,
					y + 0.5f,
					inner,
					innerRadius);

				image.Blend(x, y, color, MathF.Max(0f, covered));
			}
		}
	}

	private static void DrawLine(
		UiRasterImage image,
		UiPoint start,
		UiPoint end,
		float thickness,
		UiColor color,
		UiRect clip)
	{
		float half = thickness * 0.5f;
		float left = MathF.Max(clip.X, MathF.Min(start.X, end.X) - half);
		float top = MathF.Max(clip.Y, MathF.Min(start.Y, end.Y) - half);
		float right = MathF.Min(clip.Right, MathF.Max(start.X, end.X) + half);
		float bottom = MathF.Min(clip.Bottom, MathF.Max(start.Y, end.Y) + half);

		if (right <= left || bottom <= top)
		{
			return;
		}

		int first = (int)MathF.Floor(left);
		int last = (int)MathF.Ceiling(right);
		int row = (int)MathF.Floor(top);
		int lastRow = (int)MathF.Ceiling(bottom);

		for (int y = row; y < lastRow; y++)
		{
			for (int x = first; x < last; x++)
			{
				float distance = DistanceToSegment(
					x + 0.5f,
					y + 0.5f,
					start,
					end);

				float coverage = Math.Clamp(
					0.5f + half - distance,
					0f,
					1f);

				image.Blend(x, y, color, coverage);
			}
		}
	}

	/// <summary>
	/// Draws one image command by sampling its texture over the command
	/// rectangle.
	/// </summary>
	/// <remarks>
	/// Sampling is bilinear and clamped, so a source texture rendered at
	/// the panel's physical size lands on exact texel centres at a scale
	/// of one, and a resized or scaled panel still reads smoothly instead
	/// of repeating edge texels. The tint multiplies every channel,
	/// including alpha, which lets a caller fade an image as a whole.
	/// </remarks>
	private static void DrawImage(
		UiRasterImage image,
		UiDrawCommand command,
		UiRasterImage source,
		float scale,
		UiRect clip)
	{
		UiRect rect = ScaleRect(command.Rect, scale);
		UiRect area = rect.Intersect(clip);

		if (area.IsEmpty ||
			rect.Width <= 0f ||
			rect.Height <= 0f ||
			command.Uv.Width <= 0f ||
			command.Uv.Height <= 0f)
		{
			return;
		}

		int left = (int)MathF.Floor(area.X);
		int top = (int)MathF.Floor(area.Y);
		int right = (int)MathF.Ceiling(area.Right);
		int bottom = (int)MathF.Ceiling(area.Bottom);

		for (int y = top; y < bottom; y++)
		{
			float v = (y + 0.5f - rect.Y) / rect.Height;

			if (v < 0f || v >= 1f)
			{
				continue;
			}

			float sourceV = command.Uv.Y + (v * command.Uv.Height);

			for (int x = left; x < right; x++)
			{
				float u = (x + 0.5f - rect.X) / rect.Width;

				if (u < 0f || u >= 1f)
				{
					continue;
				}

				float sourceU = command.Uv.X + (u * command.Uv.Width);

				UiColor sample = SampleImage(source, sourceU, sourceV);

				UiColor tinted = new(
					(byte)((sample.R * command.Color.R) / 255),
					(byte)((sample.G * command.Color.G) / 255),
					(byte)((sample.B * command.Color.B) / 255),
					(byte)((sample.A * command.Color.A) / 255));

				image.Blend(x, y, tinted, 1f);
			}
		}
	}

	private static UiColor SampleImage(
		UiRasterImage source,
		float u,
		float v)
	{
		float x = (u * source.Width) - 0.5f;
		float y = (v * source.Height) - 0.5f;

		int x0 = (int)MathF.Floor(x);
		int y0 = (int)MathF.Floor(y);

		float fx = x - x0;
		float fy = y - y0;

		UiColor top = Mix(
			SampleImageTexel(source, x0, y0),
			SampleImageTexel(source, x0 + 1, y0),
			fx);

		UiColor bottom = Mix(
			SampleImageTexel(source, x0, y0 + 1),
			SampleImageTexel(source, x0 + 1, y0 + 1),
			fx);

		return Mix(top, bottom, fy);
	}

	private static UiColor SampleImageTexel(
		UiRasterImage source,
		int x,
		int y)
	{
		x = Math.Clamp(x, 0, source.Width - 1);
		y = Math.Clamp(y, 0, source.Height - 1);

		return source.GetPixel(x, y);
	}

	private static UiColor Mix(UiColor from, UiColor to, float amount)
	{
		return new UiColor(
			MixChannel(from.R, to.R, amount),
			MixChannel(from.G, to.G, amount),
			MixChannel(from.B, to.B, amount),
			MixChannel(from.A, to.A, amount));
	}

	private static byte MixChannel(byte from, byte to, float amount)
	{
		return (byte)Math.Clamp(
			(int)MathF.Round(from + ((to - from) * amount)),
			0,
			255);
	}

	private static void DrawText(
		UiRasterImage image,
		UiDrawCommand command,
		float scale,
		UiRect clip)
	{
		UiTextLayoutResult layout = command.Text!;
		UiFontAtlas atlas = layout.Font.Atlas;

		ReadOnlySpan<UiGlyphPlacement> glyphs = layout.Glyphs;

		for (int index = 0; index < glyphs.Length; index++)
		{
			UiGlyphPlacement placement = glyphs[index];
			UiGlyph glyph = placement.Glyph;

			if (glyph.IsEmpty)
			{
				continue;
			}

			UiRect rect = new(
				(command.TextOrigin.X + placement.Position.X) * scale,
				(command.TextOrigin.Y + placement.Position.Y) * scale,
				glyph.Size.Width * scale,
				glyph.Size.Height * scale);

			DrawGlyph(image, atlas, glyph, rect, command.Color, clip);
		}
	}

	private static void DrawGlyph(
		UiRasterImage image,
		UiFontAtlas atlas,
		UiGlyph glyph,
		UiRect rect,
		UiColor color,
		UiRect clip)
	{
		UiRect area = rect.Intersect(clip);

		if (area.IsEmpty || rect.Width <= 0f || rect.Height <= 0f)
		{
			return;
		}

		int left = (int)MathF.Floor(area.X);
		int top = (int)MathF.Floor(area.Y);
		int right = (int)MathF.Ceiling(area.Right);
		int bottom = (int)MathF.Ceiling(area.Bottom);

		for (int y = top; y < bottom; y++)
		{
			for (int x = left; x < right; x++)
			{
				float u = (x + 0.5f - rect.X) / rect.Width;
				float v = (y + 0.5f - rect.Y) / rect.Height;

				float coverage = SampleAtlas(atlas, glyph.Uv, u, v);

				image.Blend(x, y, color, coverage);
			}
		}
	}

	private static float SampleAtlas(
		UiFontAtlas atlas,
		UiRect uv,
		float u,
		float v)
	{
		float width = uv.Width * atlas.Width;
		float height = uv.Height * atlas.Height;

		if (width <= 0f || height <= 0f)
		{
			return 0f;
		}

		float x = (uv.X * atlas.Width) + (u * width) - 0.5f;
		float y = (uv.Y * atlas.Height) + (v * height) - 0.5f;

		int x0 = (int)MathF.Floor(x);
		int y0 = (int)MathF.Floor(y);

		float fx = x - x0;
		float fy = y - y0;

		float top = Mix(
			SampleAtlasTexel(atlas, x0, y0),
			SampleAtlasTexel(atlas, x0 + 1, y0),
			fx);

		float bottom = Mix(
			SampleAtlasTexel(atlas, x0, y0 + 1),
			SampleAtlasTexel(atlas, x0 + 1, y0 + 1),
			fx);

		return Mix(top, bottom, fy);
	}

	private static float SampleAtlasTexel(
		UiFontAtlas atlas,
		int x,
		int y)
	{
		x = Math.Clamp(x, 0, atlas.Width - 1);
		y = Math.Clamp(y, 0, atlas.Height - 1);

		return atlas.Pixels[(y * atlas.Width) + x] / 255f;
	}

	private static float Mix(float from, float to, float amount)
	{
		return from + ((to - from) * amount);
	}

	private static float RoundedCoverage(
		float x,
		float y,
		UiRect rect,
		float radius)
	{
		float halfWidth = rect.Width * 0.5f;
		float halfHeight = rect.Height * 0.5f;
		float clamped = MathF.Min(radius, MathF.Min(halfWidth, halfHeight));

		UiPoint center = rect.Center;

		float dx = MathF.Abs(x - center.X) - (halfWidth - clamped);
		float dy = MathF.Abs(y - center.Y) - (halfHeight - clamped);

		float outside = MathF.Sqrt(
			(MathF.Max(dx, 0f) * MathF.Max(dx, 0f)) +
			(MathF.Max(dy, 0f) * MathF.Max(dy, 0f)));

		float distance = outside + MathF.Min(
			MathF.Max(dx, dy),
			0f) - clamped;

		return Math.Clamp(0.5f - distance, 0f, 1f);
	}

	private static float DistanceToSegment(
		float x,
		float y,
		UiPoint start,
		UiPoint end)
	{
		float dx = end.X - start.X;
		float dy = end.Y - start.Y;
		float lengthSquared = (dx * dx) + (dy * dy);

		if (lengthSquared <= 0.000001f)
		{
			return MathF.Sqrt(
				((x - start.X) * (x - start.X)) +
				((y - start.Y) * (y - start.Y)));
		}

		float t = Math.Clamp(
			(((x - start.X) * dx) + ((y - start.Y) * dy)) /
				lengthSquared,
			0f,
			1f);

		float closestX = start.X + (dx * t);
		float closestY = start.Y + (dy * t);

		return MathF.Sqrt(
			((x - closestX) * (x - closestX)) +
			((y - closestY) * (y - closestY)));
	}

	private static bool InsideRect(UiRect rect, int x, int y)
	{
		float centerX = x + 0.5f;
		float centerY = y + 0.5f;

		return centerX >= rect.X &&
			centerX < rect.Right &&
			centerY >= rect.Y &&
			centerY < rect.Bottom;
	}

	private static UiRect ScaleRect(UiRect rect, float scale)
	{
		return new UiRect(
			rect.X * scale,
			rect.Y * scale,
			rect.Width * scale,
			rect.Height * scale);
	}

	private static UiPoint ScalePoint(UiPoint point, float scale)
	{
		return new UiPoint(point.X * scale, point.Y * scale);
	}

	private static UiColor Placeholder(UiColor tint)
	{
		return new UiColor(
			(byte)(20 + (tint.R * 0.04f)),
			(byte)(22 + (tint.G * 0.04f)),
			(byte)(26 + (tint.B * 0.04f)));
	}
}
