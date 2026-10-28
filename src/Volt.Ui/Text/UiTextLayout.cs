using System.Text;

namespace Volt.Ui.Text;

/// <summary>
/// Horizontal alignment of text inside its available width.
/// </summary>
public enum UiTextAlignment
{
	Start = 0,
	Center = 1,
	End = 2
}

/// <summary>
/// Controls whether long lines wrap at the available width.
/// </summary>
public enum UiTextWrap
{
	NoWrap = 0,
	Wrap = 1
}

/// <summary>
/// One laid-out line of text.
/// </summary>
/// <param name="Start">The UTF-16 index of the first character.</param>
/// <param name="Length">The number of UTF-16 code units in the line.</param>
/// <param name="Width">The measured logical width.</param>
/// <param name="OffsetX">The horizontal offset produced by alignment.</param>
/// <param name="Top">The top of the line relative to the text origin.</param>
public readonly record struct UiTextLine(
	int Start,
	int Length,
	float Width,
	float OffsetX,
	float Top)
{
	public int End => Start + Length;
}

/// <summary>
/// One positioned glyph ready for geometry generation.
/// </summary>
public readonly record struct UiGlyphPlacement(
	int TextIndex,
	UiGlyph Glyph,
	UiPoint Position);

/// <summary>
/// An immutable text layout for a particular string, font, width, and
/// wrapping mode.
/// </summary>
/// <remarks>
/// Layout allocates its line and glyph arrays. Callers should create
/// a result when content or width changes and reuse it across frames;
/// painting a result allocates nothing.
/// </remarks>
public sealed class UiTextLayoutResult
{
	private readonly UiTextLine[] _lines;
	private readonly UiGlyphPlacement[] _glyphs;

	internal UiTextLayoutResult(
		string source,
		UiFont font,
		float availableWidth,
		UiTextLine[] lines,
		UiGlyphPlacement[] glyphs)
	{
		Source = source;
		Font = font;
		AvailableWidth = availableWidth;
		_lines = lines;
		_glyphs = glyphs;

		float width = 0f;

		for (int index = 0; index < lines.Length; index++)
		{
			width = MathF.Max(width, lines[index].Width);
		}

		Width = width;
		Height = lines.Length * font.LineHeight;
	}

	public string Source { get; }

	public UiFont Font { get; }

	public float AvailableWidth { get; }

	public float Width { get; }

	public float Height { get; }

	public int LineCount => _lines.Length;

	public ReadOnlySpan<UiTextLine> Lines => _lines;

	public ReadOnlySpan<UiGlyphPlacement> Glyphs => _glyphs;

	public UiTextLine GetLine(int index)
	{
		return _lines[index];
	}

	/// <summary>
	/// Gets the line containing a UTF-16 index.
	/// </summary>
	public int GetLineIndex(int textIndex)
	{
		textIndex = Math.Clamp(textIndex, 0, Source.Length);

		for (int index = 0; index < _lines.Length; index++)
		{
			UiTextLine line = _lines[index];

			if (textIndex >= line.Start && textIndex <= line.End)
			{
				if (textIndex == line.End &&
					index + 1 < _lines.Length &&
					_lines[index + 1].Start == textIndex)
				{
					continue;
				}

				return index;
			}
		}

		return _lines.Length - 1;
	}

	/// <summary>
	/// Gets the caret position for a UTF-16 index, relative to the
	/// text origin.
	/// </summary>
	public UiPoint GetCaretPosition(int textIndex)
	{
		textIndex = Math.Clamp(textIndex, 0, Source.Length);

		int lineIndex = GetLineIndex(textIndex);
		UiTextLine line = _lines[lineIndex];
		int localEnd = Math.Clamp(textIndex, line.Start, line.End);

		float x = line.OffsetX + Measure(line.Start, localEnd);

		return new UiPoint(x, line.Top);
	}

	/// <summary>
	/// Gets the caret height in logical units.
	/// </summary>
	public float CaretHeight => Font.LineHeight;

	/// <summary>
	/// Gets the closest UTF-16 insertion index for a point relative to
	/// the text origin.
	/// </summary>
	public int GetIndexAt(UiPoint point)
	{
		int lineIndex = Math.Clamp(
			(int)MathF.Floor(point.Y / Font.LineHeight),
			0,
			_lines.Length - 1);

		UiTextLine line = _lines[lineIndex];
		float local = point.X - line.OffsetX;

		if (local <= 0f)
		{
			return line.Start;
		}

		float width = 0f;
		int index = line.Start;
		int previous = -1;

		while (index < line.End)
		{
			if (!TryMeasureNext(
				line.End,
				ref index,
				previous,
				out int consumed,
				out int codepoint,
				out float advance))
			{
				break;
			}

			float midpoint = width + (advance * 0.5f);

			if (local < midpoint)
			{
				return index - consumed;
			}

			width += advance;
			previous = codepoint;
			index += consumed;
		}

		return line.End;
	}

	/// <summary>
	/// Measures the advance between two indices on the same line.
	/// </summary>
	private float Measure(int start, int end)
	{
		float width = 0f;
		int index = start;
		int previous = -1;

		while (index < end)
		{
			if (!TryMeasureNext(
				end,
				ref index,
				previous,
				out int consumed,
				out int codepoint,
				out float advance))
			{
				break;
			}

			width += advance;
			previous = codepoint;
			index += consumed;
		}

		return width;
	}

	private bool TryMeasureNext(
		int end,
		ref int index,
		int previous,
		out int consumed,
		out int codepoint,
		out float advance)
	{
		consumed = 0;
		codepoint = 0;
		advance = 0f;

		if (index >= end)
		{
			return false;
		}

		if (Source[index] == '\t')
		{
			consumed = 1;
			codepoint = '\t';
			advance = Font.SpaceAdvance * 4f;
			return true;
		}

		Rune.DecodeFromUtf16(
			Source.AsSpan(index, end - index),
			out Rune rune,
			out consumed);

		if (consumed == 0)
		{
			consumed = 1;
			return true;
		}

		codepoint = rune.Value;
		UiGlyph glyph = Font.GetGlyph(codepoint);
		advance = glyph.Advance;

		if (previous >= 0)
		{
			advance += Font.GetKerningAdvance(previous, codepoint);
		}

		return true;
	}
}

/// <summary>
/// Lays out strings into lines and positioned glyphs.
/// </summary>
public static class UiTextLayout
{
	/// <summary>
	/// Lays out a string for a font and available width.
	/// </summary>
	public static UiTextLayoutResult Create(
		string text,
		UiFont font,
		float availableWidth,
		UiTextWrap wrap = UiTextWrap.NoWrap,
		UiTextAlignment alignment = UiTextAlignment.Start)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(font);

		if (!float.IsFinite(availableWidth) || availableWidth < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(availableWidth));
		}

		List<(int Start, int Length)> ranges = BuildLines(
			text,
			font,
			availableWidth,
			wrap == UiTextWrap.Wrap);

		float lineHeight = font.LineHeight;
		UiTextLine[] lines = new UiTextLine[ranges.Count];

		for (int index = 0; index < ranges.Count; index++)
		{
			(int start, int length) = ranges[index];
			float width = Measure(font, text, start, length);
			float offset = alignment switch
			{
				UiTextAlignment.Center => MathF.Max(
					0f,
					(availableWidth - width) * 0.5f),
				UiTextAlignment.End => MathF.Max(
					0f,
					availableWidth - width),
				_ => 0f
			};

			lines[index] = new UiTextLine(
				start,
				length,
				width,
				offset,
				index * lineHeight);
		}

		List<UiGlyphPlacement> glyphs = [];

		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			UiTextLine line = lines[lineIndex];
			float pen = line.OffsetX;
			float baseline = line.Top + font.Baseline;
			int index = line.Start;
			int previous = -1;

			while (index < line.End)
			{
				int current = index;
				Rune.DecodeFromUtf16(
					text.AsSpan(index, line.End - index),
					out Rune rune,
					out int consumed);

				if (consumed == 0)
				{
					break;
				}

				if (rune.Value == '\t')
				{
					pen += font.SpaceAdvance * 4f;
					index += 1;
					previous = -1;
					continue;
				}

				if (previous >= 0)
				{
					pen += font.GetKerningAdvance(
						previous,
						rune.Value);
				}

				UiGlyph glyph = font.GetGlyph(rune.Value);

				if (!glyph.IsEmpty)
				{
					glyphs.Add(
						new UiGlyphPlacement(
							current,
							glyph,
							new UiPoint(
								pen + glyph.Bearing.X,
								baseline + glyph.Bearing.Y)));
				}

				pen += glyph.Advance;
				previous = rune.Value;
				index += consumed;
			}
		}

		return new UiTextLayoutResult(
			text,
			font,
			availableWidth,
			lines,
			[.. glyphs]);
	}

	private static List<(int Start, int Length)> BuildLines(
		string text,
		UiFont font,
		float availableWidth,
		bool wrap)
	{
		List<(int Start, int Length)> lines = [];
		int paragraphStart = 0;

		for (int index = 0; index <= text.Length; index++)
		{
			if (index != text.Length && text[index] != '\n')
			{
				continue;
			}

			int paragraphEnd = index;

			if (paragraphEnd > paragraphStart &&
				text[paragraphEnd - 1] == '\r')
			{
				paragraphEnd--;
			}

			if (!wrap || availableWidth <= 0f)
			{
				lines.Add((
					paragraphStart,
					paragraphEnd - paragraphStart));
			}

			else
			{
				WrapParagraph(
					text,
					font,
					availableWidth,
					paragraphStart,
					paragraphEnd,
					lines);
			}

			paragraphStart = index + 1;
		}

		if (lines.Count == 0)
		{
			lines.Add((0, 0));
		}

		return lines;
	}

	private static void WrapParagraph(
		string text,
		UiFont font,
		float availableWidth,
		int start,
		int end,
		List<(int Start, int Length)> lines)
	{
		int position = start;

		while (position < end)
		{
			int cursor = position;
			int lastBreak = -1;
			int previous = -1;
			float width = 0f;

			while (cursor < end)
			{
				Rune.DecodeFromUtf16(
					text.AsSpan(cursor, end - cursor),
					out Rune rune,
					out int consumed);

				if (consumed == 0)
				{
					break;
				}

				float advance;

				if (rune.Value == '\t')
				{
					advance = font.SpaceAdvance * 4f;
				}

				else
				{
					UiGlyph glyph = font.GetGlyph(rune.Value);
					advance = glyph.Advance;

					if (previous >= 0)
					{
						advance += font.GetKerningAdvance(
							previous,
							rune.Value);
					}
				}

				if (cursor > position &&
					width + advance > availableWidth)
				{
					break;
				}

				width += advance;
				cursor += consumed;
				previous = rune.Value;

				if (IsBreakOpportunity(rune.Value) && cursor < end)
				{
					lastBreak = cursor;
				}
			}

			if (cursor < end && lastBreak > position)
			{
				cursor = lastBreak;
			}

			if (cursor == position)
			{
				Rune.DecodeFromUtf16(
					text.AsSpan(position, end - position),
					out _,
					out int consumed);

				cursor += Math.Max(consumed, 1);
			}

			lines.Add((position, cursor - position));
			position = cursor;
		}

		if (position == start)
		{
			lines.Add((start, 0));
		}
	}

	private static bool IsBreakOpportunity(int codepoint)
	{
		return codepoint is ' ' or '\t' or '-' or '/' or '\\' or ',' or ';';
	}

	private static float Measure(
		UiFont font,
		string text,
		int start,
		int length)
	{
		if (length <= 0)
		{
			return 0f;
		}

		float width = 0f;
		int index = start;
		int end = start + length;
		int previous = -1;

		while (index < end)
		{
			if (text[index] == '\t')
			{
				width += font.SpaceAdvance * 4f;
				index++;
				previous = -1;
				continue;
			}

			Rune.DecodeFromUtf16(
				text.AsSpan(index, end - index),
				out Rune rune,
				out int consumed);

			if (consumed == 0)
			{
				break;
			}

			UiGlyph glyph = font.GetGlyph(rune.Value);
			width += glyph.Advance;

			if (previous >= 0)
			{
				width += font.GetKerningAdvance(previous, rune.Value);
			}

			previous = rune.Value;
			index += consumed;
		}

		return width;
	}
}
