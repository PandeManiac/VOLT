using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace Volt.Editor.Core.Assets;

/// <summary>
/// One decoded PNG image as 8-bit RGBA pixels.
/// </summary>
/// <remarks>
/// Pixels are stored row-major from the top-left corner, which is the
/// order PNG defines. Sampling flips nothing: a mesh's texture
/// coordinates are mapped at the sampling site, so the image itself
/// stays a faithful copy of the file.
/// </remarks>
public sealed class PngImage
{
	private readonly byte[] _pixels;

	internal PngImage(int width, int height, byte[] pixels)
	{
		Width = width;
		Height = height;
		_pixels = pixels;
	}

	/// <summary>Gets the image width in pixels.</summary>
	public int Width { get; }

	/// <summary>Gets the image height in pixels.</summary>
	public int Height { get; }

	/// <summary>Gets the RGBA bytes, four per pixel.</summary>
	public ReadOnlySpan<byte> Pixels => _pixels;

	/// <summary>
	/// Reads one pixel.
	/// </summary>
	/// <param name="x">The column, from zero.</param>
	/// <param name="y">The row, from zero.</param>
	/// <returns>The pixel as linear 0..1 components.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The coordinates lie outside the image.
	/// </exception>
	public Vector4 GetPixel(int x, int y)
	{
		if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
		{
			throw new ArgumentOutOfRangeException(
				nameof(x),
				$"({x}, {y}) is outside a {Width}x{Height} image.");
		}

		int offset = ((y * Width) + x) * 4;

		return new Vector4(
			_pixels[offset] / 255f,
			_pixels[offset + 1] / 255f,
			_pixels[offset + 2] / 255f,
			_pixels[offset + 3] / 255f);
	}

	/// <summary>
	/// Samples the image with clamped coordinates.
	/// </summary>
	/// <param name="u">The horizontal coordinate, 0..1 across the image.</param>
	/// <param name="v">The vertical coordinate, 0..1 down the image.</param>
	/// <returns>The nearest pixel's components.</returns>
	/// <remarks>
	/// Nearest sampling keeps a texel readable at any zoom, which is what
	/// a material preview needs. Coordinates outside 0..1 clamp to the
	/// edge, so a mesh with slightly out-of-range coordinates shows the
	/// border texel instead of wrapping unrelated content.
	/// </remarks>
	public Vector4 Sample(float u, float v)
	{
		float clampedU = float.IsFinite(u) ? Math.Clamp(u, 0f, 1f) : 0f;
		float clampedV = float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : 0f;

		int x = Math.Clamp((int)(clampedU * Width), 0, Width - 1);
		int y = Math.Clamp((int)(clampedV * Height), 0, Height - 1);

		return GetPixel(x, y);
	}
}

/// <summary>
/// Decodes 8-bit, non-interlaced PNG files.
/// </summary>
/// <remarks>
/// <para>
/// PNG is the format a developer is most likely to have, and the
/// compressed data is ordinary zlib, so decoding needs no dependency
/// beyond the runtime. Greyscale, greyscale-with-alpha, RGB and RGBA are
/// read; palette, 16-bit, and interlaced files are rejected with a clear
/// message rather than decoded approximately.
/// </para>
/// <para>
/// Chunk CRCs are not verified. A corrupt file is caught by the zlib
/// stream or by a short scanline, and checking every CRC on a large
/// texture costs more than the failure it detects.
/// </para>
/// </remarks>
public static class PngDecoder
{
	private static readonly byte[] s_signature =
		[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

	/// <summary>
	/// Decodes one PNG file.
	/// </summary>
	/// <param name="fullPath">The file to read.</param>
	/// <returns>The decoded image.</returns>
	/// <exception cref="InvalidDataException">
	/// The file is not an 8-bit, non-interlaced PNG this build supports.
	/// </exception>
	public static PngImage DecodeFile(string fullPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

		using FileStream stream = File.OpenRead(fullPath);

		return Decode(stream);
	}

	/// <summary>
	/// Decodes one PNG stream.
	/// </summary>
	/// <param name="stream">The PNG bytes.</param>
	/// <returns>The decoded image.</returns>
	/// <exception cref="InvalidDataException">
	/// The data is not an 8-bit, non-interlaced PNG this build supports.
	/// </exception>
	public static PngImage Decode(Stream stream)
	{
		ArgumentNullException.ThrowIfNull(stream);

		byte[] signature = new byte[s_signature.Length];
		int read = stream.ReadAtLeast(
			signature,
			signature.Length,
			throwOnEndOfStream: false);

		if (read != signature.Length ||
			!signature.AsSpan().SequenceEqual(s_signature))
		{
			throw new InvalidDataException(
				"The file is not a PNG image.");
		}

		int width = 0;
		int height = 0;
		int channels = 0;
		bool sawHeader = false;
		MemoryStream compressed = new();

		while (true)
		{
			byte[] header = new byte[8];
			int headerRead = stream.ReadAtLeast(
				header,
				header.Length,
				throwOnEndOfStream: false);

			if (headerRead == 0)
			{
				throw new InvalidDataException(
					"The PNG has no IEND chunk.");
			}

			if (headerRead != header.Length)
			{
				throw new InvalidDataException(
					"The PNG ends in the middle of a chunk header.");
			}

			uint length = BinaryPrimitives.ReadUInt32BigEndian(header);

			if (length > 64 * 1024 * 1024)
			{
				throw new InvalidDataException(
					"The PNG has an unreasonable chunk length.");
			}

			string type = System.Text.Encoding.ASCII.GetString(
				header,
				4,
				4);

			byte[] data = new byte[length];

			if (length > 0)
			{
				stream.ReadAtLeast(
					data,
					data.Length,
					throwOnEndOfStream: false);
			}

			// The CRC follows the data; the decoder does not verify it.
			stream.ReadExactly(new byte[4], 0, 4);

			switch (type)
			{
				case "IHDR":
					(sawHeader, width, height, channels) =
						ReadHeader(data);
					break;

				case "IDAT":
					if (!sawHeader)
					{
						throw new InvalidDataException(
							"The PNG has image data before its header.");
					}

					compressed.Write(data);
					break;

				case "IEND":
					if (!sawHeader)
					{
						throw new InvalidDataException(
							"The PNG has no header.");
					}

					byte[] raw = Inflate(
						compressed.ToArray(),
						channels,
						width,
						height);

					return new PngImage(
						width,
						height,
						ToRgba(raw, channels, width, height));

				default:
					// Ancillary, palette, and colour-profile chunks are
					// skipped; the header already rejected the ones that
					// change how pixels are interpreted.
					break;
			}
		}
	}

	private static (bool Seen, int Width, int Height, int Channels)
		ReadHeader(byte[] data)
	{
		if (data.Length != 13)
		{
			throw new InvalidDataException(
				"The PNG header is malformed.");
		}

		int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
			data.AsSpan(0, 4)));
		int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
			data.AsSpan(4, 4)));
		byte depth = data[8];
		byte colorType = data[9];
		byte compression = data[10];
		byte filtering = data[11];
		byte interlace = data[12];

		if (width <= 0 || height <= 0)
		{
			throw new InvalidDataException(
				"The PNG has an empty image size.");
		}

		if (width > 16384 || height > 16384)
		{
			throw new InvalidDataException(
				"The PNG is larger than 16384 pixels on a side.");
		}

		if (depth != 8)
		{
			throw new InvalidDataException(
				$"The PNG has bit depth {depth}; only 8-bit PNGs are " +
				"supported.");
		}

		if (compression != 0 || filtering != 0)
		{
			throw new InvalidDataException(
				"The PNG uses an unsupported compression or filter method.");
		}

		if (interlace != 0)
		{
			throw new InvalidDataException(
				"Interlaced PNGs are not supported.");
		}

		int channels = colorType switch
		{
			0 => 1,
			2 => 3,
			4 => 2,
			6 => 4,
			3 => throw new InvalidDataException(
				"Palette PNGs are not supported; save the image as RGB " +
				"or RGBA."),
			_ => throw new InvalidDataException(
				$"The PNG colour type {colorType} is not defined.")
		};

		return (true, width, height, channels);
	}

	private static byte[] Inflate(
		byte[] compressed,
		int channels,
		int width,
		int height)
	{
		int stride = width * channels;
		int expected = (stride + 1) * height;
		byte[] raw = new byte[expected];

		using MemoryStream input = new(compressed);
		using ZLibStream inflater = new(input, CompressionMode.Decompress);

		int total = 0;

		while (total < expected)
		{
			int count = inflater.Read(raw, total, expected - total);

			if (count == 0)
			{
				break;
			}

			total += count;
		}

		if (total < expected)
		{
			throw new InvalidDataException(
				"The PNG image data is shorter than its header declares.");
		}

		return raw;
	}

	private static byte[] ToRgba(
		byte[] raw,
		int channels,
		int width,
		int height)
	{
		int stride = width * channels;
		byte[] filtered = new byte[stride * height];

		for (int y = 0; y < height; y++)
		{
			int source = y * (stride + 1);
			int filter = raw[source];

			Buffer.BlockCopy(
				raw,
				source + 1,
				filtered,
				y * stride,
				stride);

			Unfilter(filtered, y, stride, channels, filter);
		}

		byte[] pixels = new byte[width * height * 4];

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int source = ((y * width) + x) * channels;
				int target = ((y * width) + x) * 4;

				switch (channels)
				{
					case 1:
						byte grey = filtered[source];
						pixels[target] = grey;
						pixels[target + 1] = grey;
						pixels[target + 2] = grey;
						pixels[target + 3] = 255;
						break;

					case 2:
						byte greyAlpha = filtered[source];
						pixels[target] = greyAlpha;
						pixels[target + 1] = greyAlpha;
						pixels[target + 2] = greyAlpha;
						pixels[target + 3] = filtered[source + 1];
						break;

					case 3:
						pixels[target] = filtered[source];
						pixels[target + 1] = filtered[source + 1];
						pixels[target + 2] = filtered[source + 2];
						pixels[target + 3] = 255;
						break;

					default:
						pixels[target] = filtered[source];
						pixels[target + 1] = filtered[source + 1];
						pixels[target + 2] = filtered[source + 2];
						pixels[target + 3] = filtered[source + 3];
						break;
				}
			}
		}

		return pixels;
	}

	private static void Unfilter(
		byte[] bytes,
		int row,
		int stride,
		int channels,
		int filter)
	{
		int start = row * stride;
		int previous = start - stride;

		for (int index = 0; index < stride; index++)
		{
			int left = index >= channels
				? bytes[start + index - channels]
				: 0;

			int above = row > 0 ? bytes[previous + index] : 0;

			int upperLeft = row > 0 && index >= channels
				? bytes[previous + index - channels]
				: 0;

			bytes[start + index] = (byte)(bytes[start + index] +
				filter switch
				{
					0 => 0,
					1 => left,
					2 => above,
					3 => (left + above) / 2,
					4 => Paeth(left, above, upperLeft),
					_ => throw new InvalidDataException(
						$"The PNG uses unknown filter type {filter}.")
				});
		}
	}

	private static int Paeth(int left, int above, int upperLeft)
	{
		int estimate = left + above - upperLeft;

		int leftDistance = Math.Abs(estimate - left);
		int aboveDistance = Math.Abs(estimate - above);
		int upperLeftDistance = Math.Abs(estimate - upperLeft);

		if (leftDistance <= aboveDistance &&
			leftDistance <= upperLeftDistance)
		{
			return left;
		}

		return aboveDistance <= upperLeftDistance ? above : upperLeft;
	}
}
