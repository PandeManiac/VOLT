using Volt.Ui.Text;
using Volt.Ui.Vulkan;

namespace Volt.Ui.Vulkan.Tests;

/// <summary>
/// Verifies how fonts are assigned to the R8 texture-array layers the UI
/// shader samples.
/// </summary>
/// <remarks>
/// Layer zero holds the opaque white texel that solid geometry samples.
/// A font that is missing from this mapping would therefore paint solid
/// blocks instead of letters, which is why every distinct font must own
/// its own layer above zero.
/// </remarks>
public sealed class FontAtlasLayerTests
{
	[Fact]
	public void EveryRole_GetsADistinctLayerAboveTheWhiteTexel()
	{
		UiFont[] fonts = [.. new UiFontSet(1f).Enumerate()];

		Dictionary<UiFont, int> layers = VulkanUiRenderer.BuildFontLayers(fonts);

		Assert.Equal(8, fonts.Length);
		Assert.Equal(fonts.Length, layers.Count);

		foreach (UiFont font in fonts)
		{
			Assert.True(layers.ContainsKey(font));
		}

		int[] assigned = [.. layers.Values.Order()];

		Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], assigned);
		Assert.DoesNotContain(0, layers.Values);
	}

	[Fact]
	public void RepeatedFont_KeepsOneCompactLayer()
	{
		UiFontSet set = new(1f);

		Dictionary<UiFont, int> layers = VulkanUiRenderer.BuildFontLayers(
			[set.Small, set.Small, set.Body]);

		Assert.Equal(2, layers.Count);
		Assert.Equal(1, layers[set.Small]);
		Assert.Equal(2, layers[set.Body]);
	}

	[Fact]
	public void SeparateFontSets_NeverShareALayer()
	{
		UiFontSet first = new(1f);
		UiFontSet second = new(1f);

		Dictionary<UiFont, int> layers = VulkanUiRenderer.BuildFontLayers(
			[first.Small, second.Small]);

		Assert.Equal(2, layers.Count);
		Assert.NotEqual(layers[first.Small], layers[second.Small]);
	}

	[Fact]
	public void NoFonts_LeavesOnlyTheWhiteLayer()
	{
		Dictionary<UiFont, int> layers =
			VulkanUiRenderer.BuildFontLayers([]);

		Assert.Empty(layers);
	}
}
