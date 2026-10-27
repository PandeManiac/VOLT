using System.Text.Json;
using System.Text.Json.Serialization;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Projects;

/// <summary>
/// A VOLT project: its manifest, scene files, assets, and startup
/// scene.
/// </summary>
/// <remarks>
/// Every path accepted by this type is project-relative. Resolving a
/// path rejects absolute paths and traversal outside the project root,
/// so a malformed manifest cannot make the editor write elsewhere.
/// </remarks>
public sealed class EditorProject
{
	public const int CurrentVersion = 1;

	public const string ManifestFileName = "project.volt.json";

	public const string DefaultStartupScene =
		"Scenes/Main.volt.scene.json";

	private static readonly JsonSerializerOptions s_jsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private EditorProject(
		string rootPath,
		Guid id,
		string name,
		string startupScene)
	{
		RootPath = rootPath;
		Id = id;
		Name = name;
		StartupScenePath = startupScene;
		AssetCatalog = new ProjectAssetCatalog(rootPath);
	}

	public string RootPath { get; }

	public Guid Id { get; }

	public string Name { get; set; }

	public string StartupScenePath { get; set; }

	public ProjectAssetCatalog AssetCatalog { get; }

	public string AssetsPath => Path.Combine(RootPath, "Assets");

	public string ScenesPath => Path.Combine(RootPath, "Scenes");

	public string ManifestPath => Path.Combine(RootPath, ManifestFileName);

	public static EditorProject Create(string rootPath, string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		string fullRoot = Path.GetFullPath(rootPath);

		if (Directory.Exists(fullRoot) &&
			Directory.EnumerateFileSystemEntries(fullRoot).Any())
		{
			throw new InvalidOperationException(
				$"The project directory '{fullRoot}' already contains " +
				"files, so no project was written there. Choose a folder " +
				"that does not exist yet or is empty, or open the existing " +
				"project instead.");
		}

		Directory.CreateDirectory(fullRoot);
		Directory.CreateDirectory(Path.Combine(fullRoot, "Assets"));
		Directory.CreateDirectory(Path.Combine(fullRoot, "Scenes"));

		EditorProject project = new(
			fullRoot,
			Guid.NewGuid(),
			name.Trim(),
			DefaultStartupScene);

		SceneDocument startup = CreateStarterScene(project);
		project.SaveScene(startup, DefaultStartupScene);
		project.SaveManifest();
		project.AssetCatalog.Refresh();

		return project;
	}

	public static EditorProject Open(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		string fullRoot = Path.GetFullPath(rootPath);
		string manifestPath = Path.Combine(fullRoot, ManifestFileName);

		if (!File.Exists(manifestPath))
		{
			throw new FileNotFoundException(
				$"No VOLT project manifest was found at '{manifestPath}'.",
				manifestPath);
		}

		ProjectManifestDto? dto;

		try
		{
			dto = JsonSerializer.Deserialize<ProjectManifestDto>(
				File.ReadAllText(manifestPath),
				s_jsonOptions);
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				$"The project manifest '{manifestPath}' is malformed.",
				exception);
		}

		if (dto is null)
		{
			throw new InvalidDataException(
				"The project manifest is empty.");
		}

		if (dto.Version != CurrentVersion)
		{
			throw new InvalidDataException(
				$"Unsupported project format version {dto.Version}.");
		}

		if (dto.Id == Guid.Empty ||
			string.IsNullOrWhiteSpace(dto.Name) ||
			string.IsNullOrWhiteSpace(dto.StartupScene))
		{
			throw new InvalidDataException(
				"The project manifest is missing required fields.");
		}

		EditorProject project = new(
			fullRoot,
			dto.Id,
			dto.Name,
			dto.StartupScene);

		// Resolve before returning so an unsafe or missing startup path
		// is reported while opening, not later during Play.
		_ = project.ResolvePath(project.StartupScenePath);

		project.AssetCatalog.Refresh();
		return project;
	}

	public void SaveManifest()
	{
		Directory.CreateDirectory(RootPath);

		ProjectManifestDto dto = new()
		{
			Version = CurrentVersion,
			Id = Id,
			Name = Name,
			StartupScene = StartupScenePath
		};

		AtomicWrite(
			ManifestPath,
			JsonSerializer.Serialize(dto, s_jsonOptions));
	}

	public SceneDocument LoadStartupScene()
	{
		return LoadScene(StartupScenePath);
	}

	public SceneDocument LoadScene(string relativePath)
	{
		string path = ResolvePath(relativePath);

		if (!File.Exists(path))
		{
			throw new FileNotFoundException(
				$"The scene '{relativePath}' does not exist.",
				path);
		}

		return SceneSerializer.Load(path);
	}

	public void SaveScene(SceneDocument scene, string relativePath)
	{
		ArgumentNullException.ThrowIfNull(scene);

		string path = ResolvePath(relativePath);
		SceneSerializer.Save(path, scene);
		AssetCatalog.Refresh();
	}

	public SceneDocument CreateScene(string relativePath, string sceneName)
	{
		string path = ResolvePath(relativePath);

		if (File.Exists(path))
		{
			throw new IOException(
				$"A scene already exists at '{relativePath}'.");
		}

		SceneDocument scene = new(sceneName);
		SaveScene(scene, relativePath);
		return scene;
	}

	public IReadOnlyList<string> EnumerateScenePaths()
	{
		if (!Directory.Exists(ScenesPath))
		{
			return [];
		}

		List<string> result = [];

		foreach (string path in Directory.EnumerateFiles(
			ScenesPath,
			"*" + SceneSerializer.FileExtension,
			SearchOption.AllDirectories))
		{
			string relative = Path.GetRelativePath(RootPath, path);
			result.Add(ProjectAssetCatalog.NormalizeId(relative));
		}

		result.Sort(StringComparer.OrdinalIgnoreCase);
		return result;
	}

	/// <summary>
	/// Resolves a project-relative path and rejects traversal outside
	/// the project root.
	/// </summary>
	public string ResolvePath(string relativePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

		if (Path.IsPathRooted(relativePath))
		{
			throw new ArgumentException(
				"Project paths must be relative.",
				nameof(relativePath));
		}

		string normalized = relativePath.Replace(
			'/',
			Path.DirectorySeparatorChar);

		string fullPath = Path.GetFullPath(
			Path.Combine(RootPath, normalized));

		string prefix = RootPath.EndsWith(
			Path.DirectorySeparatorChar.ToString(),
			StringComparison.Ordinal)
			? RootPath
			: RootPath + Path.DirectorySeparatorChar;

		if (!fullPath.StartsWith(prefix, StringComparison.Ordinal))
		{
			throw new ArgumentException(
				"The path escapes the project root.",
				nameof(relativePath));
		}

		return fullPath;
	}

	private static SceneDocument CreateStarterScene(EditorProject project)
	{
		SceneDocument scene = new("Main");

		SceneEntity camera = scene.CreateEntity("Main Camera");
		camera.Transform.Position = new System.Numerics.Vector3(0f, 1.5f, 6f);
		camera.AddComponent(new CameraData());

		SceneEntity cube = scene.CreateEntity("Cube");
		cube.AddComponent(new MeshRendererData
		{
			Mesh = "builtin:cube",
			Material = "builtin:default"
		});

		cube.AddComponent(new SpinBehaviourData
		{
			Axis = System.Numerics.Vector3.UnitY,
			DegreesPerSecond = 35f
		});

		return scene;
	}

	private static void AtomicWrite(string path, string contents)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		string temporary = $"{path}.tmp-{Guid.NewGuid():N}";

		try
		{
			File.WriteAllText(temporary, contents);
			File.Move(temporary, path, overwrite: true);
		}

		catch
		{
			try
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}

			catch (IOException)
			{
				// Preserve the original failure.
			}

			throw;
		}
	}

	internal sealed class ProjectManifestDto
	{
		public int Version { get; set; }

		public Guid Id { get; set; }

		public string? Name { get; set; }

		public string? StartupScene { get; set; }
	}
}
