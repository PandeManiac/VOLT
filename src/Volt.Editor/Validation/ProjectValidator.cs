using System.Text;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Rendering;

namespace Volt.Editor.Validation;

/// <summary>How serious a project problem is.</summary>
public enum ProjectIssueSeverity
{
	/// <summary>Something to fix that does not stop the project opening.</summary>
	Warning = 0,

	/// <summary>Something that renders or runs incorrectly.</summary>
	Error = 1
}

/// <summary>One problem found while validating a project.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Location">
/// Where to look: a scene and entity, a file, or the manifest.
/// </param>
/// <param name="Message">What is wrong, in the developer's terms.</param>
public readonly record struct ProjectIssue(
	ProjectIssueSeverity Severity,
	string Location,
	string Message);

/// <summary>The result of validating one project.</summary>
public sealed class ProjectValidationReport
{
	private readonly List<ProjectIssue> _issues = [];

	/// <summary>Gets the problems found, in discovery order.</summary>
	public IReadOnlyList<ProjectIssue> Issues => _issues;

	/// <summary>Gets how many checks ran.</summary>
	public int Checks { get; private set; }

	/// <summary>Gets how many errors were found.</summary>
	public int Errors => Count(ProjectIssueSeverity.Error);

	/// <summary>Gets how many warnings were found.</summary>
	public int Warnings => Count(ProjectIssueSeverity.Warning);

	/// <summary>
	/// Gets whether nothing prevents the project from opening and
	/// rendering correctly.
	/// </summary>
	public bool IsValid => Errors == 0;

	internal void Checked()
	{
		Checks++;
	}

	internal void Add(
		ProjectIssueSeverity severity,
		string location,
		string message)
	{
		_issues.Add(new ProjectIssue(severity, location, message));
	}

	private int Count(ProjectIssueSeverity severity)
	{
		int count = 0;

		foreach (ProjectIssue issue in _issues)
		{
			if (issue.Severity == severity)
			{
				count++;
			}
		}

		return count;
	}
}

/// <summary>
/// Checks that a project's files and references resolve.
/// </summary>
/// <remarks>
/// <para>
/// A scene stores identifiers, not files: a mesh, a material, a startup
/// scene, and an optional gameplay assembly. A renamed or deleted file
/// therefore leaves the scene opening and part of it silently missing,
/// which is exactly the failure a build machine should catch. This walks
/// the project the way the editor would and reports what would not draw or
/// run.
/// </para>
/// <para>
/// Everything is reported rather than thrown, so one broken scene does not
/// hide the rest.
/// </para>
/// </remarks>
public static class ProjectValidator
{
	/// <summary>Validates a project folder.</summary>
	/// <param name="rootPath">The folder holding the project manifest.</param>
	/// <returns>Everything found, with counts.</returns>
	public static ProjectValidationReport Validate(string rootPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

		ProjectValidationReport report = new();
		EditorProject project;

		report.Checked();

		try
		{
			project = EditorProject.Open(rootPath);
		}

		catch (Exception exception)
			when (exception is IOException or
				UnauthorizedAccessException or
				InvalidDataException or
				ArgumentException or
				DecoderFallbackException)
		{
			report.Add(
				ProjectIssueSeverity.Error,
				EditorProject.ManifestFileName,
				$"The project could not be opened: {exception.Message}");

			return report;
		}

		Validate(project, report);

		return report;
	}

	/// <summary>
	/// Validates a project that is already open.
	/// </summary>
	/// <param name="project">The open project.</param>
	/// <returns>Everything found, with counts.</returns>
	/// <remarks>
	/// Opening runs this, so re-reading the manifest from disk would only
	/// repeat work the caller has already done.
	/// </remarks>
	public static ProjectValidationReport Validate(EditorProject project)
	{
		ArgumentNullException.ThrowIfNull(project);

		ProjectValidationReport report = new();

		Validate(project, report);

		return report;
	}

	private static void Validate(
		EditorProject project,
		ProjectValidationReport report)
	{
		ValidateScenes(project, report);
		ValidateGameplay(project, report);
	}

	/// <summary>
	/// Validates the asset references of one scene.
	/// </summary>
	/// <param name="project">The project the scene belongs to.</param>
	/// <param name="location">
	/// How to name the scene in a report, usually its path.
	/// </param>
	/// <param name="scene">The loaded scene to check.</param>
	/// <returns>Everything found in that scene.</returns>
	/// <remarks>
	/// Play checks the scene it is about to run with this, so a project
	/// with one broken scene does not block playing a sound one.
	/// </remarks>
	public static ProjectValidationReport ValidateScene(
		EditorProject project,
		string location,
		SceneDocument scene)
	{
		ArgumentNullException.ThrowIfNull(project);
		ArgumentNullException.ThrowIfNull(scene);

		ProjectValidationReport report = new();

		ValidateReferences(project, location, scene, report);

		return report;
	}

	private static SceneDocument? LoadStartupScene(
		EditorProject project,
		ProjectValidationReport report)
	{
		report.Checked();

		try
		{
			return project.LoadStartupScene();
		}

		catch (Exception exception)
			when (exception is IOException or
				UnauthorizedAccessException or
				InvalidDataException or
				KeyNotFoundException or
				DecoderFallbackException)
		{
			report.Add(
				ProjectIssueSeverity.Error,
				project.StartupScenePath,
				$"The startup scene could not be loaded: {exception.Message}");

			return null;
		}
	}

	private static void ValidateScenes(
		EditorProject project,
		ProjectValidationReport report)
	{
		string startupPath = ProjectAssetCatalog.NormalizeId(
			project.StartupScenePath);
		HashSet<string> validated = new(StringComparer.OrdinalIgnoreCase);

		// The startup scene is the one Play uses, so its references are
		// the ones that must be sound; check it like any other scene.
		if (LoadStartupScene(project, report) is SceneDocument startup)
		{
			validated.Add(startupPath);
			ValidateReferences(project, startupPath, startup, report);
		}

		foreach (string scenePath in project.EnumerateScenePaths())
		{
			if (!validated.Add(scenePath))
			{
				continue;
			}

			report.Checked();

			SceneDocument scene;

			try
			{
				scene = project.LoadScene(scenePath);
			}

			catch (Exception exception)
				when (exception is IOException or
					UnauthorizedAccessException or
					InvalidDataException or
					KeyNotFoundException or
					DecoderFallbackException)
			{
				report.Add(
					ProjectIssueSeverity.Error,
					scenePath,
					$"The scene could not be loaded: {exception.Message}");
				continue;
			}

			ValidateReferences(project, scenePath, scene, report);
		}
	}

	private static void ValidateReferences(
		EditorProject project,
		string scenePath,
		SceneDocument scene,
		ProjectValidationReport report)
	{
		foreach (SceneEntity entity in scene.Entities)
		{
			MeshRendererData? renderer =
				entity.GetComponent<MeshRendererData>();

			if (renderer is null || !renderer.Visible)
			{
				continue;
			}

			report.Checked();

			string location = $"{scenePath}: {entity.Name}";

			if (string.IsNullOrWhiteSpace(renderer.Mesh))
			{
				report.Add(
					ProjectIssueSeverity.Warning,
					location,
					"The mesh renderer has no mesh assigned.");
				continue;
			}

			if (!ProjectMeshResolver.TryResolve(
				project,
				renderer.Mesh,
				renderer.Material,
				out _,
				out _))
			{
				report.Add(
					ProjectIssueSeverity.Error,
					location,
					$"The mesh '{renderer.Mesh}' cannot be drawn.");
			}

			if (string.IsNullOrWhiteSpace(renderer.Material) ||
				string.Equals(
					renderer.Material,
					"builtin:default",
					StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			report.Checked();

			if (!project.TryLoadMaterial(
				renderer.Material,
				out _,
				out string? diagnostic))
			{
				report.Add(
					ProjectIssueSeverity.Warning,
					location,
					$"The material could not be loaded: {diagnostic}");
			}
		}
	}

	private static void ValidateGameplay(
		EditorProject project,
		ProjectValidationReport report)
	{
		if (string.IsNullOrWhiteSpace(project.GameplayProjectPath))
		{
			return;
		}

		report.Checked();

		// The recorded path is checked directly. Resolving it falls back
		// to whatever script project exists, which would hide a manifest
		// that names a file the project no longer has.
		string recordedPath = project.ResolvePath(
			project.GameplayProjectPath);

		if (!File.Exists(recordedPath))
		{
			report.Add(
				ProjectIssueSeverity.Error,
				project.GameplayProjectPath,
				"The game code project is recorded but its file is missing.");
			return;
		}

		report.Checked();

		if (!project.TryResolveGameplayAssembly(out _))
		{
			report.Add(
				ProjectIssueSeverity.Warning,
				project.GameplayProjectPath,
				"The game code has not been built yet; build it before " +
				"pressing Play.");
		}
	}
}
