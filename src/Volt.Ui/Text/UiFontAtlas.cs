namespace Volt.Ui.Text;

/// <summary>
/// A single-channel coverage atlas populated by one font size.
/// </summary>
/// <remarks>
/// The renderer uploads <see cref="Pixels"/> as an R8 texture. Glyph
/// positions never move once packed, so an existing draw list stays
/// valid; new glyphs only append to unused atlas space. The atlas
/// reports a new <see cref="Revision"/> whenever a glyph is added, so
/// the renderer can upload only after the atlas changed.
/// </remarks>
public sealed class UiFontAtlas
{
	/// <summary>
	/// The fixed atlas edge in texels.
	/// </summary>
	public const int Size = 1024;

	private int _shelfX;
	private int _shelfY = 1;
	private int _shelfHeight;

	internal UiFontAtlas(UiFontKind kind)
	{
		Kind = kind;
		Pixels = new byte[Size * Size];
	}

	public UiFontKind Kind { get; }

	public byte[] Pixels { get; }

	public int Width => Size;

	public int Height => Size;

	/// <summary>
	/// Gets a value that changes whenever new coverage was written.
	/// </summary>
	public int Revision { get; private set; }

	/// <summary>
	/// Gets whether at least one glyph could not fit.
	/// </summary>
	public bool IsFull { get; private set; }

	internal bool TryPack(
		int width,
		int height,
		out int x,
		out int y)
	{
		x = 0;
		y = 0;

		if (width <= 0 || height <= 0)
		{
			return false;
		}

		if (width + 2 > Size || height + 2 > Size)
		{
			IsFull = true;
			return false;
		}

		if (_shelfX + width + 1 > Size)
		{
			_shelfX = 1;
			_shelfY += _shelfHeight + 1;
			_shelfHeight = 0;
		}

		if (_shelfY + height + 1 > Size)
		{
			IsFull = true;
			return false;
		}

		x = _shelfX;
		y = _shelfY;
		_shelfX += width + 1;
		_shelfHeight = Math.Max(_shelfHeight, height);

		return true;
	}

	internal void Write(
		int x,
		int y,
		int width,
		int height,
		ReadOnlySpan<byte> coverage)
	{
		for (int row = 0; row < height; row++)
		{
			int source = row * width;
			int destination = ((y + row) * Size) + x;

			coverage
				.Slice(source, width)
				.CopyTo(Pixels.AsSpan(destination, width));
		}

		Revision++;
	}
}
