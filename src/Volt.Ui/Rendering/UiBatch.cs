namespace Volt.Ui.Rendering;

/// <summary>
/// A contiguous range of indices that share a texture and scissor.
/// </summary>
public readonly record struct UiBatch(
	int FirstIndex,
	int IndexCount,
	UiPixelRect Scissor,
	UiTextureKey Texture);
