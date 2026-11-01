using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using Volt.Editor.Core.Assets;

namespace Volt.Editor.Core.Tests;

/// <summary>
/// Covers PNG decoding: colour types, every scanline filter, rejection
/// of formats this build does not claim to support, and sampling.
/// </summary>
public sealed class PngDecoderTests
{
	[Fact]
	public void ItDecodesAnRgbImage()
	{
		byte[] png = BuildPng(
			2,
			2,
			colorType: 2,
			rows:
			[
				[255, 0, 0, 0, 255, 0],
				[0, 0, 255, 255, 255, 255]
			]);

		PngImage image = PngDecoder.Decode(
			new MemoryStream(png));

		Assert.Equal(2, image.Width);
		Assert.Equal(2, image.Height);
		Assert.Equal(
			new Vector4(1f, 0f, 0f, 1f),
			image.GetPixel(0, 0));
		Assert.Equal(
			new Vector4(0f, 1f, 0f, 1f),
			image.GetPixel(1, 0));
		Assert.Equal(
			new Vector4(0f, 0f, 1f, 1f),
			image.GetPixel(0, 1));
		Assert.Equal(
			new Vector4(1f, 1f, 1f, 1f),
			image.GetPixel(1, 1));
	}

	[Fact]
	public void ItDecodesRgbaAndKeepsAlpha()
	{
		byte[] png = BuildPng(
			1,
			1,
			colorType: 6,
			rows: [[10, 20, 30, 40]]);

		PngImage image = PngDecoder.Decode(
			new MemoryStream(png));

		Assert.Equal(
			new Vector4(10f / 255f, 20f / 255f, 30f / 255f, 40f / 255f),
			image.GetPixel(0, 0));
	}

	[Fact]
	public void ItDecodesGreyscaleAndGreyscaleWithAlpha()
	{
		PngImage grey = PngDecoder.Decode(new MemoryStream(
			BuildPng(1, 1, colorType: 0, rows: [[120]])));

		Assert.Equal(
			new Vector4(120f / 255f, 120f / 255f, 120f / 255f, 1f),
			grey.GetPixel(0, 0));

		PngImage greyAlpha = PngDecoder.Decode(new MemoryStream(
			BuildPng(1, 1, colorType: 4, rows: [[90, 200]])));

		Assert.Equal(
			new Vector4(90f / 255f, 90f / 255f, 90f / 255f, 200f / 255f),
			greyAlpha.GetPixel(0, 0));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void ItReconstructsEveryScanlineFilter(int filter)
	{
		// Four pixels per row so the filters have a predecessor to
		// work against. Encoded with the chosen filter, so decoding has
		// to reconstruct the same pixels for every row.
		byte[][] expected =
		[
			[10, 20, 30, 40],
			[50, 60, 70, 80]
		];

		byte[] png = BuildFilteredPng(
			expected[0].Length,
			expected.Length,
			colorType: 0,
			expected,
			filter);

		PngImage image = PngDecoder.Decode(
			new MemoryStream(png));

		for (int y = 0; y < expected.Length; y++)
		{
			for (int x = 0; x < expected[y].Length; x++)
			{
				Assert.Equal(
					expected[y][x] / 255f,
					image.GetPixel(x, y).X);
			}
		}
	}

	[Fact]
	public void SampleClampsOutOfRangeCoordinates()
	{
		byte[] png = BuildPng(
			2,
			1,
			colorType: 0,
			rows: [[0, 255]]);

		PngImage image = PngDecoder.Decode(
			new MemoryStream(png));

		// Clamping lands on the nearest edge texel, not on an error.
		Assert.Equal(0f, image.Sample(-4f, 9f).X);
		Assert.Equal(0f, image.Sample(float.NaN, 0.5f).X);
		Assert.Equal(0f, image.Sample(0f, 0.5f).X);
		Assert.Equal(0f, image.Sample(0.49f, 0.5f).X);
		Assert.Equal(1f, image.Sample(0.51f, 0.5f).X);
	}

	[Fact]
	public void SomethingThatIsNotAPngIsRejected()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(
			() => PngDecoder.Decode(
				new MemoryStream(Encoding.ASCII.GetBytes("GIF89a"))));

		Assert.Contains("not a PNG", error.Message);
	}

	[Fact]
	public void SixteenBitImagesAreRejected()
	{
		byte[] png = BuildPng(
			1,
			1,
			colorType: 0,
			rows: [[1, 2]],
			bitDepth: 16);

		InvalidDataException error = Assert.Throws<InvalidDataException>(
			() => PngDecoder.Decode(new MemoryStream(png)));

		Assert.Contains("bit depth 16", error.Message);
	}

	[Fact]
	public void PaletteImagesAreRejected()
	{
		byte[] png = BuildPng(
			1,
			1,
			colorType: 3,
			rows: [[0]]);

		InvalidDataException error = Assert.Throws<InvalidDataException>(
			() => PngDecoder.Decode(new MemoryStream(png)));

		Assert.Contains("Palette", error.Message);
	}

	[Fact]
	public void InterlacedImagesAreRejected()
	{
		byte[] png = BuildPng(
			1,
			1,
			colorType: 0,
			rows: [[0]],
			interlace: 1);

		InvalidDataException error = Assert.Throws<InvalidDataException>(
			() => PngDecoder.Decode(new MemoryStream(png)));

		Assert.Contains("Interlaced", error.Message);
	}

	[Fact]
	public void TruncatedImageDataIsRejected()
	{
		byte[] png = BuildPng(
			2,
			2,
			colorType: 0,
			rows: [[1, 2], [3, 4]]);

		// Drop the last four bytes of compressed data by cutting the
		// IDAT payload down; the zlib stream then ends early.
		byte[] truncated = CutImageData(png);

		Assert.ThrowsAny<Exception>(() => PngDecoder.Decode(
			new MemoryStream(truncated)));
	}

	private static byte[] BuildPng(
		int width,
		int height,
		byte colorType,
		byte[][] rows,
		byte bitDepth = 8,
		byte interlace = 0)
	{
		byte[][] filtered = new byte[rows.Length][];

		for (int row = 0; row < rows.Length; row++)
		{
			filtered[row] = [0, .. rows[row]];
		}

		return Assemble(
			width,
			height,
			colorType,
			bitDepth,
			interlace,
			filtered);
	}

	private static byte[] BuildFilteredPng(
		int width,
		int height,
		byte colorType,
		byte[][] rows,
		int filter)
	{
		byte[][] filtered = new byte[rows.Length][];

		for (int row = 0; row < rows.Length; row++)
		{
			byte[] line = rows[row];
			byte[] encoded = new byte[line.Length + 1];
			encoded[0] = (byte)filter;

			for (int index = 0; index < line.Length; index++)
			{
				int left = index > 0 ? line[index - 1] : 0;
				int above = row > 0 ? rows[row - 1][index] : 0;
				int upperLeft = row > 0 && index > 0
					? rows[row - 1][index - 1]
					: 0;

				int predictor = filter switch
				{
					1 => left,
					2 => above,
					3 => (left + above) / 2,
					_ => Paeth(left, above, upperLeft)
				};

				encoded[index + 1] =
					(byte)((line[index] - predictor) & 0xFF);
			}

			filtered[row] = encoded;
		}

		return Assemble(width, height, colorType, 8, 0, filtered);
	}

	private static int Paeth(int left, int above, int upperLeft)
	{
		int estimate = left + above - upperLeft;
		int a = Math.Abs(estimate - left);
		int b = Math.Abs(estimate - above);
		int c = Math.Abs(estimate - upperLeft);

		return a <= b && a <= c ? left : b <= c ? above : upperLeft;
	}

	private static byte[] Assemble(
		int width,
		int height,
		byte colorType,
		byte bitDepth,
		byte interlace,
		byte[][] filteredRows)
	{
		using MemoryStream output = new();
		output.Write(
			[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(0, 4),
			(uint)width);
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(4, 4),
			(uint)height);
		header[8] = bitDepth;
		header[9] = colorType;
		header[10] = 0;
		header[11] = 0;
		header[12] = interlace;

		WriteChunk(output, "IHDR", header);

		using MemoryStream uncompressed = new();

		foreach (byte[] row in filteredRows)
		{
			uncompressed.Write(row);
		}

		using MemoryStream compressed = new();

		using (ZLibStream deflate = new(
			compressed,
			CompressionLevel.Optimal,
			leaveOpen: true))
		{
			uncompressed.Position = 0;
			uncompressed.CopyTo(deflate);
		}

		WriteChunk(output, "IDAT", compressed.ToArray());
		WriteChunk(output, "IEND", []);

		return output.ToArray();
	}

	private static void WriteChunk(
		Stream output,
		string type,
		byte[] data)
	{
		Span<byte> length = stackalloc byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(
			length,
			(uint)data.Length);

		output.Write(length);
		output.Write(Encoding.ASCII.GetBytes(type));
		output.Write(data);

		// The decoder does not verify CRCs, so zeros are enough for a
		// test fixture.
		output.Write(new byte[4]);
	}

	private static byte[] CutImageData(byte[] png)
	{
		// Zero-length IDAT: the stream then ends before the header's
		// declared scanline count.
		using MemoryStream output = new();
		output.Write(png, 0, 8);

		byte[] header = new byte[13];
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(0, 4),
			2u);
		BinaryPrimitives.WriteUInt32BigEndian(
			header.AsSpan(4, 4),
			2u);
		header[8] = 8;
		header[9] = 0;

		WriteChunk(output, "IHDR", header);
		WriteChunk(output, "IDAT", []);
		WriteChunk(output, "IEND", []);

		return output.ToArray();
	}
}
