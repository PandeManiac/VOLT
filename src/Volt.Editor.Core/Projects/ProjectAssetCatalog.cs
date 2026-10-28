namespace Volt.Editor.Core.Projects;

public enum ProjectAssetKind
{
	Scene = 0,
	Mesh = 1,
	Material = 2,
	Texture = 3,
	Script = 4,
	Font = 5,
	Other = 6
}

/// <summary>
/// A stable project-relative asset reference.
/// </summary>
public sealed record ProjectAsset(
	string Id,
	string FullPath,
	string FileName,
	ProjectAssetKind Kind,
	long Length,
	DateTime LastWriteTimeUtc);

/// <summary>
/// Discovers files beneath a project's Assets and Scenes folders.
/// </summary>
/// <remarks>
/// Asset identities are normalized project-relative paths, never
/// absolute machine paths. Refreshing is explicit; painting the
/// browser does not enumerate the filesystem every frame.
/// </remarks>
public sealed class ProjectAssetCatalog
{
	private readonly Dictionary<string, ProjectAsset> _byId =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly List<ProjectAsset> _assets = [];

	private readonly List<string> _folders = [];

	public ProjectAssetCatalog(string projectRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
		ProjectRoot = Path.GetFullPath(projectRoot);
	}

	public string ProjectRoot { get; }

	public IReadOnlyList<ProjectAsset> Assets => _assets;

	/// <summary>
	/// Gets the project-relative folders beneath Assets and Scenes,
	/// including folders that contain no files.
	/// </summary>
	public IReadOnlyList<string> Folders => _folders;

	public void Refresh()
	{
		_byId.Clear();
		_assets.Clear();
		_folders.Clear();

		ScanDirectory("Assets");
		ScanDirectory("Scenes");

		_assets.Sort((left, right) =>
			string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
		_folders.Sort(StringComparer.OrdinalIgnoreCase);
	}

	public ProjectAsset? Find(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		return _byId.GetValueOrDefault(NormalizeId(id));
	}

	public bool TryResolvePath(string id, out string fullPath)
	{
		ProjectAsset? asset = Find(id);
		fullPath = asset?.FullPath ?? string.Empty;
		return asset is not null;
	}

	public bool FileExists(string id)
	{
		return Find(id) is not null;
	}

	private void ScanDirectory(string relativeDirectory)
	{
		string directory = Path.Combine(
			ProjectRoot,
			relativeDirectory.Replace('/', Path.DirectorySeparatorChar));

		if (!Directory.Exists(directory))
		{
			return;
		}

		_folders.Add(NormalizeId(relativeDirectory));

		string[] directories;

		try
		{
			directories = Directory.GetDirectories(
				directory,
				"*",
				SearchOption.AllDirectories);
		}

		catch (Exception exception)
			when (exception is IOException or UnauthorizedAccessException)
		{
			directories = [];
		}

		foreach (string child in directories)
		{
			string relative = Path.GetRelativePath(ProjectRoot, child);
			string id = NormalizeId(relative);

			if (id.StartsWith("../", StringComparison.Ordinal) ||
				Path.IsPathRooted(id))
			{
				continue;
			}

			_folders.Add(id);
		}

		IEnumerable<string> files;

		try
		{
			files = Directory.EnumerateFiles(
				directory,
				"*",
				SearchOption.AllDirectories);
		}

		catch (Exception exception)
			when (exception is IOException or UnauthorizedAccessException)
		{
			return;
		}

		foreach (string file in files)
		{
			string fullPath = Path.GetFullPath(file);
			string relative = Path.GetRelativePath(ProjectRoot, fullPath);
			string id = NormalizeId(relative);

			if (id.StartsWith("../", StringComparison.Ordinal) ||
				Path.IsPathRooted(id))
			{
				continue;
			}

			FileInfo info;

			try
			{
				info = new FileInfo(fullPath);
			}

			catch (Exception exception)
				when (exception is IOException or UnauthorizedAccessException)
			{
				continue;
			}

			ProjectAsset asset = new(
				id,
				fullPath,
				info.Name,
				Classify(id),
				info.Exists ? info.Length : 0L,
				info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue);

			if (_byId.TryAdd(id, asset))
			{
				_assets.Add(asset);
			}
		}
	}

	public static ProjectAssetKind Classify(string path)
	{
		ArgumentNullException.ThrowIfNull(path);

		if (path.EndsWith(
			".volt.scene.json",
			StringComparison.OrdinalIgnoreCase))
		{
			return ProjectAssetKind.Scene;
		}

		return Path.GetExtension(path).ToLowerInvariant() switch
		{
			".obj" or ".gltf" or ".glb" or ".mesh" =>
				ProjectAssetKind.Mesh,
			".mat" or ".material" => ProjectAssetKind.Material,
			".png" or ".jpg" or ".jpeg" or ".ktx2" or ".dds" =>
				ProjectAssetKind.Texture,
			".cs" => ProjectAssetKind.Script,
			".ttf" or ".otf" => ProjectAssetKind.Font,
			_ => ProjectAssetKind.Other
		};
	}

	public static string NormalizeId(string path)
	{
		return path
			.Replace('\\', '/')
			.TrimStart('/');
	}
}
