using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// One compiler or MSBuild message from a game-code build.
/// </summary>
/// <remarks>
/// The location is kept separate from the message so the Console can show
/// the file, line, and column a developer needs to fix, instead of a wall
/// of MSBuild output.
/// </remarks>
public sealed record GameplayBuildDiagnostic(
	string File,
	int Line,
	int Column,
	bool IsError,
	string Code,
	string Message)
{
	/// <summary>
	/// Gets the conventional <c>file(line,column): error CS0000: text</c>
	/// form, with the location omitted when the build reported none.
	/// </summary>
	public string Describe()
	{
		string severity = IsError ? "error" : "warning";

		if (string.IsNullOrEmpty(File))
		{
			return string.IsNullOrEmpty(Code)
				? $"{severity}: {Message}"
				: $"{severity} {Code}: {Message}";
		}

		return string.IsNullOrEmpty(Code)
			? $"{File}({Line},{Column}): {severity}: {Message}"
			: $"{File}({Line},{Column}): {severity} {Code}: {Message}";
	}
}

/// <summary>
/// The outcome of building a project's game code.
/// </summary>
public sealed record GameplayBuildResult(
	bool Succeeded,
	int ExitCode,
	IReadOnlyList<GameplayBuildDiagnostic> Diagnostics,
	string? AssemblyPath,
	IReadOnlyList<string> Output,
	string? FailureMessage)
{
	/// <summary>
	/// Gets the diagnostics a developer should act on first: errors when
	/// the build failed, warnings otherwise.
	/// </summary>
	public IReadOnlyList<GameplayBuildDiagnostic> RelevantDiagnostics =>
		[
			.. Diagnostics.Where(diagnostic =>
				diagnostic.IsError != Succeeded)
		];
}

/// <summary>
/// Compiles a project's game code with the .NET SDK.
/// </summary>
/// <remarks>
/// <para>
/// Game code is compiled by the real SDK rather than by an in-process
/// compiler, so a developer's own <c>dotnet build</c> and the engine's
/// build are the same build: same references, same diagnostics, same
/// output. That is also why the generated project references the engine
/// by path — the engine is the reference.
/// </para>
/// <para>
/// A build is an explicit workflow. It runs when a developer asks for it,
/// never because a folder was opened or an asset was listed.
/// </para>
/// </remarks>
public static class GameplayProjectBuilder
{
	/// <summary>
	/// The time a build may take before it is abandoned.
	/// </summary>
	/// <remarks>
	/// A first build restores and compiles the referenced engine projects,
	/// so the limit is generous; the point is that a wedged build cannot
	/// hang the editor forever.
	/// </remarks>
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(4);

	private static readonly Regex s_diagnosticPattern = new(
		@"^\s*(?<file>[^\(]+?)\((?<line>\d+),(?<column>\d+)\):\s+" +
		@"(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)?:?\s*" +
		@"(?<message>.*?)(?:\s+\[[^\]]+\])?\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex s_messagePattern = new(
		@"^\s*(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)?:?\s*" +
		@"(?<message>.*?)(?:\s+\[[^\]]+\])?\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly Regex s_outputPattern = new(
		@"^\s*\S.*\s->\s(?<path>.+\.dll)\s*$",
		RegexOptions.Compiled | RegexOptions.CultureInvariant);

	/// <summary>
	/// Builds one game project file.
	/// </summary>
	/// <param name="projectFile">The project file to build.</param>
	/// <param name="buildReferencedProjects">
	/// Whether referenced projects are built too. A host that is already
	/// running the engine can skip them; a standalone build must not.
	/// </param>
	/// <param name="timeout">Overrides <see cref="DefaultTimeout"/>.</param>
	/// <returns>
	/// The result, including parsed diagnostics. A missing project file,
	/// a missing SDK, and a timeout are all reported rather than thrown.
	/// </returns>
	public static GameplayBuildResult Build(
		string projectFile,
		bool buildReferencedProjects = true,
		TimeSpan? timeout = null)
	{
		if (string.IsNullOrWhiteSpace(projectFile))
		{
			return Failure("No game project file was given.");
		}

		string fullPath = Path.GetFullPath(projectFile);

		if (!File.Exists(fullPath))
		{
			return Failure($"The game project '{fullPath}' does not exist.");
		}

		string? dotnet = ResolveDotnetHost();

		if (dotnet is null)
		{
			return Failure(
				"The .NET SDK was not found, so game code cannot be " +
				"built from here. Build it with 'dotnet build' and reload.");
		}

		StringBuilder arguments = new();
		arguments.Append("build \"");
		arguments.Append(fullPath);
		arguments.Append('"');
		arguments.Append(" -c Release --nologo -v:minimal -nodeReuse:false");

		if (!buildReferencedProjects)
		{
			arguments.Append(" -p:BuildProjectReferences=false");
		}

		ProcessStartInfo startInfo = new()
		{
			FileName = dotnet,
			Arguments = arguments.ToString(),
			WorkingDirectory = Path.GetDirectoryName(fullPath)!,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		// A build started by a test run or by a parent MSBuild process must
		// not share its build server, and must not inherit stray locale
		// settings that would change diagnostic wording.
		startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
		startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
		startInfo.Environment["DOTNET_NOLOGO"] = "1";
		startInfo.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
		startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

		TimeSpan limit = timeout ?? DefaultTimeout;
		List<string> output = [];

		using Process process = new() { StartInfo = startInfo };

		try
		{
			if (!process.Start())
			{
				return Failure("The .NET SDK build could not be started.");
			}
		}

		catch (Exception exception)
		{
			return Failure(
				$"The .NET SDK build could not be started: " +
				$"{exception.Message}");
		}

		// Closing stdin stops a prompting build from waiting forever on
		// a question nobody can answer.
		process.StandardInput.Close();

		Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
		Task<string> standardError = process.StandardError.ReadToEndAsync();

		if (!process.WaitForExit((int)limit.TotalMilliseconds))
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}

			catch (Exception exception)
				when (exception is InvalidOperationException or
					NotSupportedException or
					Win32Exception)
			{
				// The process already exited between the wait and the kill.
			}

			return Failure(
				$"The game build did not finish within " +
				$"{limit.TotalSeconds:0} seconds and was stopped.");
		}

		output.AddRange(SplitLines(standardOutput.GetAwaiter().GetResult()));
		output.AddRange(SplitLines(standardError.GetAwaiter().GetResult()));

		IReadOnlyList<GameplayBuildDiagnostic> diagnostics =
			ParseDiagnostics(output);

		string? assembly = FindBuiltAssembly(output, fullPath);

		bool succeeded = process.ExitCode == 0;

		return new GameplayBuildResult(
			succeeded && assembly is not null,
			process.ExitCode,
			diagnostics,
			assembly,
			output,
			succeeded && assembly is null
				? "The build succeeded but did not produce a game assembly."
				: null);
	}

	/// <summary>
	/// Parses compiler and MSBuild messages out of build output.
	/// </summary>
	/// <remarks>
	/// Both the located form and the unlocated form are recognised,
	/// because MSBuild reports some failures without a file.
	/// </remarks>
	public static IReadOnlyList<GameplayBuildDiagnostic> ParseDiagnostics(
		IEnumerable<string> lines)
	{
		ArgumentNullException.ThrowIfNull(lines);

		List<GameplayBuildDiagnostic> diagnostics = [];
		HashSet<string> seen = new(StringComparer.Ordinal);

		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			Match located = s_diagnosticPattern.Match(line);

			if (located.Success)
			{
				// A build reports the same compiler error once per target
				// that compiled the file, so an unfiltered Console would
				// show every error twice.
				string key = string.Join(
					'|',
					located.Groups["file"].Value.Trim(),
					located.Groups["line"].Value,
					located.Groups["column"].Value,
					located.Groups["severity"].Value,
					located.Groups["code"].Value,
					located.Groups["message"].Value.Trim());

				if (!seen.Add(key))
				{
					continue;
				}

				diagnostics.Add(
					new GameplayBuildDiagnostic(
						located.Groups["file"].Value.Trim(),
						int.TryParse(
							located.Groups["line"].Value,
							out int number) ? number : 0,
						int.TryParse(
							located.Groups["column"].Value,
							out int column) ? column : 0,
						string.Equals(
							located.Groups["severity"].Value,
							"error",
							StringComparison.OrdinalIgnoreCase),
						located.Groups["code"].Value,
						located.Groups["message"].Value.Trim()));

				continue;
			}

			Match unlocated = s_messagePattern.Match(line);

			if (unlocated.Success &&
				!string.IsNullOrWhiteSpace(
					unlocated.Groups["message"].Value) &&
				seen.Add(string.Join(
					'|',
					string.Empty,
					"0",
					"0",
					unlocated.Groups["severity"].Value,
					unlocated.Groups["code"].Value,
					unlocated.Groups["message"].Value.Trim())))
			{
				diagnostics.Add(
					new GameplayBuildDiagnostic(
						string.Empty,
						0,
						0,
						string.Equals(
							unlocated.Groups["severity"].Value,
							"error",
							StringComparison.OrdinalIgnoreCase),
						unlocated.Groups["code"].Value,
						unlocated.Groups["message"].Value.Trim()));
			}
		}

		return diagnostics;
	}

	private static string? FindBuiltAssembly(
		IReadOnlyList<string> output,
		string projectFile)
	{
		string? reported = null;

		foreach (string line in output)
		{
			Match match = s_outputPattern.Match(line);

			if (match.Success &&
				match.Groups["path"].Value.EndsWith(
					GameplayProjectScaffold.AssemblyName + ".dll",
					StringComparison.Ordinal))
			{
				reported = match.Groups["path"].Value.Trim();
			}
		}

		if (reported is not null && File.Exists(reported))
		{
			return Path.GetFullPath(reported);
		}

		// The build may have been up to date, in which case it prints no
		// "->" line. Fall back to the conventional output next to it.
		string directory = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
		string conventional = Path.Combine(
			directory,
			"bin",
			"Release",
			"net10.0",
			GameplayProjectScaffold.AssemblyName + ".dll");

		if (File.Exists(conventional))
		{
			return conventional;
		}

		return reported;
	}

	private static string? ResolveDotnetHost()
	{
		// A host started by the SDK exports the path to the same runtime
		// that is already running, which is the one the game must build
		// against.
		string? hosted = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

		if (!string.IsNullOrWhiteSpace(hosted) && File.Exists(hosted))
		{
			return hosted;
		}

		string executable = OperatingSystem.IsWindows()
			? "dotnet.exe"
			: "dotnet";

		string? path = Environment.GetEnvironmentVariable("PATH");

		if (path is not null)
		{
			foreach (string directory in path.Split(
				Path.PathSeparator,
				StringSplitOptions.RemoveEmptyEntries))
			{
				try
				{
					string candidate = Path.Combine(directory, executable);

					if (File.Exists(candidate))
					{
						return candidate;
					}
				}

				catch (ArgumentException)
				{
					// A malformed PATH entry is not a reason to give up.
				}
			}
		}

		// The runtime that is executing this process can always open the
		// SDK's own muxer by name.
		return executable;
	}

	private static IEnumerable<string> SplitLines(string text)
	{
		return text.Split(
			['\r', '\n'],
			StringSplitOptions.RemoveEmptyEntries);
	}

	private static GameplayBuildResult Failure(string message)
	{
		return new GameplayBuildResult(
			false,
			-1,
			[],
			null,
			[],
			message);
	}
}
