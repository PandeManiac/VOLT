using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Writes and reads <see cref="Vector3"/> as an object with
/// <c>x</c>, <c>y</c>, and <c>z</c> members.
/// </summary>
/// <remarks>
/// <see cref="Vector3"/> exposes public fields rather than properties,
/// so without an explicit converter the serializer emits an empty
/// object and silently discards authored component values such as a
/// behaviour axis. Keeping this converter next to the scene format
/// makes the wire representation explicit and stable.
/// </remarks>
public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
	public override Vector3 Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartObject)
		{
			throw new JsonException(
				"A vector must be a JSON object with x, y, and z.");
		}

		float x = 0f;
		float y = 0f;
		float z = 0f;

		while (reader.Read())
		{
			if (reader.TokenType == JsonTokenType.EndObject)
			{
				return new Vector3(x, y, z);
			}

			if (reader.TokenType != JsonTokenType.PropertyName)
			{
				throw new JsonException(
					"A vector member must be a property name.");
			}

			string name = reader.GetString() ?? string.Empty;

			if (!reader.Read())
			{
				throw new JsonException("The vector is truncated.");
			}

			float value = reader.TokenType == JsonTokenType.Number
				? reader.GetSingle()
				: throw new JsonException(
					$"Vector member '{name}' must be a number.");

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

				default:
					reader.Skip();
					break;
			}
		}

		throw new JsonException("The vector is truncated.");
	}

	public override void Write(
		Utf8JsonWriter writer,
		Vector3 value,
		JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteNumber("x", value.X);
		writer.WriteNumber("y", value.Y);
		writer.WriteNumber("z", value.Z);
		writer.WriteEndObject();
	}
}
