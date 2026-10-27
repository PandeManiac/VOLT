using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Text;
using Volt.Ui.Vulkan;

namespace Volt.Ui.Vulkan.Tests;

/// <summary>
/// Covers how a UI batch selects its atlas layer, sampling path, and
/// descriptor set. Every assertion here runs without a device.
/// </summary>
public sealed class UiTextureShaderInputTests
{
	private static readonly UiFont Body = new(UiFontKind.Sans, 13f, 1f);

	[Fact]
	public void FontBatch_SamplesItsAtlasLayerInCoverageMode()
	{
		Dictionary<UiFont, int> layers = new()
		{
			[Body] = 3
		};

		UiTextureShaderInput input = UiTextureShaderInput.Resolve(
			UiTextureKey.FromFont(Body),
			layers);

		Assert.Equal(3f, input.Layer);
		Assert.Equal(UiTextureShaderInput.CoverageMode, input.Mode);
		Assert.Equal(UiTextureShaderInput.CoverageSetSlot, input.SetSlot);
	}

	[Fact]
	public void SolidBatch_SamplesTheWhiteTexel()
	{
		UiTextureShaderInput input = UiTextureShaderInput.Resolve(
			UiTextureKey.White,
			new Dictionary<UiFont, int>());

		Assert.Equal(0f, input.Layer);
		Assert.Equal(UiTextureShaderInput.CoverageMode, input.Mode);
		Assert.Equal(UiTextureShaderInput.CoverageSetSlot, input.SetSlot);
	}

	[Fact]
	public void ImageBatch_SamplesItsOwnDescriptorSet()
	{
		UiTextureShaderInput input = UiTextureShaderInput.Resolve(
			UiTextureKey.Image(UiTextureId.FromIndex(4)),
			new Dictionary<UiFont, int>());

		Assert.Equal(0f, input.Layer);
		Assert.Equal(UiTextureShaderInput.ImageMode, input.Mode);

		// Slot zero holds the coverage textures, so the set that
		// carries image n is slot n.
		Assert.Equal(4, input.SetSlot);
	}

	[Fact]
	public void UnknownFont_FallsBackToTheWhiteTexel()
	{
		Dictionary<UiFont, int> layers = new()
		{
			[Body] = 1
		};

		UiFont unregistered = new(UiFontKind.Monospace, 13f, 1f);

		UiTextureShaderInput input = UiTextureShaderInput.Resolve(
			UiTextureKey.FromFont(unregistered),
			layers);

		Assert.Equal(UiTextureShaderInput.CoverageMode, input.Mode);
		Assert.Equal(UiTextureShaderInput.CoverageSetSlot, input.SetSlot);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(UiTextureRegistry.MaximumTextures + 1)]
	public void ImageIdentifierOutsideTheSlots_FallsBackToTheWhiteTexel(
		int imageId)
	{
		Assert.False(UiTextureShaderInput.IsImageSlot(imageId));

		// An identifier outside the range must not address a set that
		// holds an unrelated image.
		UiTextureKey key = imageId == 0
			? UiTextureKey.White
			: new UiTextureKey(UiTextureKind.Image, imageId, null);

		UiTextureShaderInput input = UiTextureShaderInput.Resolve(
			key,
			new Dictionary<UiFont, int>());

		Assert.Equal(UiTextureShaderInput.CoverageMode, input.Mode);
		Assert.Equal(UiTextureShaderInput.CoverageSetSlot, input.SetSlot);
	}

	[Fact]
	public void EveryRegisteredImage_HasItsOwnDescriptorSet()
	{
		Assert.Equal(
			UiTextureRegistry.MaximumTextures + 1,
			UiTextureShaderInput.SetSlotCount);

		for (int imageId = 1;
			imageId <= UiTextureRegistry.MaximumTextures;
			imageId++)
		{
			UiTextureShaderInput input = UiTextureShaderInput.Resolve(
				UiTextureKey.Image(UiTextureId.FromIndex(imageId)),
				new Dictionary<UiFont, int>());

			Assert.Equal(imageId, input.SetSlot);
		}
	}

	[Fact]
	public void Resolve_RejectsAMissingLayerMap()
	{
		Assert.Throws<ArgumentNullException>(() =>
			UiTextureShaderInput.Resolve(
				UiTextureKey.White,
				null!));
	}
}
