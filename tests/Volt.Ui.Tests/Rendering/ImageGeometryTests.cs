using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Ui.Tests.Rendering;

/// <summary>
/// Covers how image commands become batches, because a batch is the
/// unit that binds one texture.
/// </summary>
public sealed class ImageGeometryTests
{
	[Fact]
	public void DifferentImages_BreakTheBatch()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(200f, 200f));

		list.AddImage(
			new UiRect(0f, 0f, 10f, 10f),
			UiTextureId.FromIndex(1),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		list.AddImage(
			new UiRect(20f, 0f, 10f, 10f),
			UiTextureId.FromIndex(2),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		Assert.Equal(2, geometry.BatchCount);
		Assert.Equal(
			UiTextureKey.Image(UiTextureId.FromIndex(1)),
			geometry.Batches[0].Texture);

		Assert.Equal(
			UiTextureKey.Image(UiTextureId.FromIndex(2)),
			geometry.Batches[1].Texture);
	}

	[Fact]
	public void OneImage_DrawsInOneBatch()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(200f, 200f));

		for (int index = 0; index < 4; index++)
		{
			list.AddImage(
				new UiRect(index * 20f, 0f, 10f, 10f),
				UiTextureId.FromIndex(1),
				new UiRect(0f, 0f, 1f, 1f),
				UiColor.White);
		}

		UiGeometry geometry = new();
		geometry.Build(list);

		Assert.Equal(1, geometry.BatchCount);
		Assert.Equal(24, geometry.Batches[0].IndexCount);
	}

	[Fact]
	public void ImageAndSolidBatch_UseDifferentTextures()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(200f, 200f));

		list.AddRect(new UiRect(0f, 0f, 10f, 10f), UiColor.White);

		list.AddImage(
			new UiRect(20f, 0f, 10f, 10f),
			UiTextureId.FromIndex(3),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		Assert.Equal(2, geometry.BatchCount);
		Assert.Equal(UiTextureKind.White, geometry.Batches[0].Texture.Kind);
		Assert.Equal(UiTextureKind.Image, geometry.Batches[1].Texture.Kind);
		Assert.Equal(3, geometry.Batches[1].Texture.ImageId);
	}

	[Fact]
	public void ImageVertices_KeepTheTopLeftUvOrientation()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));

		list.AddImage(
			new UiRect(10f, 20f, 40f, 30f),
			UiTextureId.FromIndex(1),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		ReadOnlySpan<UiVertex> vertices = geometry.Vertices;

		Assert.Equal(4, vertices.Length);

		// The image's own top-left corner must sample the UV origin,
		// exactly as a downsampled atlas row does, so an uploaded image
		// is not mirrored vertically.
		UiVertex topLeft = vertices[0];
		UiVertex bottomLeft = vertices[3];

		Assert.True(topLeft.Position.Y < bottomLeft.Position.Y);
		Assert.True(topLeft.Uv.Y < bottomLeft.Uv.Y);
		Assert.Equal(0f, topLeft.Uv.Y, 3);
		Assert.Equal(1f, bottomLeft.Uv.Y, 3);
	}

	[Fact]
	public void ImageOutsideItsClip_IsScissoredAway()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));
		list.PushClip(new UiRect(0f, 0f, 10f, 10f));

		list.AddImage(
			new UiRect(80f, 80f, 20f, 20f),
			UiTextureId.FromIndex(1),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		// Clipping is a scissor, not geometry trimming, so the command
		// still produces a batch. Every pixel it could cover lies
		// outside the scissor, which is what makes it invisible.
		Assert.Equal(1, geometry.BatchCount);

		UiBatch batch = geometry.Batches[0];

		Assert.Equal(0, batch.Scissor.X);
		Assert.Equal(0, batch.Scissor.Y);
		Assert.Equal(10, batch.Scissor.Width);
		Assert.Equal(10, batch.Scissor.Height);

		foreach (UiVertex vertex in geometry.Vertices)
		{
			Assert.True(
				vertex.Position.X >= batch.Scissor.Right ||
				vertex.Position.Y >= batch.Scissor.Bottom);
		}
	}

	[Fact]
	public void ImageClippedToAPartialRegion_IsBoundedByTheClip()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));
		list.PushClip(new UiRect(0f, 0f, 10f, 10f));

		list.AddImage(
			new UiRect(5f, 5f, 20f, 20f),
			UiTextureId.FromIndex(1),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		UiBatch batch = geometry.Batches[0];

		// The scissor covers exactly the pixels the command may paint,
		// so the part of the image outside the clip is never drawn.
		Assert.Equal(10, batch.Scissor.Width);
		Assert.Equal(10, batch.Scissor.Height);

		Assert.True(geometry.Vertices[0].Position.X < batch.Scissor.Right);
		Assert.True(geometry.Vertices[0].Position.Y < batch.Scissor.Bottom);
	}

	[Fact]
	public void ImageScaledToPixels_ScalesItsRectangle()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));

		list.AddImage(
			new UiRect(10f, 20f, 40f, 30f),
			UiTextureId.FromIndex(1),
			new UiRect(0f, 0f, 1f, 1f),
			UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list, scale: 2f);

		ReadOnlySpan<UiVertex> vertices = geometry.Vertices;

		Assert.Equal(20f, vertices[0].Position.X, 3);
		Assert.Equal(40f, vertices[0].Position.Y, 3);
		Assert.Equal(100f, vertices[2].Position.X, 3);
		Assert.Equal(100f, vertices[2].Position.Y, 3);
	}
}
