using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Versioned, validated JSON persistence for authored scenes.
/// </summary>
/// <remarks>
/// Writes go to a sibling temporary file and are moved into place
/// only after the new contents are complete. A failed save therefore
/// cannot truncate the previous scene file.
/// </remarks>
public static class SceneSerializer
{
	public const int CurrentVersion = 1;

	public const string FileExtension = ".volt.scene.json";

	public static JsonSerializerOptions Options { get; } = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters =
		{
			new Vector3JsonConverter(),
			new Vector4JsonConverter()
		}
	};

	public static string Serialize(SceneDocument scene)
	{
		ArgumentNullException.ThrowIfNull(scene);

		SceneDocumentDto dto = new()
		{
			Version = CurrentVersion,
			Id = scene.Id,
			Name = scene.Name,
			Entities = [.. scene.Entities.Select(entity => new SceneEntityDto
			{
				Id = entity.Id,
				Name = entity.Name,
				ParentId = entity.ParentId,
				Transform = new TransformDto
				{
					Position = Vector3Dto.From(entity.Transform.Position),
					EulerAngles = Vector3Dto.From(entity.Transform.EulerAngles),
					Scale = Vector3Dto.From(entity.Transform.Scale)
				},
				Components = [.. entity.Components.Select(ToDto)]
			})]
		};

		return JsonSerializer.Serialize(dto, Options);
	}

	public static SceneDocument Deserialize(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);

		SceneDocumentDto? dto;

		try
		{
			dto = JsonSerializer.Deserialize<SceneDocumentDto>(json, Options);
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				"The scene JSON is malformed.",
				exception);
		}

		if (dto is null)
		{
			throw new InvalidDataException("The scene document is empty.");
		}

		if (dto.Version != CurrentVersion)
		{
			throw new InvalidDataException(
				$"Unsupported scene format version {dto.Version}. " +
				$"This build reads version {CurrentVersion}.");
		}

		if (dto.Id == Guid.Empty)
		{
			throw new InvalidDataException(
				"The scene identity is missing or empty.");
		}

		if (dto.Entities is null)
		{
			throw new InvalidDataException(
				"The scene has no entity collection.");
		}

		SceneDocument scene = new(
			dto.Id,
			string.IsNullOrWhiteSpace(dto.Name) ? "Untitled" : dto.Name);

		HashSet<Guid> ids = [];

		foreach (SceneEntityDto entityDto in dto.Entities)
		{
			if (entityDto.Id == Guid.Empty || !ids.Add(entityDto.Id))
			{
				throw new InvalidDataException(
					"The scene contains an empty or duplicate entity " +
					"identity.");
			}

			SceneEntity entity = new(
				entityDto.Id,
				string.IsNullOrWhiteSpace(entityDto.Name)
					? "GameObject"
					: entityDto.Name)
			{
				ParentId = entityDto.ParentId
			};

			TransformDto transform = entityDto.Transform ??
				throw new InvalidDataException(
					$"Entity '{entityDto.Name}' has no transform.");

			entity.Transform.Position =
				transform.Position?.ToVector3() ?? Vector3.Zero;

			entity.Transform.EulerAngles =
				transform.EulerAngles?.ToVector3() ?? Vector3.Zero;

			entity.Transform.Scale =
				transform.Scale?.ToVector3() ?? Vector3.One;

			HashSet<string> componentTypes = new(StringComparer.Ordinal);

			foreach (ComponentDto componentDto in
				entityDto.Components ?? [])
			{
				if (string.IsNullOrWhiteSpace(componentDto.TypeId) ||
					!componentTypes.Add(componentDto.TypeId))
				{
					throw new InvalidDataException(
						$"Entity '{entity.Name}' contains an empty or " +
						"duplicate component type.");
				}

				entity.AddLoadedComponent(
					FromDto(componentDto, entity.Name));
			}

			scene.InsertLoaded(entity, scene.EntityCount);
		}

		scene.ValidateLoad();

		return scene;
	}

	public static void Save(string path, SceneDocument scene)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(scene);

		WriteAtomically(Path.GetFullPath(path), Serialize(scene));
	}

	/// <summary>
	/// Writes text to a sibling temporary file and moves it into place.
	/// </summary>
	internal static void WriteAtomically(string path, string contents)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(contents);

		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);

		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		string temporaryPath =
			$"{fullPath}.tmp-{Guid.NewGuid():N}";

		try
		{
			File.WriteAllText(temporaryPath, contents);
			File.Move(temporaryPath, fullPath, overwrite: true);
		}

		catch
		{
			try
			{
				if (File.Exists(temporaryPath))
				{
					File.Delete(temporaryPath);
				}
			}

			catch (IOException)
			{
				// Preserve the original failure; the temporary file is
				// harmless and can be cleaned up later.
			}

			throw;
		}
	}

	public static SceneDocument Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		string json;

		try
		{
			json = File.ReadAllText(path);
		}

		catch (Exception exception)
			when (exception is IOException or UnauthorizedAccessException)
		{
			throw new IOException(
				$"Could not read the scene file '{path}'.",
				exception);
		}

		try
		{
			return Deserialize(json);
		}

		catch (InvalidDataException exception)
		{
			throw new InvalidDataException(
				$"Invalid scene file '{path}': {exception.Message}",
				exception);
		}
	}

	private static ComponentDto ToDto(AuthoredComponent component)
	{
		if (component is UnknownComponentData unknown)
		{
			return new ComponentDto
			{
				TypeId = unknown.TypeId,
				Data = unknown.Data.ValueKind == JsonValueKind.Undefined
					? JsonSerializer.SerializeToElement(
						new { },
						Options)
					: unknown.Data.Clone()
			};
		}

		EditorComponentDescriptor descriptor =
			ComponentCatalog.Default.GetForType(component.GetType());

		return new ComponentDto
		{
			TypeId = descriptor.TypeId,
			Data = JsonSerializer.SerializeToElement(
				component,
				descriptor.ComponentType,
				Options)
		};
	}

	private static AuthoredComponent FromDto(
		ComponentDto dto,
		string entityName)
	{
		if (!ComponentCatalog.Default.TryGet(
			dto.TypeId,
			out EditorComponentDescriptor descriptor))
		{
			JsonElement raw = dto.Data.ValueKind == JsonValueKind.Undefined
				? JsonSerializer.SerializeToElement(new { }, Options)
				: dto.Data.Clone();

			return new UnknownComponentData(dto.TypeId, raw);
		}

		if (dto.Data.ValueKind == JsonValueKind.Undefined)
		{
			return descriptor.Create();
		}

		try
		{
			return (AuthoredComponent?)JsonSerializer.Deserialize(
				dto.Data,
				descriptor.ComponentType,
				Options)
				?? throw new InvalidDataException(
					$"Component '{dto.TypeId}' on entity " +
					$"'{entityName}' has no value.");
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				$"Component '{dto.TypeId}' on entity '{entityName}' " +
				"could not be read.",
				exception);
		}
	}

	internal sealed class SceneDocumentDto
	{
		public int Version { get; set; }

		public Guid Id { get; set; }

		public string? Name { get; set; }

		public List<SceneEntityDto>? Entities { get; set; }
	}

	internal sealed class SceneEntityDto
	{
		public Guid Id { get; set; }

		public string? Name { get; set; }

		public Guid? ParentId { get; set; }

		public TransformDto? Transform { get; set; }

		public List<ComponentDto>? Components { get; set; }
	}

	internal sealed class TransformDto
	{
		public Vector3Dto? Position { get; set; }

		public Vector3Dto? EulerAngles { get; set; }

		public Vector3Dto? Scale { get; set; }
	}

	internal sealed class Vector3Dto
	{
		public float X { get; set; }

		public float Y { get; set; }

		public float Z { get; set; }

		public static Vector3Dto From(Vector3 value)
		{
			return new Vector3Dto
			{
				X = value.X,
				Y = value.Y,
				Z = value.Z
			};
		}

		public Vector3 ToVector3()
		{
			return new Vector3(X, Y, Z);
		}
	}

	internal sealed class ComponentDto
	{
		public string TypeId { get; set; } = string.Empty;

		public JsonElement Data { get; set; }
	}
}
