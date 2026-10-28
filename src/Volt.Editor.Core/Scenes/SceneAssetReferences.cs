using System.Text.Json;
using System.Text.Json.Nodes;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Rewrites asset paths stored inside scene component data.
/// </summary>
/// <remarks>
/// Only component data is traversed, so entity names, scene names, and
/// other document metadata are never treated as asset references. The
/// walk is intentionally structure-preserving for unknown component
/// payloads: properties this build does not understand still carry
/// their asset strings.
/// </remarks>
internal static class SceneAssetReferences
{
	public static int ReplaceInFile(
		string path,
		string oldId,
		string newId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ValidateIds(oldId, newId);

		if (string.Equals(oldId, newId, StringComparison.Ordinal))
		{
			return 0;
		}

		string json;

		try
		{
			json = File.ReadAllText(path);
		}

		catch (Exception exception)
			when (exception is IOException or UnauthorizedAccessException)
		{
			throw new IOException(
				$"Could not read the scene '{path}'.",
				exception);
		}

		JsonNode? root;

		try
		{
			root = JsonNode.Parse(json);
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				$"The scene '{path}' is malformed.",
				exception);
		}

		if (root is null)
		{
			return 0;
		}

		int changed = ReplaceInDocument(root, oldId, newId);

		if (changed > 0)
		{
			SceneSerializer.WriteAtomically(
				path,
				root.ToJsonString(SceneSerializer.Options));
		}

		return changed;
	}

	public static int ReplaceInDocument(
		JsonNode root,
		string oldId,
		string newId)
	{
		ArgumentNullException.ThrowIfNull(root);
		ValidateIds(oldId, newId);

		if (string.Equals(oldId, newId, StringComparison.Ordinal))
		{
			return 0;
		}

		if (root is not JsonObject document ||
			document["entities"] is not JsonArray entities)
		{
			return 0;
		}

		int changed = 0;

		foreach (JsonNode? entityNode in entities)
		{
			if (entityNode is not JsonObject entity ||
				entity["components"] is not JsonArray components)
			{
				continue;
			}

			foreach (JsonNode? componentNode in components)
			{
				if (componentNode is not JsonObject component ||
					component["data"] is not JsonObject data)
				{
					continue;
				}

				changed += ReplaceInNode(data, oldId, newId);
			}
		}

		return changed;
	}

	public static int ReplaceInNode(
		JsonNode node,
		string oldId,
		string newId)
	{
		ArgumentNullException.ThrowIfNull(node);
		ValidateIds(oldId, newId);

		if (string.Equals(oldId, newId, StringComparison.Ordinal))
		{
			return 0;
		}

		int replaced = 0;

		switch (node)
		{
			case JsonObject obj:
				string[] keys = [.. obj.Select(pair => pair.Key)];

				foreach (string key in keys)
				{
					JsonNode? child = obj[key];

					if (IsMatch(child, oldId))
					{
						obj[key] = JsonValue.Create(newId);
						replaced++;
					}

					else if (child is not null)
					{
						replaced += ReplaceInNode(child, oldId, newId);
					}
				}

				return replaced;

			case JsonArray array:
				for (int index = 0; index < array.Count; index++)
				{
					JsonNode? child = array[index];

					if (IsMatch(child, oldId))
					{
						array[index] = JsonValue.Create(newId);
						replaced++;
					}

					else if (child is not null)
					{
						replaced += ReplaceInNode(child, oldId, newId);
					}
				}

				return replaced;

			default:
				return 0;
		}
	}

	private static bool IsMatch(JsonNode? node, string oldId)
	{
		return node is JsonValue value &&
			value.TryGetValue<string>(out string? text) &&
			string.Equals(text, oldId, StringComparison.OrdinalIgnoreCase);
	}

	private static void ValidateIds(string oldId, string newId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
		ArgumentNullException.ThrowIfNull(newId);
	}
}
