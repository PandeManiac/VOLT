using System.Buffers.Binary;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Tests;

/// <summary>
/// Covers the headless rasterizer: solid geometry, clipping, the
/// analytic edges, text coverage, and PNG encoding.
/// </summary>
public sealed class UiRasterizerTests
{
	[Fact]
	public void Rectangle_FillsItsPixelsAndLeavesTheRestAlone()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(64f, 64f));

		drawList.AddRect(
			new UiRect(10f, 12f, 20f, 16f),
			UiColor.FromRgb(0xFF0000));

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		Assert.Equal(new UiColor(255, 0, 0), image.GetPixel(15, 20));
		Assert.Equal(UiColor.Black, image.GetPixel(2, 2));
		Assert.Equal(UiColor.Black, image.GetPixel(40, 20));
	}

	[Fact]
	public void ClippedGeometry_StopsAtTheClipRectangle()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(64f, 64f));

		drawList.PushClip(new UiRect(0f, 0f, 20f, 20f));
		drawList.AddRect(new UiRect(0f, 0f, 64f, 64f), UiColor.White);
		drawList.PopClip();

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		Assert.Equal(UiColor.White, image.GetPixel(8, 8));
		Assert.Equal(UiColor.Black, image.GetPixel(30, 30));
	}

	[Fact]
	public void RoundedRectangle_LeavesTheCornerOutsideTheArc()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(64f, 64f));

		drawList.AddRoundedRect(
			new UiRect(4f, 4f, 40f, 40f),
			12f,
			UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		Assert.Equal(UiColor.Black, image.GetPixel(5, 5));
		Assert.Equal(UiColor.White, image.GetPixel(24, 24));
	}

	[Fact]
	public void Line_CoversItsPathButNotItsSurroundings()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(64f, 64f));

		drawList.AddLine(
			new UiPoint(4f, 32f),
			new UiPoint(60f, 32f),
			3f,
			UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		Assert.Equal(UiColor.White, image.GetPixel(32, 32));
		Assert.Equal(UiColor.Black, image.GetPixel(32, 10));
	}

	[Fact]
	public void Text_DrawsGlyphCoverage()
	{
		UiTheme theme = UiTheme.CreateDark();

		UiTextLayoutResult layout = UiTextLayout.Create(
			"VOLT",
			theme.Fonts.Body,
			200f,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		UiDrawList drawList = new();
		drawList.Begin(new UiSize(128f, 48f));
		drawList.AddText(layout, new UiPoint(4f, 4f), UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		int lit = 0;

		for (int y = 0; y < image.Height; y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				if (image.GetPixel(x, y) != UiColor.Black)
				{
					lit++;
				}
			}
		}

		Assert.True(
			lit > 40,
			$"The text must cover pixels, but only {lit} were drawn.");
	}

	[Fact]
	public void Scale_RendersAtTheRequestedPixelDensity()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(32f, 32f));
		drawList.AddRect(new UiRect(4f, 4f, 8f, 8f), UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			2f,
			UiColor.Black);

		Assert.Equal(64, image.Width);
		Assert.Equal(64, image.Height);
		Assert.Equal(UiColor.White, image.GetPixel(12, 12));
		Assert.Equal(UiColor.Black, image.GetPixel(40, 40));
	}

	[Fact]
	public void ImageCommand_SamplesTheSuppliedTexture()
	{
		UiTextureId textureId = UiTextureId.FromIndex(1);

		UiRasterImage texture = UiRasterImage.Create(4, 4);
		texture.Fill(UiColor.FromRgb(0x3366CC));

		UiDrawList drawList = new();
		drawList.Begin(new UiSize(32f, 32f));

		drawList.AddImage(
			new UiRect(8f, 8f, 16f, 16f),
			textureId,
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black,
			new Dictionary<UiTextureId, UiRasterImage>
			{
				[textureId] = texture
			});

		Assert.Equal(UiColor.FromRgb(0x3366CC), image.GetPixel(16, 16));
		Assert.Equal(UiColor.Black, image.GetPixel(2, 2));
	}

	[Fact]
	public void ImageCommand_MultipliesItsTint()
	{
		UiTextureId textureId = UiTextureId.FromIndex(2);

		UiRasterImage texture = UiRasterImage.Create(2, 2);
		texture.Fill(UiColor.White);

		UiDrawList drawList = new();
		drawList.Begin(new UiSize(16f, 16f));

		drawList.AddImage(
			new UiRect(0f, 0f, 16f, 16f),
			textureId,
			new UiRect(0f, 0f, 1f, 1f),
			new UiColor(128, 64, 255));

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black,
			new Dictionary<UiTextureId, UiRasterImage>
			{
				[textureId] = texture
			});

		UiColor pixel = image.GetPixel(8, 8);

		Assert.Equal(128, pixel.R);
		Assert.Equal(64, pixel.G);
		Assert.Equal(255, pixel.B);
		Assert.Equal(255, pixel.A);
	}

	[Fact]
	public void Png_EncodesTheImageAndItsDimensions()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(40f, 24f));
		drawList.AddRect(new UiRect(0f, 0f, 40f, 24f), UiColor.White);

		UiRasterImage image = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		byte[] png = image.EncodePng();

		Assert.True(png.Length > 100);
		Assert.Equal(0x89, png[0]);
		Assert.Equal((byte)'P', png[1]);
		Assert.Equal((byte)'N', png[2]);
		Assert.Equal((byte)'G', png[3]);

		Assert.Equal(
			40,
			BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));

		Assert.Equal(
			24,
			BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
	}

	[Fact]
	public void Png_RoundTripsThroughTheDecoder()
	{
		UiDrawList drawList = new();
		drawList.Begin(new UiSize(24f, 18f));
		drawList.AddRect(new UiRect(2f, 3f, 10f, 7f), UiColor.FromRgb(0x33CC88));
		drawList.AddRoundedRect(new UiRect(12f, 8f, 9f, 9f), 3f, UiColor.FromRgb(0xFF8800));

		UiRasterImage rendered = UiRasterizer.Render(
			drawList,
			1f,
			UiColor.Black);

		UiRasterImage decoded = UiRasterImage.DecodePng(rendered.EncodePng());

		Assert.Equal(rendered.Width, decoded.Width);
		Assert.Equal(rendered.Height, decoded.Height);

		for (int y = 0; y < rendered.Height; y++)
		{
			for (int x = 0; x < rendered.Width; x++)
			{
				Assert.Equal(rendered.GetPixel(x, y), decoded.GetPixel(x, y));
			}
		}
	}

	[Fact]
	public void SavePng_WritesTheEncodedFile()
	{
		string path = Path.Combine(
			Path.GetTempPath(),
			$"volt-raster-{Guid.NewGuid():N}.png");

		try
		{
			UiDrawList drawList = new();
			drawList.Begin(new UiSize(16f, 16f));
			drawList.AddRect(new UiRect(0f, 0f, 16f, 16f), UiColor.White);

			UiRasterizer
				.Render(drawList, 1f, UiColor.Black)
				.SavePng(path);

			Assert.True(File.Exists(path));

			byte[] saved = File.ReadAllBytes(path);

			Assert.Equal(0x89, saved[0]);
			Assert.Equal((byte)'P', saved[1]);
			Assert.Equal((byte)'N', saved[2]);
			Assert.Equal((byte)'G', saved[3]);
		}

		finally
		{
			File.Delete(path);
		}
	}
}
