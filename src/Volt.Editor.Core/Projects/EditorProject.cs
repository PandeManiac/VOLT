using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Gameplay;
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

	private readonly Dictionary<string, MeshCacheEntry> _meshes =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, MaterialCacheEntry> _materials =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, TextureCacheEntry> _textures =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly Lock _meshGate = new();

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

	/// <summary>
	/// Gets or sets the project-relative path of the C# project that holds
	/// this project's gameplay code, when it has one.
	/// </summary>
	/// <remarks>
	/// Game code lives outside <c>Assets</c> so its build output never
	/// appears in the asset browser. The path is optional: a project that
	/// predates game-code support, or one whose scripts were removed, opens
	/// exactly as before and simply runs the built-in behaviours.
	/// </remarks>
	public string? GameplayProjectPath { get; private set; }

	/// <summary>
	/// Gets the project-relative path of the built gameplay assembly.
	/// </summary>
	public string? GameplayAssemblyPath { get; private set; }

	/// <summary>
	/// Gets why game code was not scaffolded when a project was created.
	/// </summary>
	/// <remarks>
	/// <see langword="null"/> when the scaffold was written, or when the
	/// project was opened rather than created.
	/// </remarks>
	public string? GameplayScaffoldReason { get; private set; }

	public string AssetsPath => Path.Combine(RootPath, "Assets");

	public string ScenesPath => Path.Combine(RootPath, "Scenes");

	public string ManifestPath => Path.Combine(RootPath, ManifestFileName);

	/// <summary>
	/// Loads a mesh asset a scene references.
	/// </summary>
	/// <param name="meshId">The project-relative asset identifier.</param>
	/// <param name="mesh">The loaded geometry when this succeeds.</param>
	/// <param name="diagnostic">
	/// Why the mesh could not be loaded, when it could not be. A scene
	/// that names a broken file has to stay openable, so a failure is
	/// reported rather than thrown.
	/// </param>
	/// <returns>Whether the mesh is available to draw.</returns>
	/// <remarks>
	/// Loaded geometry is cached against the file's size and write time,
	/// so painting a viewport does not re-read or re-parse it every
	/// frame, while an edited file is picked up on the next lookup.
	/// </remarks>
	public bool TryLoadMesh(
		string meshId,
		out ImportedMesh mesh,
		out string? diagnostic)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(meshId);

		mesh = null!;
		diagnostic = null;

		ProjectAsset? asset = AssetCatalog.Find(meshId);

		if (asset is null)
		{
			AssetCatalog.Refresh();
			asset = AssetCatalog.Find(meshId);
		}

		if (asset is null)
		{
			diagnostic = $"'{meshId}' is not part of this project.";
			return false;
		}

		if (!asset.FileName.EndsWith(
			".obj",
			StringComparison.OrdinalIgnoreCase))
		{
			diagnostic =
				$"'{asset.FileName}' is not a mesh format this build can " +
				"import yet.";
			return false;
		}

		lock (_meshGate)
		{
			if (_meshes.TryGetValue(asset.Id, out MeshCacheEntry cached) &&
				cached.Matches(asset))
			{
				mesh = cached.Mesh!;
				diagnostic = cached.Diagnostic;
				return cached.Mesh is not null;
			}

			try
			{
				mesh = ObjMeshLoader.Load(asset.Id, asset.FullPath);

				_meshes[asset.Id] = new MeshCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Mesh: mesh,
					Diagnostic: null);

				return true;
			}

			catch (Exception exception)
				when (exception is IOException or
					UnauthorizedAccessException or
					InvalidDataException or
					DecoderFallbackException)
			{
				diagnostic = $"'{asset.FileName}' could not be read: " +
					exception.Message;

				_meshes[asset.Id] = new MeshCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Mesh: null,
					Diagnostic: diagnostic);

				return false;
			}
		}
	}

	/// <summary>
	/// Loads an image a material references.
	/// </summary>
	/// <param name="textureId">The project-relative path.</param>
	/// <param name="image">The decoded image when this succeeds.</param>
	/// <param name="diagnostic">
	/// Why the image could not be loaded, when it could not be.
	/// </param>
	/// <returns>Whether the image is available to sample.</returns>
	/// <remarks>
	/// Decoded pixels are cached against the file's size and write time,
	/// so painting a viewport samples the same image instead of decoding
	/// it every frame, while an edited file is picked up on the next
	/// lookup.
	/// </remarks>
	public bool TryLoadTexture(
		string textureId,
		out PngImage image,
		out string? diagnostic)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(textureId);

		image = null!;
		diagnostic = null;

		ProjectAsset? asset = AssetCatalog.Find(textureId);

		if (asset is null)
		{
			AssetCatalog.Refresh();
			asset = AssetCatalog.Find(textureId);
		}

		if (asset is null)
		{
			diagnostic = $"'{textureId}' is not part of this project.";
			return false;
		}

		if (!asset.FileName.EndsWith(
			".png",
				StringComparison.OrdinalIgnoreCase))
		{
			diagnostic =
				$"'{asset.FileName}' is not an image format this build can " +
				"import yet.";
			return false;
		}

		lock (_meshGate)
		{
			if (_textures.TryGetValue(
					asset.Id,
					out TextureCacheEntry cached) &&
				cached.Matches(asset))
			{
				image = cached.Image!;
				diagnostic = cached.Diagnostic;
				return cached.Image is not null;
			}

			try
			{
				image = PngDecoder.DecodeFile(asset.FullPath);

				_textures[asset.Id] = new TextureCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Image: image,
					Diagnostic: null);

				return true;
			}

			catch (Exception exception)
				when (exception is IOException or
					UnauthorizedAccessException or
					InvalidDataException)
			{
				diagnostic = $"'{asset.FileName}' could not be read: " +
					exception.Message;

				_textures[asset.Id] = new TextureCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Image: null,
					Diagnostic: diagnostic);

				return false;
			}
		}
	}

	/// <summary>
	/// Loads a material asset a scene references.
	/// </summary>
	/// <param name="materialId">The project-relative asset identifier.</param>
	/// <param name="material">The loaded material when this succeeds.</param>
	/// <param name="diagnostic">
	/// Why the material could not be loaded, when it could not be.
	/// </param>
	/// <returns>Whether the material is available to apply.</returns>
	/// <remarks>
	/// The built-in default is not a file: it is the shading every mesh
	/// already has, so asking for it is reported as missing here and the
	/// renderer treats the reference as no tint at all.
	/// </remarks>
	public bool TryLoadMaterial(
		string materialId,
		out MaterialAsset material,
		out string? diagnostic)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(materialId);

		material = null!;
		diagnostic = null;

		ProjectAsset? asset = AssetCatalog.Find(materialId);

		if (asset is null)
		{
			AssetCatalog.Refresh();
			asset = AssetCatalog.Find(materialId);
		}

		if (asset is null)
		{
			diagnostic = $"'{materialId}' is not part of this project.";
			return false;
		}

		if (asset.Kind != ProjectAssetKind.Material)
		{
			diagnostic =
				$"'{asset.FileName}' is not a material file.";
			return false;
		}

		lock (_meshGate)
		{
			if (_materials.TryGetValue(
					asset.Id,
					out MaterialCacheEntry cached) &&
				cached.Matches(asset))
			{
				material = cached.Material!;
				diagnostic = cached.Diagnostic;
				return cached.Material is not null;
			}

			try
			{
				material = MaterialLoader.Load(asset.Id, asset.FullPath);

				_materials[asset.Id] = new MaterialCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Material: material,
					Diagnostic: null);

				return true;
			}

			catch (Exception exception)
				when (exception is IOException or
					UnauthorizedAccessException or
					InvalidDataException or
					DecoderFallbackException)
			{
				diagnostic = $"'{asset.FileName}' could not be read: " +
					exception.Message;

				_materials[asset.Id] = new MaterialCacheEntry(
					asset.Length,
					asset.LastWriteTimeUtc,
					Material: null,
					Diagnostic: diagnostic);

				return false;
			}
		}
	}

	/// <summary>
	/// Creates a new material asset with the default colour.
	/// </summary>
	/// <remarks>
	/// The file is named against the existing assets, so creating several
	/// materials never overwrites one. The folder is created on demand:
	/// a project without materials needs no placeholder to start.
	/// </remarks>
	/// <returns>The project-relative identity of the new material.</returns>
	public string CreateMaterial()
	{
		string folder = Path.Combine(AssetsPath, "Materials");
		Directory.CreateDirectory(folder);

		int index = 0;
		string path;

		do
		{
			string name = index == 0 ? "Material" : $"Material {index}";
			path = Path.Combine(folder, name + ".material");
			index++;
		}
		while (File.Exists(path));

		File.WriteAllText(path, MaterialLoader.CreateDefaultJson());
		AssetCatalog.Refresh();

		return ProjectAssetCatalog.NormalizeId(
			Path.GetRelativePath(RootPath, path));
	}

	private readonly record struct MaterialCacheEntry(
		long Length,
		DateTime LastWriteTimeUtc,
		MaterialAsset? Material,
		string? Diagnostic)
	{
		public bool Matches(ProjectAsset asset)
		{
			return Length == asset.Length &&
				LastWriteTimeUtc == asset.LastWriteTimeUtc;
		}
	}

	private readonly record struct TextureCacheEntry(
		long Length,
		DateTime LastWriteTimeUtc,
		PngImage? Image,
		string? Diagnostic)
	{
		public bool Matches(ProjectAsset asset)
		{
			return Length == asset.Length &&
				LastWriteTimeUtc == asset.LastWriteTimeUtc;
		}
	}

	private readonly record struct MeshCacheEntry(
		long Length,
		DateTime LastWriteTimeUtc,
		ImportedMesh? Mesh,
		string? Diagnostic)
	{
		public bool Matches(ProjectAsset asset)
		{
			return Length == asset.Length &&
				LastWriteTimeUtc == asset.LastWriteTimeUtc;
		}
	}

	/// <summary>
	/// Records the project's game-code paths and saves the manifest.
	/// </summary>
	/// <remarks>
	/// Both paths are validated as project-relative before they are
	/// stored, so a scripted or malformed call cannot point the editor at
	/// a file outside the project. Clearing both removes the gameplay
	/// section, which returns the project to built-in behaviours only.
	/// </remarks>
	public void AttachGameplayProject(
		string? projectRelativePath,
		string? assemblyRelativePath)
	{
		AttachGameplayPaths(projectRelativePath, assemblyRelativePath);
		SaveManifest();
	}

	/// <summary>
	/// Resolves the built gameplay assembly, whether or not the manifest's
	/// recorded path is still the newest build.
	/// </summary>
	/// <remarks>
	/// A developer's own <c>dotnet build</c> may produce Debug output while
	/// the engine builds Release, so the script folder is searched for the
	/// newest assembly when the recorded path is missing.
	/// </remarks>
	public bool TryResolveGameplayAssembly(out string assemblyPath)
	{
		assemblyPath = string.Empty;

		if (!string.IsNullOrWhiteSpace(GameplayAssemblyPath))
		{
			string candidate = ResolvePath(GameplayAssemblyPath);

			if (File.Exists(candidate))
			{
				assemblyPath = candidate;
				return true;
			}
		}

		if (GameplayProjectPath is null)
		{
			return false;
		}

		string? built = GameplayProjectScaffold.FindBuiltAssembly(RootPath);

		if (built is null)
		{
			return false;
		}

		assemblyPath = built;
		return true;
	}

	/// <summary>
	/// Gets the full path of the project's game-code project file, or
	/// <see langword="null"/> when it has none.
	/// </summary>
	public string? ResolveGameplayProjectPath()
	{
		if (!string.IsNullOrWhiteSpace(GameplayProjectPath))
		{
			string candidate = ResolvePath(GameplayProjectPath);

			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		return GameplayProjectScaffold.FindScriptProject(RootPath);
	}

	public static EditorProject Create(
		string rootPath,
		string name,
		string? engineRoot = null)
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

		// A new project owns real game code from the start: a buildable
		// C# project with a working component, binding, and system. When no
		// engine checkout can be found to reference, the project is still
		// created and the reason is recorded for the caller to report.
		GameplayScaffoldResult scaffold = GameplayProjectScaffold.Create(
			fullRoot,
			project.Name,
			engineRoot);

		if (scaffold.Created)
		{
			project.GameplayProjectPath = ProjectAssetCatalog.NormalizeId(
				Path.GetRelativePath(fullRoot, scaffold.ProjectPath));

			project.GameplayAssemblyPath = ProjectAssetCatalog.NormalizeId(
				Path.GetRelativePath(fullRoot, scaffold.AssemblyPath));
		}

		else
		{
			project.GameplayScaffoldReason = scaffold.SkipReason;
		}

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

		if (dto.Gameplay is GameplayManifestDto gameplay)
		{
			project.AttachGameplayPaths(gameplay.Project, gameplay.Assembly);
		}

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
			StartupScene = StartupScenePath,
			Gameplay = GameplayProjectPath is null &&
				GameplayAssemblyPath is null
					? null
					: new GameplayManifestDto
					{
						Project = GameplayProjectPath,
						Assembly = GameplayAssemblyPath
					}
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

	/// <summary>
	/// Creates a folder beneath Assets or Scenes.
	/// </summary>
	/// <remarks>
	/// Only project-visible roots are accepted. The parent folder must
	/// already exist, and an existing file or directory at the target
	/// path refuses the operation. An empty folder is a real project
	/// folder: refreshing the catalog records it and the browser shows
	/// it before anything is added.
	/// </remarks>
	/// <returns>The normalized project-relative folder identity.</returns>
	public string CreateFolder(string relativePath)
	{
		ArgumentNullException.ThrowIfNull(relativePath);

		string id = ProjectAssetCatalog.NormalizeId(relativePath).TrimEnd('/');

		if (id.Length == 0)
		{
			throw new ArgumentException(
				"A folder path is required.",
				nameof(relativePath));
		}

		string[] segments = id.Split('/');

		if (segments.Length < 2 ||
			(!string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(segments[0], "Scenes", StringComparison.OrdinalIgnoreCase)))
		{
			throw new ArgumentException(
				"Folders can only be created inside Assets or Scenes.",
				nameof(relativePath));
		}

		foreach (string segment in segments)
		{
			ValidateAssetName(segment, nameof(relativePath));
		}

		string destination = ResolvePath(id);

		if (File.Exists(destination) || Directory.Exists(destination))
		{
			throw new IOException(
				$"A file or folder already exists at '{id}'.");
		}

		string? parent = Path.GetDirectoryName(destination);

		if (parent is null || !Directory.Exists(parent))
		{
			throw new DirectoryNotFoundException(
				$"The parent folder for '{id}' does not exist.");
		}

		Directory.CreateDirectory(destination);
		AssetCatalog.Refresh();

		return id;
	}

	/// <summary>
	/// Copies an asset to a numbered sibling in the same folder.
	/// </summary>
	/// <remarks>
	/// The copy never overwrites an existing file. Scene assets keep
	/// their compound extension, so <c>Main.volt.scene.json</c> becomes
	/// <c>Main 1.volt.scene.json</c> instead of being split at the first
	/// dot.
	/// </remarks>
	/// <returns>The project-relative identity of the copy.</returns>
	public string DuplicateAsset(string relativePath)
	{
		return DuplicateAssets([relativePath])[0];
	}

	/// <summary>
	/// Copies several assets to numbered siblings in their own folders.
	/// </summary>
	/// <remarks>
	/// Every source is validated before the first copy is written, so a
	/// missing or unsafe member refuses the whole selection instead of
	/// leaving a partial set of copies behind. Copies are named against
	/// both the existing files and the destinations reserved earlier in
	/// the same operation, which keeps names unique when a selection
	/// already contains a numbered sibling. Scene assets keep their
	/// compound extension.
	/// </remarks>
	/// <returns>
	/// The project-relative identities of the copies, in source order
	/// with duplicate sources removed.
	/// </returns>
	public IReadOnlyList<string> DuplicateAssets(
		IEnumerable<string> relativePaths)
	{
		ArgumentNullException.ThrowIfNull(relativePaths);

		List<(string Source, string RelativeDirectory, string Stem, string Extension)>
			sources = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

		foreach (string relativePath in relativePaths)
		{
			string id = ProjectAssetCatalog.NormalizeId(relativePath);

			if (!seen.Add(id))
			{
				continue;
			}

			string source = ResolvePath(relativePath);

			if (!File.Exists(source))
			{
				throw new FileNotFoundException(
					$"The asset '{relativePath}' does not exist.",
					source);
			}

			string directory = Path.GetDirectoryName(source)!;
			(string stem, string extension) = SplitAssetName(
				Path.GetFileName(source));

			sources.Add((
				source,
				Path.GetRelativePath(RootPath, directory),
				stem,
				extension));
		}

		if (sources.Count == 0)
		{
			return [];
		}

		HashSet<string> reserved = new(StringComparer.OrdinalIgnoreCase);
		List<(string Source, string Destination)> copies = [];

		foreach ((string source, string relativeDirectory, string stem,
			string extension) in sources)
		{
			int index = 1;
			string destination;

			do
			{
				string copyName = $"{stem} {index}{extension}";
				string relativeCopy = relativeDirectory == "."
					? copyName
					: Path.Combine(relativeDirectory, copyName);

				destination = ResolvePath(relativeCopy);
				index++;
			}
			while (File.Exists(destination) ||
				Directory.Exists(destination) ||
				!reserved.Add(destination));

			copies.Add((source, destination));
		}

		foreach ((string source, string destination) in copies)
		{
			File.Copy(source, destination, overwrite: false);
		}

		AssetCatalog.Refresh();

		return [.. copies.Select(copy => ProjectAssetCatalog.NormalizeId(
			Path.GetRelativePath(RootPath, copy.Destination)))];
	}

	/// <summary>
	/// Renames an asset in place while keeping its file extension.
	/// </summary>
	/// <param name="relativePath">The current project-relative asset path.</param>
	/// <param name="newName">
	/// The new file name without its extension. Path separators and
	/// reserved filename characters are rejected.
	/// </param>
	/// <returns>The project-relative identity of the renamed asset.</returns>
	/// <remarks>
	/// Renaming the startup scene updates the manifest, so reopening
	/// the project still finds a startup scene. The scene's authored
	/// name is intentionally unchanged: this operation renames the file,
	/// not the document inside it.
	/// </remarks>
	public string RenameAsset(string relativePath, string newName)
	{
		string source = ResolvePath(relativePath);

		if (!File.Exists(source))
		{
			throw new FileNotFoundException(
				$"The asset '{relativePath}' does not exist.",
				source);
		}

		string trimmed = (newName ?? string.Empty).Trim();
		ValidateAssetName(trimmed, nameof(newName));

		(string stem, string extension) = SplitAssetName(
			Path.GetFileName(source));

		if (string.Equals(trimmed, stem, StringComparison.Ordinal))
		{
			return ProjectAssetCatalog.NormalizeId(relativePath);
		}

		if (extension.Length > 0 &&
			trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException(
				"Enter the name without the file extension.",
				nameof(newName));
		}

		string directory = Path.GetDirectoryName(source)!;
		string relativeDirectory = Path.GetRelativePath(RootPath, directory);
		string renamed = relativeDirectory == "."
			? trimmed + extension
			: Path.Combine(relativeDirectory, trimmed + extension);
		string destination = ResolvePath(renamed);

		if (File.Exists(destination) || Directory.Exists(destination))
		{
			throw new IOException(
				$"An asset already exists at '{renamed}'.");
		}

		string id = ProjectAssetCatalog.NormalizeId(relativePath);
		bool startupScene = string.Equals(
			ProjectAssetCatalog.NormalizeId(StartupScenePath),
			id,
			StringComparison.OrdinalIgnoreCase);

		File.Move(source, destination);

		string renamedId = ProjectAssetCatalog.NormalizeId(renamed);

		if (startupScene)
		{
			StartupScenePath = renamedId;
			SaveManifest();
		}

		ReplaceReferencesInScenes(id, renamedId);
		AssetCatalog.Refresh();

		return renamedId;
	}

	/// <summary>
	/// Renames a folder beneath Assets or Scenes.
	/// </summary>
	/// <remarks>
	/// The project roots cannot be renamed because the catalog and the
	/// project layout depend on them. Moving a folder updates its files,
	/// the startup scene path when it lives there, and every scene's
	/// asset references that share the old folder prefix. The folder's
	/// own leaf name is the only segment changed.
	/// </remarks>
	/// <returns>The normalized project-relative identity of the folder.</returns>
	public string RenameFolder(string relativePath, string newName)
	{
		string id = ProjectAssetCatalog.NormalizeId(relativePath).TrimEnd('/');
		string[] segments = id.Split('/');

		if (segments.Length < 2 ||
			(!string.Equals(segments[0], "Assets", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(segments[0], "Scenes", StringComparison.OrdinalIgnoreCase)))
		{
			throw new ArgumentException(
				"Only folders inside Assets or Scenes can be renamed.",
				nameof(relativePath));
		}

		foreach (string segment in segments)
		{
			ValidateAssetName(segment, nameof(relativePath));
		}

		string source = ResolvePath(id);

		if (!Directory.Exists(source))
		{
			throw new DirectoryNotFoundException(
				$"The folder '{relativePath}' does not exist.");
		}

		string trimmed = (newName ?? string.Empty).Trim();
		ValidateAssetName(trimmed, nameof(newName));

		string currentName = Path.GetFileName(source);

		if (string.Equals(trimmed, currentName, StringComparison.Ordinal))
		{
			return id;
		}

		string directory = Path.GetDirectoryName(source)!;
		string relativeDirectory = Path.GetRelativePath(RootPath, directory);
		string renamed = relativeDirectory == "."
			? trimmed
			: Path.Combine(relativeDirectory, trimmed);
		string destination = ResolvePath(renamed);

		if (File.Exists(destination) || Directory.Exists(destination))
		{
			throw new IOException(
				$"A file or folder already exists at '{renamed}'.");
		}

		string oldPrefix = id + "/";
		string renamedId = ProjectAssetCatalog.NormalizeId(renamed);
		string newPrefix = renamedId + "/";
		string startupScene = ProjectAssetCatalog.NormalizeId(StartupScenePath);
		bool startupInside = startupScene.StartsWith(
			oldPrefix,
			StringComparison.OrdinalIgnoreCase);

		Directory.Move(source, destination);

		if (startupInside)
		{
			StartupScenePath = newPrefix + startupScene[oldPrefix.Length..];
			SaveManifest();
		}

		ReplaceFolderReferencesInScenes(oldPrefix, newPrefix);
		AssetCatalog.Refresh();

		return renamedId;
	}

	/// <summary>
	/// Deletes several asset files from the project.
	/// </summary>
	/// <remarks>
	/// Every path is validated before the first file is removed, so a
	/// protected startup scene or a missing asset refuses the whole
	/// operation rather than leaving a partial deletion behind.
	/// </remarks>
	public void DeleteAssets(IEnumerable<string> relativePaths)
	{
		ArgumentNullException.ThrowIfNull(relativePaths);

		List<string> ids = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		string startupId = ProjectAssetCatalog.NormalizeId(StartupScenePath);

		foreach (string relativePath in relativePaths)
		{
			string id = ProjectAssetCatalog.NormalizeId(relativePath);

			if (!seen.Add(id))
			{
				continue;
			}

			if (string.Equals(startupId, id, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException(
					"The startup scene cannot be deleted. " +
					"Choose another startup scene first.");
			}

			string source = ResolvePath(relativePath);

			if (!File.Exists(source))
			{
				throw new FileNotFoundException(
					$"The asset '{relativePath}' does not exist.",
					source);
			}

			ids.Add(id);
		}

		foreach (string id in ids)
		{
			File.Delete(ResolvePath(id));
			ReplaceReferencesInScenes(id, string.Empty);
		}

		if (ids.Count > 0)
		{
			AssetCatalog.Refresh();
		}
	}

	/// <summary>
	/// Deletes an asset file from the project.
	/// </summary>
	/// <remarks>
	/// The startup scene is protected: deleting it would leave the
	/// manifest pointing at a missing scene. Choose another startup
	/// scene first. The editor separately protects the scene that is
	/// currently open.
	/// </remarks>
	public void DeleteAsset(string relativePath)
	{
		string id = ProjectAssetCatalog.NormalizeId(relativePath);

		if (string.Equals(
			ProjectAssetCatalog.NormalizeId(StartupScenePath),
			id,
			StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				"The startup scene cannot be deleted. " +
				"Choose another startup scene first.");
		}

		string source = ResolvePath(relativePath);

		if (!File.Exists(source))
		{
			throw new FileNotFoundException(
				$"The asset '{relativePath}' does not exist.",
				source);
		}

		File.Delete(source);
		ReplaceReferencesInScenes(id, string.Empty);
		AssetCatalog.Refresh();
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

	private void AttachGameplayPaths(
		string? projectRelativePath,
		string? assemblyRelativePath)
	{
		GameplayProjectPath = NormalizeOptionalPath(projectRelativePath);
		GameplayAssemblyPath = NormalizeOptionalPath(assemblyRelativePath);
	}

	private string? NormalizeOptionalPath(string? relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return null;
		}

		string id = ProjectAssetCatalog.NormalizeId(relativePath);

		// Resolve so a manifest that points outside the project is refused
		// while opening, not when a build is attempted.
		_ = ResolvePath(id);
		return id;
	}

	private int ReplaceReferencesInScenes(string oldId, string newId)
	{
		int changed = 0;

		foreach (string path in EnumerateSceneFiles())
		{
			changed += SceneAssetReferences.ReplaceInFile(
				path,
				oldId,
				newId);
		}

		return changed;
	}

	private int ReplaceFolderReferencesInScenes(
		string oldPrefix,
		string newPrefix)
	{
		int changed = 0;

		foreach (string path in EnumerateSceneFiles())
		{
			changed += SceneAssetReferences.ReplaceFolderInFile(
				path,
				oldPrefix,
				newPrefix);
		}

		return changed;
	}

	private IEnumerable<string> EnumerateSceneFiles()
	{
		foreach (string directory in new[] { AssetsPath, ScenesPath })
		{
			if (!Directory.Exists(directory))
			{
				continue;
			}

			foreach (string path in Directory.EnumerateFiles(
				directory,
				"*" + SceneSerializer.FileExtension,
				SearchOption.AllDirectories))
			{
				yield return path;
			}
		}
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

	/// <summary>
	/// The starter camera's identity, fixed so a new project and a
	/// documentation capture are reproducible.
	/// </summary>
	private static readonly Guid StarterCameraId =
		new("5f0e7a1c-3b2d-4c8e-9f6a-1b2c3d4e5f60");

	/// <summary>
	/// The starter cube's identity, fixed for the same reason.
	/// </summary>
	private static readonly Guid StarterCubeId =
		new("9a7b6c5d-4e3f-4a2b-8c1d-0e9f8a7b6c5d");

	private static SceneDocument CreateStarterScene(EditorProject project)
	{
		SceneDocument scene = new("Main");

		SceneEntity camera = scene.CreateEntity(
			"Main Camera",
			id: StarterCameraId);

		camera.Transform.Position = new System.Numerics.Vector3(0f, 1.5f, 6f);
		camera.AddComponent(new CameraData());

		SceneEntity cube = scene.CreateEntity(
			"Cube",
			id: StarterCubeId);
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

	private static (string Stem, string Extension) SplitAssetName(
		string fileName)
	{
		string extension = fileName.EndsWith(
			SceneSerializer.FileExtension,
			StringComparison.OrdinalIgnoreCase)
			? SceneSerializer.FileExtension
			: Path.GetExtension(fileName);

		return (fileName[..^extension.Length], extension);
	}

	private static void ValidateAssetName(
		string name,
		string parameterName)
	{
		if (name.Length == 0)
		{
			throw new ArgumentException(
				"An asset name is required.",
				parameterName);
		}

		if (name is "." or "..")
		{
			throw new ArgumentException(
				"The name must be more than a relative path segment.",
				parameterName);
		}

		foreach (char character in name)
		{
			if (character is '/' or '\\' or ':' or '*' or '?' or '"' or
				'<' or '>' or '|' ||
				char.IsControl(character))
			{
				throw new ArgumentException(
					"The name may not contain path separators or " +
					"reserved filename characters.",
					parameterName);
			}
		}

		if (name.EndsWith('.'))
		{
			throw new ArgumentException(
				"The name may not end with a period.",
				parameterName);
		}
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

		public GameplayManifestDto? Gameplay { get; set; }
	}

	/// <summary>
	/// The manifest's optional gameplay section.
	/// </summary>
	/// <remarks>
	/// Both paths are project-relative, like every other path in the
	/// manifest, so a project folder can move between machines and
	/// checkouts without carrying absolute paths.
	/// </remarks>
	internal sealed class GameplayManifestDto
	{
		public string? Project { get; set; }

		public string? Assembly { get; set; }
	}
}
