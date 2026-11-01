using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Validation;

/// <summary>
/// What a headless project run produced.
/// </summary>
public sealed class ProjectRunResult
{
	/// <summary>
	/// Gets whether the project ran to completion.
	/// </summary>
	public bool Succeeded { get; init; }

	/// <summary>
	/// Gets why the run could not start, or <see langword="null"/>.
	/// </summary>
	public string? Error { get; init; }

	/// <summary>
	/// Gets the number of fixed ticks that were simulated.
	/// </summary>
	public int Ticks { get; init; }

	/// <summary>
	/// Gets the number of entities the runtime world held.
	/// </summary>
	public int EntityCount { get; init; }

	/// <summary>
	/// Gets the name of the scene that ran.
	/// </summary>
	public string SceneName { get; init; } = string.Empty;

	/// <summary>
	/// Gets the loaded game-code assembly name, or <see langword="null"/>
	/// when the project has no built gameplay assembly.
	/// </summary>
	public string? AssemblyName { get; init; }

	/// <summary>
	/// Gets the game messages the run wrote.
	/// </summary>
	public IReadOnlyList<RuntimeLogEntry> Messages { get; init; } = [];

	/// <summary>
	/// Gets the runtime diagnostics the world reported at startup.
	/// </summary>
	public IReadOnlyList<string> Diagnostics { get; init; } = [];

	/// <summary>
	/// Gets the conditions that did not stop the run, such as missing
	/// built game code.
	/// </summary>
	public IReadOnlyList<string> Warnings { get; init; } = [];

	/// <summary>
	/// Gets the reference problems that stopped the run.
	/// </summary>
	public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>
/// Runs a project's startup scene without the editor.
/// </summary>
/// <remarks>
/// This is the same path Play Mode takes - project validation, then the
/// validated scene serialized into a separate runtime world, then built
/// game code loaded from the project's output folder - minus the window
/// and the panels. A fixed tick count makes the result reproducible, so
/// it can gate a build or run on a server, and it is how the engine can
/// be checked end to end without a GPU.
/// </remarks>
public static class ProjectRunner
{
	/// <summary>Gets the default number of ticks a run simulates.</summary>
	public const int DefaultTicks = 180;

	public static ProjectRunResult Run(string projectRoot, int ticks)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

		if (ticks <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ticks),
				ticks,
				"A run must simulate at least one tick.");
		}

		EditorProject project;

		try
		{
			project = EditorProject.Open(projectRoot);
		}

		catch (Exception exception)
			when (exception is IOException or
				InvalidDataException or
				UnauthorizedAccessException or
				ArgumentException)
		{
			return new ProjectRunResult
			{
				Error = $"The project could not be opened: {exception.Message}"
			};
		}

		ProjectValidationReport report = ProjectValidator.Validate(project);

		if (!report.IsValid)
		{
			return new ProjectRunResult
			{
				Error =
					$"The project has {report.Errors} broken " +
					"reference(s).",
				Problems = [.. report.Issues
					.Where(issue =>
						issue.Severity == ProjectIssueSeverity.Error)
					.Select(issue =>
						$"{issue.Location}: {issue.Message}")]
			};
		}

		SceneDocument scene;

		try
		{
			scene = project.LoadStartupScene();
		}

		catch (Exception exception)
			when (exception is IOException or
				InvalidDataException or
				UnauthorizedAccessException or
				ArgumentException)
		{
			return new ProjectRunResult
			{
				Error = $"The startup scene could not be loaded: " +
					exception.Message
			};
		}

		GameplayAssembly? gameplay = null;
		List<string> warnings = [];

		if (project.TryResolveGameplayAssembly(out string assemblyPath))
		{
			GameplayAssemblyLoadResult load = GameplayAssembly.Load(
				assemblyPath,
				ComponentCatalog.Default);

			if (!load.Succeeded)
			{
				return new ProjectRunResult
				{
					Error = $"The game code could not be loaded: " +
						load.Error,
					Problems = [.. load.Diagnostics],
					Warnings = warnings
				};
			}

			gameplay = load.Assembly;
		}

		else
		{
			warnings.Add(
				"The project has no built game code; the scene ran " +
				"without project systems.");
		}

		using PlaySession play = new(
			scene,
			assetExists: project.AssetCatalog.FileExists,
			componentBindings: gameplay?.Registration.Bindings,
			systems: gameplay?.Registration.Systems);

		RuntimeScene runtime;

		try
		{
			runtime = play.Start();
		}

		catch (Exception exception)
			when (exception is InvalidOperationException or
				InvalidDataException or
				ArgumentException)
		{
			return new ProjectRunResult
			{
				Error = $"The scene could not start: {exception.Message}"
			};
		}

		for (int tick = 0; tick < ticks; tick++)
		{
			runtime.Update(PlaySession.FixedDeltaSeconds);
		}

		// Stopping Play disposes the runtime world, so the observations
		// are taken while it is still alive.
		int entityCount = runtime.World.Count;
		RuntimeLogEntry[] messages = [.. runtime.Log.Entries];
		string[] diagnostics = [.. runtime.Diagnostics];

		play.Stop();

		return new ProjectRunResult
		{
			Succeeded = true,
			Ticks = ticks,
			EntityCount = entityCount,
			SceneName = scene.Name,
			AssemblyName = gameplay?.AssemblyName,
			Messages = messages,
			Diagnostics = diagnostics,
			Warnings = warnings
		};
	}
}
