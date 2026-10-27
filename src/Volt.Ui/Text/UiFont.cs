using System.Buffers;
using System.Text;
using StbTrueTypeSharp;

namespace Volt.Ui.Text;

/// <summary>
/// A font size instance with CPU rasterization, measurement, and a
/// coverage atlas.
/// </summary>
/// <remarks>
/// <para>
/// Fonts are created from one of the TrueType faces embedded in
/// Volt.Ui, or from caller-supplied TTF data. Both embedded faces are
/// SIL Open Font License 1.1 software; see
/// <c>assets/fonts</c> in the repository.
/// </para>
/// <para>
/// Metrics are exposed in logical units and already account for the
/// display scale, while coverage is rasterized at physical pixel
/// resolution. Glyph coverage is packed into a single-channel atlas
/// owned by this font; the renderer uploads it as an R8 texture.
/// </para>
/// <para>
/// The implementation decodes full Unicode scalar values and is not
/// ASCII-only. It does not perform complex shaping, bidirectional
/// reordering, or glyph substitution; combining marks receive zero
/// advance but are not repositioned. Those are documented limitations
/// of this milestone, not assumptions in the API.
/// </para>
/// </remarks>
public sealed unsafe class UiFont
{
	private readonly UiFontFace _face;
	private readonly Dictionary<int, UiGlyph> _glyphs = [];
	private readonly Dictionary<long, float> _kerning = [];

	private readonly float _pixelSize;
	private readonly float _dpiScale;
	private readonly float _scale;
	private readonly float _ascent;
	private readonly float _descent;
	private readonly float _lineGap;
	private readonly float _spaceAdvance;

	public UiFont(UiFontKind kind, float size, float dpiScale = 1f)
	{
		if (kind is not (UiFontKind.Sans or UiFontKind.Monospace))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		if (!float.IsFinite(size) || size <= 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(size),
				"Font size must be positive and finite.");
		}

		if (!float.IsFinite(dpiScale) || dpiScale <= 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(dpiScale),
				"Display scale must be positive and finite.");
		}

		_face = UiFontFace.Load(kind);
		Kind = kind;
		Size = size;
		_dpiScale = dpiScale;
		_pixelSize = MathF.Max(1f, size * dpiScale);
		_scale = StbTrueType.stbtt_ScaleForPixelHeight(
			_face.Info,
			_pixelSize);

		int ascent;
		int descent;
		int lineGap;

		StbTrueType.stbtt_GetFontVMetrics(
			_face.Info,
			&ascent,
			&descent,
			&lineGap);

		_ascent = ascent * _scale / dpiScale;
		_descent = descent * _scale / dpiScale;
		_lineGap = lineGap * _scale / dpiScale;

		Atlas = new UiFontAtlas(kind);

		UiGlyph space = Rasterize(' ');
		_spaceAdvance = space.Advance;
	}

	public UiFontKind Kind { get; }

	/// <summary>
	/// Gets the requested size in logical units.
	/// </summary>
	public float Size { get; }

	/// <summary>
	/// Gets the display scale this font was created for.
	/// </summary>
	public float DpiScale => _dpiScale;

	/// <summary>
	/// Gets the family name of the underlying face.
	/// </summary>
	public string FamilyName => _face.FamilyName;

	/// <summary>
	/// Gets the font atlas containing every glyph rasterized so far.
	/// </summary>
	public UiFontAtlas Atlas { get; }

	/// <summary>
	/// Gets the ascent above the baseline in logical units.
	/// </summary>
	public float Ascent => _ascent;

	/// <summary>
	/// Gets the descent below the baseline in logical units (positive).
	/// </summary>
	public float Descent => -_descent;

	/// <summary>
	/// Gets the recommended line height in logical units.
	/// </summary>
	public float LineHeight => _ascent - _descent + _lineGap;

	/// <summary>
	/// Gets the distance from the top of a line to its baseline.
	/// </summary>
	public float Baseline => _ascent;

	/// <summary>
	/// Gets the advance width of a space in logical units.
	/// </summary>
	public float SpaceAdvance => _spaceAdvance;

	/// <summary>
	/// Gets the glyph for a Unicode scalar value, rasterizing and
	/// caching it on first use.
	/// </summary>
	public UiGlyph GetGlyph(int codepoint)
	{
		if (_glyphs.TryGetValue(codepoint, out UiGlyph glyph))
		{
			return glyph;
		}

		glyph = Rasterize(codepoint);
		_glyphs.Add(codepoint, glyph);

		return glyph;
	}

	/// <summary>
	/// Measures the advance width of one line, excluding newlines.
	/// </summary>
	public float MeasureLine(ReadOnlySpan<char> text)
	{
		float width = 0f;
		int previous = -1;

		while (!text.IsEmpty)
		{
			if (text[0] == '\t')
			{
				width += _spaceAdvance * 4f;
				previous = -1;
				text = text[1..];
				continue;
			}

			Rune.DecodeFromUtf16(
				text,
				out Rune rune,
				out int consumed);

			width += MeasureAdvance(rune.Value, previous);
			previous = rune.Value;
			text = text[consumed..];
		}

		return width;
	}

	/// <summary>
	/// Measures a string, returning the width of its widest line.
	/// </summary>
	public float MeasureText(ReadOnlySpan<char> text)
	{
		float widest = 0f;
		int lineStart = 0;

		for (int index = 0; index <= text.Length; index++)
		{
			if (index == text.Length || text[index] == '\n')
			{
				widest = MathF.Max(
					widest,
					MeasureLine(text.Slice(lineStart, index - lineStart)));

				lineStart = index + 1;
			}
		}

		return widest;
	}

	/// <summary>
	/// Gets the kerning adjustment between two codepoints in logical
	/// units.
	/// </summary>
	public float GetKerningAdvance(int left, int right)
	{
		if (left < 0 || right < 0)
		{
			return 0f;
		}

		long key = ((long)left << 32) | (uint)right;

		if (_kerning.TryGetValue(key, out float cached))
		{
			return cached;
		}

		int advance = StbTrueType.stbtt_GetCodepointKernAdvance(
			_face.Info,
			left,
			right);

		float logical = advance * _scale / _dpiScale;
		_kerning.Add(key, logical);

		return logical;
	}

	private float MeasureAdvance(int codepoint, int previous)
	{
		UiGlyph glyph = GetGlyph(codepoint);
		float kerning = previous < 0
			? 0f
			: GetKerningAdvance(previous, codepoint);

		return glyph.Advance + kerning;
	}

	private UiGlyph Rasterize(int codepoint)
	{
		StbTrueType.stbtt_fontinfo info = _face.Info;
		int glyphIndex = StbTrueType.stbtt_FindGlyphIndex(info, codepoint);

		if (glyphIndex == 0 && codepoint != '?')
		{
			codepoint = '?';
			glyphIndex = StbTrueType.stbtt_FindGlyphIndex(info, codepoint);
		}

		if (glyphIndex == 0)
		{
			return new UiGlyph(codepoint, 0f, UiRect.Empty, UiSize.Zero, UiPoint.Zero);
		}

		int advanceWidth;
		int leftSideBearing;

		StbTrueType.stbtt_GetGlyphHMetrics(
			info,
			glyphIndex,
			&advanceWidth,
			&leftSideBearing);

		float advance = advanceWidth * _scale / _dpiScale;

		int width;
		int height;
		int xOffset;
		int yOffset;

		byte* bitmap = StbTrueType.stbtt_GetGlyphBitmap(
			info,
			_scale,
			_scale,
			glyphIndex,
			&width,
			&height,
			&xOffset,
			&yOffset);

		if (bitmap is null || width <= 0 || height <= 0)
		{
			if (bitmap is not null)
			{
				StbTrueType.stbtt_FreeBitmap(bitmap, null);
			}

			return new UiGlyph(
				codepoint,
				advance,
				UiRect.Empty,
				UiSize.Zero,
				UiPoint.Zero);
		}

		try
		{
			if (!Atlas.TryPack(width, height, out int atlasX, out int atlasY))
			{
				return new UiGlyph(
					codepoint,
					advance,
					UiRect.Empty,
					UiSize.Zero,
					UiPoint.Zero);
			}

			ReadOnlySpan<byte> coverage = new(bitmap, width * height);
			Atlas.Write(atlasX, atlasY, width, height, coverage);

			float inverse = 1f / _dpiScale;
			UiRect uv = new(
				atlasX / (float)UiFontAtlas.Size,
				atlasY / (float)UiFontAtlas.Size,
				width / (float)UiFontAtlas.Size,
				height / (float)UiFontAtlas.Size);

			return new UiGlyph(
				codepoint,
				advance,
				uv,
				new UiSize(width * inverse, height * inverse),
				new UiPoint(xOffset * inverse, yOffset * inverse));
		}

		finally
		{
			StbTrueType.stbtt_FreeBitmap(bitmap, null);
		}
	}
}
