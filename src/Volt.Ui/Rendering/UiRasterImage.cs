using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// An RGBA image produced by the headless rasterizer.
/// </summary>
/// <remarks>
/// Pixels are eight-bit straight alpha in RGBA order, which is what a
/// PNG scanline stores, so encoding is a copy rather than a conversion.
/// The image is immutable to callers: blending happens inside the
/// rasterizer through its own methods.
/// </remarks>
public sealed class UiRasterImage
{
	private static readonly uint[] s_crcTable = CreateCrcTable();

	private readonly byte[] _pixels;

	internal UiRasterImage(int width, int height)
	{
		if (width <= 0 || height <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		Width = width;
		Height = height;
		_pixels = new byte[width * height * 4];
	}

	/// <summary>
	/// Creates an empty image for a CPU renderer to fill.
	/// </summary>
	/// <remarks>
	/// The pixels start transparent black. A renderer that does not
	/// cover every pixel should clear the image first with
	/// <see cref="Fill"/>.
	/// </remarks>
	public static UiRasterImage Create(int width, int height)
	{
		return new UiRasterImage(width, height);
	}

	public int Width { get; }

	public int Height { get; }

	/// <summary>
	/// Gets the pixels as RGBA rows, top to bottom.
	/// </summary>
	public ReadOnlySpan<byte> Pixels => _pixels;

	/// <summary>
	/// Reads one pixel.
	/// </summary>
	public UiColor GetPixel(int x, int y)
	{
		if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		int offset = ((y * Width) + x) * 4;

		return new UiColor(
			_pixels[offset],
			_pixels[offset + 1],
			_pixels[offset + 2],
			_pixels[offset + 3]);
	}

	/// <summary>
	/// Reads an eight-bit RGBA, non-interlaced PNG.
	/// </summary>
	/// <remarks>
	/// This is the reader for the format <see cref="EncodePng"/> writes,
	/// which is what a committed snapshot baseline is. Every standard
	/// scanline filter is accepted, so a baseline re-encoded by an image
	/// tool still loads; palette, sixteen-bit, and interlaced files are
	/// rejected rather than misread.
	/// </remarks>
	public static UiRasterImage DecodePng(ReadOnlySpan<byte> png)
	{
		ReadOnlySpan<byte> signature =
			[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

		if (png.Length < signature.Length ||
			!png[..signature.Length].SequenceEqual(signature))
		{
			throw new InvalidDataException("The data is not a PNG file.");
		}

		int width = 0;
		int height = 0;
		int bitDepth = 0;
		int colorType = 0;
		int interlace = 0;
		bool complete = false;
		int offset = signature.Length;

		using MemoryStream compressed = new();

		while (offset + 12 <= png.Length)
		{
			int length = BinaryPrimitives.ReadInt32BigEndian(
				png.Slice(offset, 4));

			string type = Encoding.ASCII.GetString(
				png.Slice(offset + 4, 4));

			offset += 8;

			if (length < 0 || offset + length > png.Length - 4)
			{
				throw new InvalidDataException(
					"A PNG chunk runs past the end of the file.");
			}

			ReadOnlySpan<byte> data = png.Slice(offset, length);

			switch (type)
			{
				case "IHDR":
					width = BinaryPrimitives.ReadInt32BigEndian(data);
					height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
					bitDepth = data[8];
					colorType = data[9];
					interlace = data[12];
					break;

				case "IDAT":
					compressed.Write(data);
					break;

				case "IEND":
					complete = true;
					break;
			}

			offset += length + 4;

			if (complete)
			{
				break;
			}
		}

		if (width <= 0 || height <= 0 || !complete)
		{
			throw new InvalidDataException(
				"The PNG is missing its header or its end chunk.");
		}

		if (bitDepth != 8 || colorType != 6 || interlace != 0)
		{
			throw new NotSupportedException(
				"Only eight-bit RGBA, non-interlaced PNGs are supported.");
		}

		int stride = width * 4;
		byte[] scanlines = new byte[(stride + 1) * height];

		compressed.Position = 0;

		using (ZLibStream zlib = new(
			compressed,
			CompressionMode.Decompress))
		{
			int read = 0;

			while (read < scanlines.Length)
			{
				int count = zlib.Read(
					scanlines,
					read,
					scanlines.Length - read);

				if (count <= 0)
				{
					break;
				}

				read += count;
			}

			if (read != scanlines.Length)
			{
				throw new InvalidDataException(
					"The PNG's image data is incomplete.");
			}
		}

		UiRasterImage image = new(width, height);
		image.Unfilter(scanlines);

		return image;
	}

	/// <summary>
	/// Writes the image as a PNG file.
	/// </summary>
	public void SavePng(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		File.WriteAllBytes(path, EncodePng());
	}

	/// <summary>
	/// Encodes the image as an eight-bit RGBA PNG.
	/// </summary>
	/// <remarks>
	/// The only compression the format needs is a zlib stream, which the
	/// runtime already provides, so the editor gains screenshots without
	/// an image dependency.
	/// </remarks>
	public byte[] EncodePng()
	{
		using MemoryStream output = new();

		output.Write([
			0x89, 0x50, 0x4E, 0x47,
			0x0D, 0x0A, 0x1A, 0x0A
		]);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, Width);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
		header[8] = 8;
		header[9] = 6;
		header[10] = 0;
		header[11] = 0;
		header[12] = 0;

		WriteChunk(output, "IHDR", header);

		byte[] scanlines = new byte[(Width * 4 + 1) * Height];
		int cursor = 0;
		int stride = Width * 4;

		for (int y = 0; y < Height; y++)
		{
			// Filter type 0 (none) keeps the encoder simple; the stream
			// still compresses well because UI frames are flat colour.
			scanlines[cursor++] = 0;
			_pixels.AsSpan(y * stride, stride).CopyTo(
				scanlines.AsSpan(cursor));

			cursor += stride;
		}

		using (MemoryStream compressed = new())
		{
			using (ZLibStream zlib = new(
				compressed,
				CompressionLevel.Optimal,
				leaveOpen: true))
			{
				zlib.Write(scanlines);
			}

			WriteChunk(output, "IDAT", compressed.ToArray());
		}

		WriteChunk(output, "IEND", []);

		return output.ToArray();
	}

	/// <summary>
	/// Writes one pixel without blending.
	/// </summary>
	public void SetPixel(int x, int y, UiColor color)
	{
		if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
		{
			throw new ArgumentOutOfRangeException(nameof(x));
		}

		int offset = ((y * Width) + x) * 4;

		_pixels[offset] = color.R;
		_pixels[offset + 1] = color.G;
		_pixels[offset + 2] = color.B;
		_pixels[offset + 3] = color.A;
	}

	/// <summary>
	/// Replaces every pixel with one opaque coverage value.
	/// </summary>
	public void Fill(UiColor color)
	{
		Clear(color);
	}

	internal void Clear(UiColor color)
	{
		for (int index = 0; index < _pixels.Length; index += 4)
		{
			_pixels[index] = color.R;
			_pixels[index + 1] = color.G;
			_pixels[index + 2] = color.B;
			_pixels[index + 3] = color.A;
		}
	}

	/// <summary>
	/// Composites one colour over a pixel with straight alpha.
	/// </summary>
	internal void Blend(int x, int y, UiColor color, float alpha)
	{
		if (alpha <= 0f)
		{
			return;
		}

		alpha = MathF.Min(1f, alpha);

		int offset = ((y * Width) + x) * 4;
		float destinationAlpha = _pixels[offset + 3] / 255f;
		float source = alpha * (color.A / 255f);

		if (source <= 0f)
		{
			return;
		}

		float total = source + (destinationAlpha * (1f - source));

		_pixels[offset] = Mix(color.R, _pixels[offset], source, total);
		_pixels[offset + 1] = Mix(color.G, _pixels[offset + 1], source, total);
		_pixels[offset + 2] = Mix(color.B, _pixels[offset + 2], source, total);
		_pixels[offset + 3] = (byte)Math.Clamp(
			(int)MathF.Round(total * 255f),
			0,
			255);
	}

	private static byte Mix(
		byte sourceChannel,
		byte destinationChannel,
		float sourceAlpha,
		float totalAlpha)
	{
		if (totalAlpha <= 0f)
		{
			return 0;
		}

		float value =
			((sourceChannel * sourceAlpha) +
				(destinationChannel * (1f - sourceAlpha))) / totalAlpha;

		return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
	}

	private void Unfilter(ReadOnlySpan<byte> scanlines)
	{
		int stride = Width * 4;

		for (int y = 0; y < Height; y++)
		{
			int rowOffset = y * (stride + 1);
			byte filter = scanlines[rowOffset];
			ReadOnlySpan<byte> source = scanlines.Slice(
				rowOffset + 1,
				stride);

			Span<byte> row = _pixels.AsSpan(y * stride, stride);

			ReadOnlySpan<byte> previous = y > 0
				? _pixels.AsSpan((y - 1) * stride, stride)
				: ReadOnlySpan<byte>.Empty;

			for (int index = 0; index < stride; index++)
			{
				int left = index >= 4 ? row[index - 4] : 0;
				int above = previous.IsEmpty ? 0 : previous[index];

				int corner = index >= 4 && !previous.IsEmpty
					? previous[index - 4]
					: 0;

				row[index] = filter switch
				{
					0 => source[index],
					1 => (byte)(source[index] + left),
					2 => (byte)(source[index] + above),
					3 => (byte)(source[index] + ((left + above) / 2)),
					4 => (byte)(source[index] + Paeth(left, above, corner)),
					_ => throw new InvalidDataException(
						$"Unknown PNG filter {filter}.")
				};
			}
		}
	}

	private static int Paeth(int left, int above, int corner)
	{
		int estimate = left + above - corner;
		int leftDistance = Math.Abs(estimate - left);
		int aboveDistance = Math.Abs(estimate - above);
		int cornerDistance = Math.Abs(estimate - corner);

		if (leftDistance <= aboveDistance &&
			leftDistance <= cornerDistance)
		{
			return left;
		}

		return aboveDistance <= cornerDistance ? above : corner;
	}

	private static void WriteChunk(
		Stream stream,
		string type,
		ReadOnlySpan<byte> data)
	{
		Span<byte> buffer = stackalloc byte[4];

		BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
		stream.Write(buffer);

		Span<byte> tag = stackalloc byte[4];

		for (int index = 0; index < 4; index++)
		{
			tag[index] = (byte)type[index];
		}

		stream.Write(tag);
		stream.Write(data);

		BinaryPrimitives.WriteUInt32BigEndian(
			buffer,
			Crc32(tag, data));

		stream.Write(buffer);
	}

	private static uint Crc32(
		ReadOnlySpan<byte> type,
		ReadOnlySpan<byte> data)
	{
		uint crc = 0xFFFFFFFFu;

		crc = Update(crc, type);
		crc = Update(crc, data);

		return crc ^ 0xFFFFFFFFu;
	}

	private static uint Update(uint crc, ReadOnlySpan<byte> data)
	{
		foreach (byte value in data)
		{
			crc = s_crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
		}

		return crc;
	}

	private static uint[] CreateCrcTable()
	{
		uint[] table = new uint[256];

		for (uint index = 0; index < table.Length; index++)
		{
			uint value = index;

			for (int bit = 0; bit < 8; bit++)
			{
				value = (value & 1) != 0
					? 0xEDB88320u ^ (value >> 1)
					: value >> 1;
			}

			table[index] = value;
		}

		return table;
	}
}
