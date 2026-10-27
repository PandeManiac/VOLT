using System.Numerics;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Vulkan;

namespace Volt.Ui.Vulkan.Tests;

/// <summary>
/// Covers the single pixel-space to clip-space conversion used by the
/// Vulkan UI renderer. Every assertion here runs without a device.
/// </summary>
public sealed class UiClipSpaceTests
{
	private const float Width = 1280f;
	private const float Height = 800f;

	[Fact]
	public void TopLeftPixel_MapsToTheTopLeftCornerOfClipSpace()
	{
		UiVertex converted = ConvertPixel(new Vector2(0f, 0f));

		Assert.Equal(-1f, converted.Position.X, 5);
		Assert.Equal(-1f, converted.Position.Y, 5);
	}

	[Fact]
	public void BottomRightPixel_MapsToTheBottomRightCornerOfClipSpace()
	{
		UiVertex converted = ConvertPixel(new Vector2(Width, Height));

		Assert.Equal(1f, converted.Position.X, 5);
		Assert.Equal(1f, converted.Position.Y, 5);
	}

	[Fact]
	public void CenterPixel_MapsToTheCenterOfClipSpace()
	{
		UiVertex converted = ConvertPixel(
			new Vector2(Width / 2f, Height / 2f));

		Assert.Equal(0f, converted.Position.X, 5);
		Assert.Equal(0f, converted.Position.Y, 5);
	}

	[Fact]
	public void IncreasingPixelY_MovesDownTheViewport()
	{
		// Vulkan clip space is Y-down from a top-left origin, exactly
		// like the UI's pixel space, so the mapping is monotonic and
		// needs no axis flip. A flip here mirrors the whole frame
		// while scissor rectangles stay in framebuffer coordinates.
		float top = ConvertPixel(new Vector2(0f, 0f)).Position.Y;
		float middle = ConvertPixel(new Vector2(0f, Height / 2f)).Position.Y;
		float bottom = ConvertPixel(new Vector2(0f, Height)).Position.Y;

		Assert.True(top < middle);
		Assert.True(middle < bottom);
	}

	[Fact]
	public void ExtentAxes_AreIndependent()
	{
		UiVertex converted = ConvertPixel(new Vector2(Width / 2f, Height / 4f));

		Assert.Equal(0f, converted.Position.X, 5);
		Assert.Equal(-0.5f, converted.Position.Y, 5);
	}

	[Fact]
	public void TextureCoordinatesAndColors_AreUnchanged()
	{
		UiVertex source = new(
			new Vector2(10f, 20f),
			new Vector2(0.25f, 0.75f),
			0xFF102030u);

		UiVertex[] destination = new UiVertex[1];

		UiClipSpace.Convert([source], destination, Width, Height);

		Assert.Equal(source.Uv, destination[0].Uv);
		Assert.Equal(source.Color, destination[0].Color);
	}

	[Fact]
	public void DegenerateExtent_StaysFinite()
	{
		UiVertex[] destination = new UiVertex[1];

		UiClipSpace.Convert(
			[new UiVertex(Vector2.Zero, Vector2.Zero, 0xFFFFFFFFu)],
			destination,
			0f,
			0f);

		Assert.True(float.IsFinite(destination[0].Position.X));
		Assert.True(float.IsFinite(destination[0].Position.Y));
		Assert.Equal(-1f, destination[0].Position.X, 5);
		Assert.Equal(-1f, destination[0].Position.Y, 5);
	}

	[Fact]
	public void ShortDestination_IsRejected()
	{
		Assert.Throws<ArgumentException>(() =>
			UiClipSpace.Convert(
				[new UiVertex(Vector2.Zero, Vector2.Zero, 0xFFFFFFFFu)],
				[],
				Width,
				Height));
	}

	[Fact]
	public void GeometryAndScissor_UseOneCoordinateSystem()
	{
		// A clipped rectangle is scissored to exactly the pixels its
		// geometry is converted from, so both must describe the same
		// top-left region. A Y flip in the conversion would put the
		// geometry in the bottom half while the scissor stayed put.
		UiRect rectangle = new(0f, 0f, 100f, 50f);

		UiDrawList list = new();
		list.Begin(new UiSize(Width, Height));
		list.PushClip(rectangle);
		list.AddRect(rectangle, UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		Assert.Equal(1, geometry.BatchCount);

		UiBatch batch = geometry.Batches[0];

		Assert.Equal(0, batch.Scissor.X);
		Assert.Equal(0, batch.Scissor.Y);
		Assert.Equal(100, batch.Scissor.Width);
		Assert.Equal(50, batch.Scissor.Height);

		UiVertex[] converted = new UiVertex[geometry.Vertices.Length];

		UiClipSpace.Convert(
			geometry.Vertices,
			converted,
			Width,
			Height);

		float left = float.MaxValue;
		float top = float.MaxValue;
		float bottom = float.MinValue;

		foreach (UiVertex vertex in converted)
		{
			left = MathF.Min(left, vertex.Position.X);
			top = MathF.Min(top, vertex.Position.Y);
			bottom = MathF.Max(bottom, vertex.Position.Y);
		}

		// The scissor's top-left pixel is the viewport's top-left
		// corner, and the scissor's height is a quarter of the
		// half-height clip space uses.
		Assert.Equal(-1f, left, 5);
		Assert.Equal(-1f, top, 5);
		Assert.Equal(-0.875f, bottom, 5);
	}

	[Fact]
	public void TextQuad_KeepsTheAtlasOrientation()
	{
		// Atlas rows are stored and uploaded top-down and glyph UVs
		// measure from the top-left, so a glyph's top edge must keep
		// the smaller V and convert to the smaller clip-space Y.
		// Flipping only one of the two is what rendered text upside
		// down while the rest of the layout looked plausible.
		UiVertex top = new(
			new Vector2(200f, 100f),
			new Vector2(0.1f, 0.2f),
			0xFFFFFFFFu);

		UiVertex bottom = new(
			new Vector2(200f, 120f),
			new Vector2(0.1f, 0.4f),
			0xFFFFFFFFu);

		UiVertex[] converted = new UiVertex[2];

		UiClipSpace.Convert([top, bottom], converted, Width, Height);

		Assert.True(converted[0].Position.Y < converted[1].Position.Y);
		Assert.True(converted[0].Uv.Y < converted[1].Uv.Y);
	}

	private static UiVertex ConvertPixel(Vector2 position)
	{
		UiVertex[] destination = new UiVertex[1];

		UiClipSpace.Convert(
			[new UiVertex(position, Vector2.Zero, 0xFFFFFFFFu)],
			destination,
			Width,
			Height);

		return destination[0];
	}
}
