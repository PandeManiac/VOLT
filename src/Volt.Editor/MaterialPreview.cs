using System.Numerics;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;

namespace Volt.Editor;

/// <summary>
/// A material's authored values, ready to show.
/// </summary>
/// <param name="Name">The material name without its extension.</param>
/// <param name="Albedo">The flat surface colour in linear components.</param>
/// <param name="AlbedoHex">
/// The same colour as the hex spelling a developer would edit, which is
/// what makes a mismatch between the file and the preview obvious.
/// </param>
/// <param name="TextureLine">
/// One line describing the image: its size when it decodes, or why it
/// cannot be shown.
/// </param>
public sealed record MaterialPreview(
	string Name,
	Vector3 Albedo,
	string AlbedoHex,
	string TextureLine)
{
	/// <summary>
	/// Describes the material an asset identifier names.
	/// </summary>
	/// <param name="project">The open project, or null.</param>
	/// <param name="assetId">The selected asset, or null.</param>
	/// <returns>
	/// The description, or null when the selection is not a material.
	/// </returns>
	/// <remarks>
	/// A material that cannot be read still produces a description: the
	/// preview is where a broken file should say so, not disappear.
	/// </remarks>
	public static MaterialPreview? Describe(
		EditorProject? project,
		string? assetId)
	{
		if (project is null || string.IsNullOrWhiteSpace(assetId))
		{
			return null;
		}

		ProjectAsset? asset = project.AssetCatalog.Find(assetId);

		if (asset is null || asset.Kind != ProjectAssetKind.Material)
		{
			return null;
		}

		string name = Path.GetFileNameWithoutExtension(asset.FileName);

		if (!project.TryLoadMaterial(
			assetId,
			out MaterialAsset material,
			out string? diagnostic))
		{
			return new MaterialPreview(
				name,
				MaterialLoader.DefaultAlbedo,
				Hex(MaterialLoader.DefaultAlbedo),
				$"Could not be read: {diagnostic}");
		}

		return new MaterialPreview(
			name,
			material.Albedo,
			Hex(material.Albedo),
			DescribeTexture(project, material.Texture));
	}

	private static string DescribeTexture(
		EditorProject project,
		string? texture)
	{
		if (string.IsNullOrWhiteSpace(texture))
		{
			return "No texture; the albedo is a flat colour.";
		}

		if (!project.TryLoadTexture(
			texture,
			out PngImage image,
			out string? diagnostic))
		{
			return $"Missing: {diagnostic}";
		}

		return $"{Path.GetFileName(texture)} · {image.Width} × {image.Height}";
	}

	private static string Hex(Vector3 colour)
	{
		static int Channel(float value)
		{
			return Math.Clamp(
				(int)MathF.Round(value * 255f),
				0,
				255);
		}

		return $"#{Channel(colour.X):X2}{Channel(colour.Y):X2}" +
			$"{Channel(colour.Z):X2}";
	}
}
