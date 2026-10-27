using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

/// <summary>
/// Covers how an image lands in a widget's content area and what it
/// paints, which is what a viewport panel depends on.
/// </summary>
public sealed class ImageViewTests
{
	[Fact]
	public void Measure_ReportsTheNaturalTextureSize()
	{
		using UiTestHost host = new(320f, 200f);

		UiImageTexture texture = host.Context.Textures.Create(64, 32);

		ImageView view = host.Add(new ImageView(texture));

		host.Layout();

		Assert.Equal(64f, view.DesiredSize.Width);
		Assert.Equal(32f, view.DesiredSize.Height);
	}

	[Fact]
	public void Fit_KeepsTheWholeImageInsideAndCentersIt()
	{
		using UiTestHost host = new(640f, 480f);

		UiImageTexture texture = host.Context.Textures.Create(64, 32);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Fit
		});

		host.Layout();

		UiRect content = view.GlobalContentBounds;
		UiRect image = view.ImageBounds;

		// A 2:1 texture fills the width when the area is taller than
		// 2:1, and the leftover height is split evenly.
		Assert.Equal(content.Width, image.Width, 3);
		Assert.Equal(content.Width / 2f, image.Height, 3);
		Assert.Equal(content.X, image.X, 3);

		Assert.Equal(
			content.Y + ((content.Height - image.Height) * 0.5f),
			image.Y,
			3);
	}

	[Fact]
	public void Fill_CoversTheAreaAndOverflowsCentered()
	{
		using UiTestHost host = new(640f, 480f);

		UiImageTexture texture = host.Context.Textures.Create(64, 32);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Fill
		});

		host.Layout();

		UiRect content = view.GlobalContentBounds;
		UiRect image = view.ImageBounds;

		Assert.Equal(content.Height, image.Height, 3);
		Assert.Equal(content.Height * 2f, image.Width, 3);

		Assert.Equal(
			content.X - ((image.Width - content.Width) * 0.5f),
			image.X,
			3);

		Assert.Equal(content.Y, image.Y, 3);
	}

	[Fact]
	public void Stretch_IgnoresTheTextureAspectRatio()
	{
		using UiTestHost host = new(300f, 200f);

		UiImageTexture texture = host.Context.Textures.Create(64, 32);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch,
			Padding = new UiThickness(10f)
		});

		host.Layout();

		Assert.Equal(view.GlobalContentBounds, view.ImageBounds);
		Assert.Equal(view.VisualBounds.Width - 20f, view.ImageBounds.Width, 3);
	}

	[Fact]
	public void ImageWithoutPadding_IsInsetByPaddingOnly()
	{
		using UiTestHost host = new(400f, 300f);

		UiImageTexture texture = host.Context.Textures.Create(32, 32);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch,
			Padding = new UiThickness(4f, 8f)
		});

		host.Layout();

		// The content box is relative to the widget, so padding moves
		// it and the position inside the parent must not leak into it.
		Assert.Equal(4f, view.ContentBounds.X);
		Assert.Equal(8f, view.ContentBounds.Y);
		Assert.Equal(4f, view.ImageBounds.X - view.GlobalBounds.X, 3);
		Assert.Equal(8f, view.ImageBounds.Y - view.GlobalBounds.Y, 3);
	}

	[Fact]
	public void MapToTexture_MatchesTheDrawnRectangle()
	{
		using UiTestHost host = new(640f, 480f);

		UiImageTexture texture = host.Context.Textures.Create(200, 100);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Fit
		});

		host.Layout();

		UiRect image = view.ImageBounds;

		Assert.True(view.TryMapToTexture(image.Center, out var center));

		Assert.Equal(100f, center.X, 3);
		Assert.Equal(50f, center.Y, 3);

		Assert.True(view.TryMapToTexture(
			new UiPoint(image.X, image.Y),
			out var topLeft));

		Assert.Equal(0f, topLeft.X, 3);
		Assert.Equal(0f, topLeft.Y, 3);

		// The mapping is not clamped: a point outside the image yields
		// a coordinate outside it, which is what a drag gesture needs.
		Assert.True(view.TryMapToTexture(
			new UiPoint(image.X - 10f, image.Y),
			out var outside));

		Assert.True(outside.X < 0f);
	}

	[Fact]
	public void Uv_SelectsASubRegion()
	{
		using UiTestHost host = new(200f, 200f);

		UiImageTexture texture = host.Context.Textures.Create(100, 100);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch,
			Uv = new UiRect(0.5f, 0.5f, 0.5f, 0.5f)
		});

		host.Layout();

		// The sub-region's top-left is the image's middle texel.
		Assert.True(view.TryMapToTexture(
			new UiPoint(view.ImageBounds.X, view.ImageBounds.Y),
			out var topLeft));

		Assert.Equal(50f, topLeft.X, 3);
		Assert.Equal(50f, topLeft.Y, 3);
	}

	[Fact]
	public void Paint_DrawsTheRegisteredImageInItsRectangle()
	{
		using UiTestHost host = new(300f, 200f);

		UiImageTexture texture = host.Context.Textures.Create(30, 20);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch
		});

		host.Layout();

		UiDrawList list = host.Context.Draw();

		Assert.Equal(2, list.Count);

		UiDrawCommand image = list.GetCommand(1);

		Assert.Equal(UiDrawCommandKind.Image, image.Kind);
		Assert.Equal(texture.Id, image.Texture);
		Assert.Equal(view.ImageBounds, image.Rect);
		Assert.Equal(new UiRect(0f, 0f, 1f, 1f), image.Uv);
		Assert.Equal(UiColor.White, image.Color);

		// The paint rectangle and the reported rectangle are the same
		// thing, so hit testing and drawing cannot drift apart.
		Assert.True(image.Rect.Contains(view.ImageBounds.Center));
	}

	[Fact]
	public void Paint_ClipsTheImageToTheWidget()
	{
		using UiTestHost host = new(100f, 100f);

		UiImageTexture texture = host.Context.Textures.Create(400, 400);

		ImageView view = host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Fill
		});

		host.Layout();

		UiDrawList list = host.Context.Draw();
		UiDrawCommand image = list.GetCommand(1);

		Assert.Equal(
			view.GlobalVisualBounds,
			image.Clip);

		Assert.True(image.Clip.Width <= 100f);
		Assert.True(image.Clip.Height <= 100f);
	}

	[Fact]
	public void Paint_SkipsAnImageThatWasNeverRegistered()
	{
		using UiTestHost host = new(200f, 200f);

		UiImageTexture texture = new(20, 20);

		host.Add(new ImageView(texture));

		host.Layout();

		UiDrawList list = host.Context.Draw();

		Assert.Equal(1, list.Count);
		Assert.Equal(UiDrawCommandKind.Rectangle, list.GetCommand(0).Kind);
	}

	[Fact]
	public void Paint_StopsDrawingAnImageThatWasReleased()
	{
		using UiTestHost host = new(200f, 200f);

		UiImageTexture texture = host.Context.Textures.Create(20, 20);

		host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch
		});

		host.Layout();

		Assert.Equal(2, host.Context.Draw().Count);

		host.Context.Textures.Release(texture);

		host.Layout();

		UiDrawList list = host.Context.Draw();

		Assert.Equal(1, list.Count);
		Assert.False(texture.IsRegistered);
	}

	[Fact]
	public void ImageThatChangesEveryFrame_PaintsTheLatestRevision()
	{
		using UiTestHost host = new(64f, 64f);

		UiImageTexture texture = host.Context.Textures.Create(4, 4);

		host.Add(new ImageView(texture)
		{
			Scale = UiImageScale.Stretch
		});

		host.Layout();

		UiDrawList first = host.Context.Draw();

		texture.Fill(new UiPixelRect(0, 0, 4, 4), UiColor.White);

		host.Layout();

		UiDrawList second = host.Context.Draw();

		Assert.Equal(first.Count, second.Count);
		Assert.Equal(texture.Id, second.GetCommand(1).Texture);
		Assert.Equal(1, texture.Revision);
	}
}
