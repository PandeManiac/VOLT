using Volt.Ui;

namespace Volt.Ui.Tests.Textures;

/// <summary>
/// Covers image registration, identifiers, and pixel storage without a
/// graphics device.
/// </summary>
public sealed class UiTextureRegistryTests
{
	[Fact]
	public void Register_AssignsIdentifiersFromOne()
	{
		UiTextureRegistry registry = new();

		UiImageTexture first = new(4, 4, "first");
		UiImageTexture second = new(4, 4, "second");

		Assert.Equal(1, registry.Register(first).Value);
		Assert.Equal(2, registry.Register(second).Value);
		Assert.Equal(2, registry.Count);
		Assert.True(first.IsRegistered);
		Assert.Same(first, registry.Resolve(UiTextureId.FromIndex(1)));
		Assert.Same(second, registry.Resolve(UiTextureId.FromIndex(2)));
	}

	[Fact]
	public void Register_RejectsAnImageThatIsAlreadyRegistered()
	{
		UiTextureRegistry registry = new();
		UiImageTexture texture = new(8, 8);

		registry.Register(texture);

		Assert.Throws<InvalidOperationException>(() =>
			registry.Register(texture));
	}

	[Fact]
	public void Register_ReleasesTheSlotWhenTheCapIsReached()
	{
		UiTextureRegistry registry = new();

		for (int index = 0; index < UiTextureRegistry.MaximumTextures; index++)
		{
			registry.Register(new UiImageTexture(2, 2));
		}

		InvalidOperationException error =
			Assert.Throws<InvalidOperationException>(() =>
				registry.Register(new UiImageTexture(2, 2)));

		Assert.Contains(
			UiTextureRegistry.MaximumTextures.ToString(),
			error.Message);
	}

	[Fact]
	public void Release_FreesTheSlotWithoutRenumberingOtherImages()
	{
		UiTextureRegistry registry = new();

		UiImageTexture first = registry.Create(4, 4);
		UiImageTexture second = registry.Create(4, 4);
		UiImageTexture third = registry.Create(4, 4);

		Assert.True(registry.Release(second));

		Assert.Equal(UiTextureId.White, second.Id);
		Assert.False(second.IsRegistered);
		Assert.Equal(1, first.Id.Value);
		Assert.Equal(3, third.Id.Value);
		Assert.Null(registry.Resolve(UiTextureId.FromIndex(2)));

		// The freed slot is reused, and the images that stayed keep the
		// identifiers a renderer already uploaded them under.
		UiImageTexture replacement = registry.Create(4, 4);

		Assert.Equal(2, replacement.Id.Value);
		Assert.Equal(3, third.Id.Value);
		Assert.Equal(3, registry.Count);
	}

	[Fact]
	public void Release_IgnoresAnImageThatMovedToAnotherRegistry()
	{
		UiTextureRegistry first = new();
		UiTextureRegistry second = new();

		UiImageTexture texture = first.Create(4, 4);

		Assert.False(second.Release(texture));
		Assert.True(texture.IsRegistered);
	}

	[Fact]
	public void Clear_ReleasesEveryImage()
	{
		UiContext context = new();

		UiImageTexture texture = context.Textures.Create(4, 4);

		context.Dispose();

		Assert.False(texture.IsRegistered);
		Assert.Equal(0, context.Textures.Count);
	}

	[Fact]
	public void Resolve_IgnoresIdentifiersOutsideTheSlotRange()
	{
		UiTextureRegistry registry = new();

		registry.Create(4, 4);

		Assert.Null(registry.Resolve(UiTextureId.White));
		Assert.Null(registry.Resolve(
			UiTextureId.FromIndex(UiTextureRegistry.MaximumTextures + 1)));
	}

	[Fact]
	public void Texture_StartsFullyTransparent()
	{
		UiImageTexture texture = new(2, 3);

		Assert.Equal(2, texture.Width);
		Assert.Equal(3, texture.Height);
		Assert.Equal(8, texture.Stride);
		Assert.Equal(24, texture.Pixels.Length);
		Assert.Equal(UiColor.Transparent, texture.GetPixel(1, 1));
	}

	[Fact]
	public void SetPixel_RoundTripsPixelsTopDown()
	{
		UiImageTexture texture = new(3, 2);

		texture.SetPixel(0, 0, UiColor.FromRgb(0x102030));
		texture.SetPixel(2, 1, UiColor.FromRgb(0xAABBCC));

		// The first pixel of the buffer is the one drawn at the
		// top-left corner.
		Assert.Equal(
			new byte[] { 0x10, 0x20, 0x30, 255 },
			texture.Pixels[..4].ToArray());

		int last = texture.Pixels.Length - 4;

		Assert.Equal(
			new byte[] { 0xAA, 0xBB, 0xCC, 255 },
			texture.Pixels[last..].ToArray());

		Assert.Equal(
			UiColor.FromRgb(0xAABBCC),
			texture.GetPixel(2, 1));
	}

	[Fact]
	public void PixelAccess_OutsideTheImageIsIgnored()
	{
		UiImageTexture texture = new(2, 2);

		texture.SetPixel(-1, 0, UiColor.White);
		texture.SetPixel(2, 0, UiColor.White);
		texture.SetPixel(0, 5, UiColor.White);

		Assert.Equal(0, texture.Revision);
		Assert.Equal(UiColor.Transparent, texture.GetPixel(-1, 0));
	}

	[Fact]
	public void Revision_AdvancesOnlyWhenPixelsChange()
	{
		UiImageTexture texture = new(2, 2);

		Assert.Equal(0, texture.Revision);

		texture.SetPixel(0, 0, UiColor.White);

		Assert.Equal(1, texture.Revision);

		texture.SetPixel(0, 0, UiColor.White);

		Assert.Equal(2, texture.Revision);

		// A fill entirely outside the image changes nothing, so the
		// renderer must not re-upload it.
		texture.Fill(new UiPixelRect(10, 10, 2, 2), UiColor.White);

		Assert.Equal(2, texture.Revision);
	}

	[Fact]
	public void Fill_ClampsToTheImage()
	{
		UiImageTexture texture = new(4, 4);

		texture.Fill(new UiPixelRect(-2, -2, 4, 4), UiColor.FromRgb(0x00FF00));

		Assert.Equal(UiColor.FromRgb(0x00FF00), texture.GetPixel(0, 0));
		Assert.Equal(UiColor.FromRgb(0x00FF00), texture.GetPixel(1, 1));
		Assert.Equal(UiColor.Transparent, texture.GetPixel(2, 2));
	}

	[Fact]
	public void SetPixels_RequiresTheWholeImage()
	{
		UiImageTexture texture = new(2, 2);

		Assert.Throws<ArgumentException>(() =>
			texture.SetPixels(new byte[3]));

		byte[] pixels = new byte[16];

		pixels[0] = 9;

		texture.SetPixels(pixels);

		Assert.Equal(9, texture.Pixels[0]);
		Assert.Equal(1, texture.Revision);
	}
}
