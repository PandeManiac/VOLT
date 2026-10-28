using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Writes and reads <see cref="Vector4"/> as an object with
/// <c>x</c>, <c>y</c>, <c>z</c>, and <c>w</c> members.
/// </summary>
/// <remarks>
/// Like <see cref="Vector3"/>, <see cref="Vector4"/> exposes public
/// fields rather than properties, so without an explicit converter the
/// serializer emits an empty object and silently discards authored
/// values such as a light color. Keeping this converter next to the
/// scene format makes the wire representation explicit and stable.
/// </remarks>
public sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
	public override Vector4 Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException(
				"A color must be a JSON object with x, y, z, and w.");
		}

		float x = 0f;
		float y = 0f;
		float z = 0f;
		float w = 0f;

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return new Vector4(x, y, z, w);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
			{
				throw new JsonException(
					"A color member must be a property name.");
			}

			string name = reader.GetString() ?? string.Empty;

			if (!reader.Read())
			{
				throw new JsonException("The color is truncated.");
			}

			float value = reader.TokenType == JsonTokenType.Number
				? reader.GetSingle()
				: throw new JsonException(
					$"Color member '{name}' must be a number.");

			switch (name)
			{
				case "x":
					x = value;
					break;

				case "y":
					y = value;
					break;

				case "z":
					z = value;
					break;

				case "w":
					w = value;
					break;

				default:
					reader.Skip();
					break;
			}
		}

		throw new JsonException("The color is truncated.");
	}

	public override void Write(
		Utf8JsonWriter writer,
		Vector4 value,
		JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteNumber("x", value.X);
		writer.WriteNumber("y", value.Y);
		writer.WriteNumber("z", value.Z);
		writer.WriteNumber("w", value.W);
		writer.WriteEndObject();
	}
}
