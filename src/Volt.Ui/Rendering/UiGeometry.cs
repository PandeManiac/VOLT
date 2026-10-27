using System.Numerics;
using System.Runtime.InteropServices;
using Volt.Ui.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// Converts a UI draw list into batched triangles.
/// </summary>
/// <remarks>
/// <para>
/// Geometry is generated in logical units. Callers pass the display
/// scale used by the target framebuffer; positions, glyph sizes, and
/// scissor rectangles are converted to physical pixels. The renderer
/// only uploads buffers and issues one draw per batch.
/// </para>
/// <para>
/// Clipping uses a scissor rectangle rather than clipping individual
/// triangles, so a command's geometry can extend beyond its clip
/// while remaining invisible. A batch breaks whenever the scissor or
/// sampled texture changes, which keeps state changes independent of
/// the number of widgets.
/// </para>
/// <para>
/// This type is deliberately free of graphics API references. Its
/// output can be validated on a headless machine.
/// </para>
/// </remarks>
public sealed class UiGeometry
{
	private const int RoundedCornerSegments = 6;

	private readonly List<UiVertex> _vertices = [];
	private readonly List<uint> _indices = [];
	private readonly List<UiBatch> _batches = [];

	private float _scale = 1f;
	private UiPixelRect _surfacePixels;

	private UiTextureKey _batchTexture;
	private UiPixelRect _batchScissor;
	private int _batchFirstIndex;
	private int _batchIndexCount;
	private bool _hasBatch;

	public ReadOnlySpan<UiVertex> Vertices =>
		CollectionsMarshal.AsSpan(_vertices);

	public ReadOnlySpan<uint> Indices =>
		CollectionsMarshal.AsSpan(_indices);

	public ReadOnlySpan<UiBatch> Batches =>
		CollectionsMarshal.AsSpan(_batches);

	public int BatchCount => _batches.Count;

	/// <summary>
	/// Regenerates all geometry for a draw list.
	/// </summary>
	public void Build(UiDrawList drawList, float scale = 1f)
	{
		ArgumentNullException.ThrowIfNull(drawList);

		if (!float.IsFinite(scale) || scale <= 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(scale));
		}

		_vertices.Clear();
		_indices.Clear();
		_batches.Clear();
		_hasBatch = false;
		_batchFirstIndex = 0;
		_batchIndexCount = 0;

		_scale = scale;
		_surfacePixels = new UiPixelRect(
			0,
			0,
			Math.Max(0, (int)MathF.Ceiling(
				drawList.Surface.Width * scale)),
			Math.Max(0, (int)MathF.Ceiling(
				drawList.Surface.Height * scale)));

		ReadOnlySpan<UiDrawCommand> commands = drawList.Commands;

		for (int commandIndex = 0;
			commandIndex < commands.Length;
			commandIndex++)
		{
			UiDrawCommand command = commands[commandIndex];

			if (command.IsEmpty)
			{
				continue;
			}

			UiPixelRect scissor = ToPixels(command.Clip);

			if (scissor.IsEmpty)
			{
				continue;
			}

			switch (command.Kind)
			{
				case UiDrawCommandKind.Text:
					AppendText(command, scissor);
					break;

				case UiDrawCommandKind.Image:
					AppendImage(command, scissor);
					break;

				default:
					AppendSolid(command, scissor);
					break;
			}
		}

		FlushBatch();
	}

	private void AppendSolid(
		UiDrawCommand command,
		UiPixelRect scissor)
	{
		switch (command.Kind)
		{
			case UiDrawCommandKind.Rectangle:
				BeginBatch(UiTextureKey.White, scissor);
				AddRect(command.Rect, command.Color);
				break;

			case UiDrawCommandKind.RoundedRectangle:
				BeginBatch(UiTextureKey.White, scissor);
				AddRoundedRect(
					command.Rect,
					command.Radius,
					command.Color);
				break;

			case UiDrawCommandKind.Border:
				BeginBatch(UiTextureKey.White, scissor);
				AddBorder(
					command.Rect,
					command.Radius,
					command.Thickness,
					command.Color);
				break;

			case UiDrawCommandKind.Line:
				BeginBatch(UiTextureKey.White, scissor);
				AddLine(
					command.Start,
					command.End,
					command.Thickness,
					command.Color);
				break;
		}
	}

	private void AppendText(
		UiDrawCommand command,
		UiPixelRect scissor)
	{
		UiTextLayoutResult layout = command.Text!;
		BeginBatch(
			UiTextureKey.FromFont(layout.Font),
			scissor);

		ReadOnlySpan<UiGlyphPlacement> glyphs = layout.Glyphs;

		for (int index = 0; index < glyphs.Length; index++)
		{
			UiGlyphPlacement placement = glyphs[index];
			UiGlyph glyph = placement.Glyph;

			UiRect rect = new(
				command.TextOrigin.X + placement.Position.X,
				command.TextOrigin.Y + placement.Position.Y,
				glyph.Size.Width,
				glyph.Size.Height);

			AddTexturedQuad(
				rect,
				glyph.Uv,
				command.Color);
		}
	}

	private void AppendImage(
		UiDrawCommand command,
		UiPixelRect scissor)
	{
		BeginBatch(UiTextureKey.Image(command.Texture), scissor);

		AddTexturedQuad(
			command.Rect,
			command.Uv,
			command.Color);
	}

	private void BeginBatch(
		UiTextureKey texture,
		UiPixelRect scissor)
	{
		if (_hasBatch &&
			(_batchTexture != texture ||
				_batchScissor != scissor))
		{
			FlushBatch();
		}

		if (!_hasBatch)
		{
			_batchTexture = texture;
			_batchScissor = scissor;
			_batchFirstIndex = _indices.Count;
			_batchIndexCount = 0;
			_hasBatch = true;
		}
	}

	private void FlushBatch()
	{
		if (!_hasBatch || _batchIndexCount == 0)
		{
			_hasBatch = false;
			return;
		}

		_batches.Add(
			new UiBatch(
				_batchFirstIndex,
				_batchIndexCount,
				_batchScissor,
				_batchTexture));

		_hasBatch = false;
		_batchIndexCount = 0;
	}

	private void AddRect(UiRect rect, UiColor color)
	{
		if (rect.IsEmpty)
		{
			return;
		}

		Vector2 topLeft = ToVector(
			new UiPoint(rect.X, rect.Y));

		Vector2 topRight = ToVector(
			new UiPoint(rect.Right, rect.Y));

		Vector2 bottomRight = ToVector(
			new UiPoint(rect.Right, rect.Bottom));

		Vector2 bottomLeft = ToVector(
			new UiPoint(rect.X, rect.Bottom));

		uint packed = Pack(color);
		Vector2 uv = new(0.5f, 0.5f);

		uint baseIndex = checked((uint)_vertices.Count);
		_vertices.Add(new UiVertex(topLeft, uv, packed));
		_vertices.Add(new UiVertex(topRight, uv, packed));
		_vertices.Add(new UiVertex(bottomRight, uv, packed));
		_vertices.Add(new UiVertex(bottomLeft, uv, packed));

		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 1);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 3);

		_batchIndexCount += 6;
	}

	private void AddRoundedRect(
		UiRect rect,
		float radius,
		UiColor color)
	{
		radius = MathF.Min(
			MathF.Max(0f, radius),
			MathF.Min(rect.Width, rect.Height) * 0.5f);

		if (radius < 0.5f)
		{
			AddRect(rect, color);
			return;
		}

		Span<Vector2> perimeter =
			stackalloc Vector2[(RoundedCornerSegments + 1) * 4];

		BuildPerimeter(rect, radius, perimeter);

		uint centerIndex = checked((uint)_vertices.Count);
		uint packed = Pack(color);

		_vertices.Add(
			new UiVertex(
				ToVector(rect.Center),
				new Vector2(0.5f, 0.5f),
				packed));

		for (int index = 0; index < perimeter.Length; index++)
		{
			_vertices.Add(
				new UiVertex(
					perimeter[index],
					new Vector2(0.5f, 0.5f),
					packed));
		}

		for (int index = 0; index < perimeter.Length; index++)
		{
			uint current = checked(
				(uint)(centerIndex + 1 + index));

			uint next = checked(
				(uint)(centerIndex + 1 +
					((index + 1) % perimeter.Length)));

			_indices.Add(centerIndex);
			_indices.Add(current);
			_indices.Add(next);

			_batchIndexCount += 3;
		}
	}

	private void AddBorder(
		UiRect rect,
		float radius,
		float thickness,
		UiColor color)
	{
		float maximum = MathF.Min(rect.Width, rect.Height) * 0.5f;
		thickness = MathF.Min(thickness, maximum);
		radius = MathF.Min(
			MathF.Max(0f, radius),
			maximum);

		if (thickness <= 0f)
		{
			return;
		}

		if (radius < 0.5f)
		{
			AddRect(
				new UiRect(rect.X, rect.Y, rect.Width, thickness),
				color);

			AddRect(
				new UiRect(
					rect.X,
					rect.Bottom - thickness,
					rect.Width,
					thickness),
				color);

			AddRect(
				new UiRect(rect.X, rect.Y, thickness, rect.Height),
				color);

			AddRect(
				new UiRect(
					rect.Right - thickness,
					rect.Y,
					thickness,
					rect.Height),
				color);

			return;
		}

		Span<Vector2> outer =
			stackalloc Vector2[(RoundedCornerSegments + 1) * 4];

		Span<Vector2> inner =
			stackalloc Vector2[(RoundedCornerSegments + 1) * 4];

		BuildPerimeter(rect, radius, outer);

		UiRect inset = rect.Inset(thickness);
		float innerRadius = MathF.Max(0f, radius - thickness);

		if (inset.Width <= 0f || inset.Height <= 0f)
		{
			return;
		}

		BuildPerimeter(inset, innerRadius, inner);

		uint packed = Pack(color);
		Vector2 uv = new(0.5f, 0.5f);

		for (int index = 0; index < outer.Length; index++)
		{
			int next = (index + 1) % outer.Length;
			uint baseIndex = checked((uint)_vertices.Count);

			_vertices.Add(new UiVertex(outer[index], uv, packed));
			_vertices.Add(new UiVertex(outer[next], uv, packed));
			_vertices.Add(new UiVertex(inner[next], uv, packed));
			_vertices.Add(new UiVertex(inner[index], uv, packed));

			_indices.Add(baseIndex + 0);
			_indices.Add(baseIndex + 1);
			_indices.Add(baseIndex + 2);
			_indices.Add(baseIndex + 0);
			_indices.Add(baseIndex + 2);
			_indices.Add(baseIndex + 3);

			_batchIndexCount += 6;
		}
	}

	private static void BuildPerimeter(
		UiRect rect,
		float radius,
		Span<Vector2> destination)
	{
		Span<UiPoint> centers =
			stackalloc UiPoint[4];

		centers[0] = new UiPoint(
			rect.X + radius,
			rect.Y + radius);

		centers[1] = new UiPoint(
			rect.Right - radius,
			rect.Y + radius);

		centers[2] = new UiPoint(
			rect.Right - radius,
			rect.Bottom - radius);

		centers[3] = new UiPoint(
			rect.X + radius,
			rect.Bottom - radius);

		int cursor = 0;

		for (int corner = 0; corner < 4; corner++)
		{
			float startAngle = MathF.PI + (corner * MathF.PI * 0.5f);
			UiPoint center = centers[corner];

			for (int segment = 0;
				segment <= RoundedCornerSegments;
				segment++)
			{
				float angle = startAngle +
					(segment / (float)RoundedCornerSegments) *
					(MathF.PI * 0.5f);

				destination[cursor++] = new Vector2(
					center.X + (MathF.Cos(angle) * radius),
					center.Y + (MathF.Sin(angle) * radius));
			}
		}
	}

	private void AddLine(
		UiPoint start,
		UiPoint end,
		float thickness,
		UiColor color)
	{
		Vector2 direction = new(
			end.X - start.X,
			end.Y - start.Y);

		float length = direction.Length();

		if (length <= 0.0001f)
		{
			return;
		}

		direction /= length;

		Vector2 normal = new(-direction.Y, direction.X);
		Vector2 half = normal * (thickness * 0.5f);

		Vector2 startPoint = ToVector(start);
		Vector2 endPoint = ToVector(end);

		uint packed = Pack(color);
		Vector2 uv = new(0.5f, 0.5f);

		uint baseIndex = checked((uint)_vertices.Count);

		_vertices.Add(
			new UiVertex(startPoint + half, uv, packed));

		_vertices.Add(
			new UiVertex(endPoint + half, uv, packed));

		_vertices.Add(
			new UiVertex(endPoint - half, uv, packed));

		_vertices.Add(
			new UiVertex(startPoint - half, uv, packed));

		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 1);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 3);

		_batchIndexCount += 6;
	}

	private void AddTexturedQuad(
		UiRect rect,
		UiRect uvRect,
		UiColor tint)
	{
		if (rect.IsEmpty)
		{
			return;
		}

		Vector2 topLeft = ToVector(new UiPoint(rect.X, rect.Y));
		Vector2 topRight = ToVector(new UiPoint(rect.Right, rect.Y));
		Vector2 bottomRight = ToVector(new UiPoint(rect.Right, rect.Bottom));
		Vector2 bottomLeft = ToVector(new UiPoint(rect.X, rect.Bottom));

		uint packed = Pack(tint);
		uint baseIndex = checked((uint)_vertices.Count);

		_vertices.Add(
			new UiVertex(
				topLeft,
				new Vector2(uvRect.X, uvRect.Y),
				packed));

		_vertices.Add(
			new UiVertex(
				topRight,
				new Vector2(uvRect.Right, uvRect.Y),
				packed));

		_vertices.Add(
			new UiVertex(
				bottomRight,
				new Vector2(uvRect.Right, uvRect.Bottom),
				packed));

		_vertices.Add(
			new UiVertex(
				bottomLeft,
				new Vector2(uvRect.X, uvRect.Bottom),
				packed));

		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 1);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 0);
		_indices.Add(baseIndex + 2);
		_indices.Add(baseIndex + 3);

		_batchIndexCount += 6;
	}

	private Vector2 ToVector(UiPoint point)
	{
		return new Vector2(
			point.X * _scale,
			point.Y * _scale);
	}

	private UiPixelRect ToPixels(UiRect rect)
	{
		int left = (int)MathF.Floor(rect.X * _scale);
		int top = (int)MathF.Floor(rect.Y * _scale);
		int right = (int)MathF.Ceiling(rect.Right * _scale);
		int bottom = (int)MathF.Ceiling(rect.Bottom * _scale);

		left = Math.Clamp(left, 0, _surfacePixels.Right);
		top = Math.Clamp(top, 0, _surfacePixels.Bottom);
		right = Math.Clamp(right, left, _surfacePixels.Right);
		bottom = Math.Clamp(bottom, top, _surfacePixels.Bottom);

		return new UiPixelRect(
			left,
			top,
			right - left,
			bottom - top);
	}

	private static uint Pack(UiColor color)
	{
		return color.R |
			((uint)color.G << 8) |
			((uint)color.B << 16) |
			((uint)color.A << 24);
	}
}
