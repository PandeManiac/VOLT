using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Volt.Editor.Core.Assets;

/// <summary>
/// A material authored as a project asset.
/// </summary>
/// <param name="Id">The project-relative asset identifier.</param>
/// <param name="Albedo">
/// The flat surface colour, in linear 0..1 components. The scene
/// pipeline shades by face, so a material tints that shading rather
/// than replacing it.
/// </param>
/// <param name="Texture">
/// The project-relative path of an image applied to the surface, or
/// <see langword="null"/> for a flat colour. The image is sampled by
/// the corners' texture coordinates and multiplied by the albedo, so a
/// material colour still tints a texture.
/// </param>
public sealed record MaterialAsset(
	string Id,
	Vector3 Albedo,
	string? Texture = null);

/// <summary>
/// Reads the project's material files.
/// </summary>
/// <remarks>
/// <para>
/// A material is a small JSON file rather than an opaque binary, so it
/// can be authored, reviewed, and diffed in the project like any other
/// asset. The only property that changes what is rendered today is
/// <c>albedo</c>; nothing else is read, so a file cannot promise an
/// effect this build does not implement.
/// </para>
/// <para>
/// <c>albedo</c> accepts <c>"#RRGGBB"</c> or three 0..1 numbers, and
/// <c>texture</c> optionally names a PNG in the project. The built-in
/// default is the colour every imported mesh already uses, so assigning
/// it changes nothing.
/// </para>
/// </remarks>
public static class MaterialLoader
{
	/// <summary>
	/// The identifier scenes use for the material they cannot name.
	/// </summary>
	public const string DefaultId = "builtin:default";

	/// <summary>Gets the default material colour.</summary>
	/// <remarks>
	/// The components are the exact 8-bit values of the colour a newly
	/// created material file names, so the constant and the file agree and
	/// a fresh material changes nothing.
	/// </remarks>
	public static Vector3 DefaultAlbedo { get; } =
		new(199f / 255f, 204f / 255f, 217f / 255f);

	/// <summary>
	/// Gets the text a newly created material file contains.
	/// </summary>
	public static string CreateDefaultJson()
	{
		return
			"""
			{
			  "albedo": "#C7CCD9"
			}

			""";
	}

	/// <summary>
	/// Loads one material file.
	/// </summary>
	/// <param name="id">The project-relative identifier to record.</param>
	/// <param name="fullPath">The file to read.</param>
	/// <returns>The loaded material.</returns>
	/// <exception cref="InvalidDataException">
	/// The file is not valid material JSON.
	/// </exception>
	public static MaterialAsset Load(string id, string fullPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

		using StreamReader reader = new(fullPath);

		return Parse(id, reader);
	}

	/// <summary>
	/// Parses material JSON.
	/// </summary>
	/// <param name="id">The project-relative identifier to record.</param>
	/// <param name="reader">The text to read.</param>
	/// <returns>The parsed material.</returns>
	/// <exception cref="InvalidDataException">
	/// The text is not valid material JSON.
	/// </exception>
	public static MaterialAsset Parse(string id, TextReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		JsonDocument document;

		try
		{
			document = JsonDocument.Parse(reader.ReadToEnd());
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				$"'{id}' is not valid material JSON: {exception.Message}",
				exception);
		}

		using (document)
		{
			if (!document.RootElement.TryGetProperty(
					"albedo",
					out JsonElement albedo))
			{
				throw new InvalidDataException(
					$"'{id}' does not set an albedo colour.");
			}

			Vector3 color = albedo.ValueKind switch
			{
				JsonValueKind.String => ParseHex(id, albedo.GetString()!),
				JsonValueKind.Array => ParseComponents(id, albedo),
				_ => throw new InvalidDataException(
					$"'{id}' must set albedo as \"#RRGGBB\" or three numbers.")
			};

			string? texture = null;

			if (document.RootElement.TryGetProperty(
					"texture",
					out JsonElement value))
			{
				if (value.ValueKind != JsonValueKind.String ||
					string.IsNullOrWhiteSpace(value.GetString()))
				{
					throw new InvalidDataException(
						$"'{id}' must set texture as a non-empty path.");
				}

				texture = value.GetString()!.Trim();
			}

			return new MaterialAsset(
				id,
				Vector3.Clamp(color, Vector3.Zero, Vector3.One),
				texture);
		}
	}

	private static Vector3 ParseHex(string id, string text)
	{
		string value = text.Trim().TrimStart('#');

		if (value.Length == 8)
		{
			// Alpha is not part of the scene pipeline's shading yet, so
			// a trailing pair is read and ignored rather than rejected.
			value = value[..6];
		}

		if (value.Length != 6 ||
			!uint.TryParse(
				value,
				NumberStyles.HexNumber,
				CultureInfo.InvariantCulture,
				out uint packed))
		{
			throw new InvalidDataException(
				$"'{id}' has an invalid albedo colour '{text}'.");
		}

		return new Vector3(
			((packed >> 16) & 0xFF) / 255f,
			((packed >> 8) & 0xFF) / 255f,
			(packed & 0xFF) / 255f);
	}

	private static Vector3 ParseComponents(string id, JsonElement array)
	{
		if (array.GetArrayLength() != 3)
		{
			throw new InvalidDataException(
				$"'{id}' sets albedo as {array.GetArrayLength()} numbers; " +
				"three are required.");
		}

		Vector3 color = Vector3.Zero;
		int index = 0;

		foreach (JsonElement component in array.EnumerateArray())
		{
			if (component.ValueKind != JsonValueKind.Number ||
				!component.TryGetSingle(out float value) ||
				!float.IsFinite(value))
			{
				throw new InvalidDataException(
					$"'{id}' has an albedo component that is not a number.");
			}

			switch (index++)
			{
				case 0:
					color.X = value;
					break;
				case 1:
					color.Y = value;
					break;
				default:
					color.Z = value;
					break;
			}
		}

		return color;
	}
}
