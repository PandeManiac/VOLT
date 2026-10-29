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

		return RewriteFile(
			path,
			text => string.Equals(
				text,
				oldId,
				StringComparison.OrdinalIgnoreCase)
				? newId
				: null);
	}

	/// <summary>
	/// Rewrites every asset reference beneath one folder prefix.
	/// </summary>
	/// <remarks>
	/// A folder rename changes every file it contains at once. Walking
	/// the scenes once with a prefix keeps that operation proportional
	/// to the number of scene files rather than the number of assets.
	/// </remarks>
	public static int ReplaceFolderInFile(
		string path,
		string oldFolder,
		string newFolder)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentException.ThrowIfNullOrWhiteSpace(oldFolder);
		ArgumentException.ThrowIfNullOrWhiteSpace(newFolder);

		string oldPrefix = oldFolder.EndsWith('/')
			? oldFolder
			: oldFolder + "/";
		string newPrefix = newFolder.EndsWith('/')
			? newFolder
			: newFolder + "/";

		if (string.Equals(oldPrefix, newPrefix, StringComparison.Ordinal))
		{
			return 0;
		}

		return RewriteFile(
			path,
			text => text.StartsWith(
				oldPrefix,
				StringComparison.OrdinalIgnoreCase)
				? newPrefix + text[oldPrefix.Length..]
				: null);
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

		return ReplaceInDocument(
			root,
			text => string.Equals(
				text,
				oldId,
				StringComparison.OrdinalIgnoreCase)
				? newId
				: null);
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

		return ReplaceInNode(
			node,
			text => string.Equals(
				text,
				oldId,
				StringComparison.OrdinalIgnoreCase)
				? newId
				: null);
	}

	private static int RewriteFile(
		string path,
		Func<string, string?> replace)
	{
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

		int changed = ReplaceInDocument(root, replace);

		if (changed > 0)
		{
			SceneSerializer.WriteAtomically(
				path,
				root.ToJsonString(SceneSerializer.Options));
		}

		return changed;
	}

	private static int ReplaceInDocument(
		JsonNode root,
		Func<string, string?> replace)
	{
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

				changed += ReplaceInNode(data, replace);
			}
		}

		return changed;
	}

	private static int ReplaceInNode(
		JsonNode node,
		Func<string, string?> replace)
	{
		int replaced = 0;

		switch (node)
		{
			case JsonObject obj:
				string[] keys = [.. obj.Select(pair => pair.Key)];

				foreach (string key in keys)
				{
					JsonNode? child = obj[key];

					if (child is JsonValue value &&
						value.TryGetValue<string>(out string? text) &&
						replace(text) is string replacement &&
						!string.Equals(text, replacement, StringComparison.Ordinal))
					{
						obj[key] = JsonValue.Create(replacement);
						replaced++;
					}

					else if (child is not null)
					{
						replaced += ReplaceInNode(child, replace);
					}
				}

				return replaced;

			case JsonArray array:
				for (int index = 0; index < array.Count; index++)
				{
					JsonNode? child = array[index];

					if (child is JsonValue value &&
						value.TryGetValue<string>(out string? text) &&
						replace(text) is string replacement &&
						!string.Equals(text, replacement, StringComparison.Ordinal))
					{
						array[index] = JsonValue.Create(replacement);
						replaced++;
					}

					else if (child is not null)
					{
						replaced += ReplaceInNode(child, replace);
					}
				}

				return replaced;

			default:
				return 0;
		}
	}

	private static void ValidateIds(string oldId, string newId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
		ArgumentNullException.ThrowIfNull(newId);
	}
}
