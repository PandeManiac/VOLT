using System.Text;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// The result of writing a project's game-code scaffold.
/// </summary>
public sealed record GameplayScaffoldResult(
	bool Created,
	string ProjectPath,
	string AssemblyPath,
	IReadOnlyList<string> Files,
	string? SkipReason);

/// <summary>
/// Creates the C# project a VOLT project owns for its gameplay code.
/// </summary>
/// <remarks>
/// <para>
/// A project's game code lives in <c>Scripts/</c>, outside <c>Assets</c>,
/// because a build produces <c>bin</c> and <c>obj</c> folders that must
/// not appear in the asset browser. The generated project is an ordinary
/// SDK project: a developer can open the folder in VS Code, build it with
/// the .NET SDK, and get real compiler diagnostics, without touching the
/// engine's own source.
/// </para>
/// <para>
/// The engine is referenced by project path because VOLT is a
/// source-distributed engine with no published package. The generated file
/// records the engine checkout it was created from and accepts a
/// <c>VoltRoot</c> property or a <c>VOLT_ROOT</c> environment variable, so
/// moving the project to another machine is one setting rather than an
/// edit to every reference.
/// </para>
/// </remarks>
public static class GameplayProjectScaffold
{
	public const string ScriptsFolder = "Scripts";

	public const string ProjectFileName = "GameScripts.csproj";

	public const string ModuleFileName = "Game.cs";

	public const string AssemblyName = "GameScripts";

	/// <summary>
	/// Finds the engine checkout this process is running from.
	/// </summary>
	/// <remarks>
	/// The <c>VOLT_ROOT</c> environment variable wins, then a parent
	/// directory of the running assembly that holds <c>Volt.slnx</c>.
	/// </remarks>
	public static bool TryFindEngineRoot(out string engineRoot)
	{
		string? configured = Environment.GetEnvironmentVariable("VOLT_ROOT");

		if (!string.IsNullOrWhiteSpace(configured) &&
			File.Exists(Path.Combine(configured, "Volt.slnx")))
		{
			engineRoot = Path.GetFullPath(configured);
			return true;
		}

		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "Volt.slnx")))
			{
				engineRoot = directory.FullName;
				return true;
			}

			directory = directory.Parent;
		}

		engineRoot = string.Empty;
		return false;
	}

	/// <summary>
	/// Gets the expected build output for a scaffolded project.
	/// </summary>
	public static string GetAssemblyPath(string projectRoot)
	{
		return Path.Combine(
			projectRoot,
			ScriptsFolder,
			"bin",
			"Release",
			"net10.0",
			AssemblyName + ".dll");
	}

	/// <summary>
	/// Gets the project file a scaffolded project writes, if it exists.
	/// </summary>
	public static string? FindScriptProject(string projectRoot)
	{
		string path = Path.Combine(
			projectRoot,
			ScriptsFolder,
			ProjectFileName);

		return File.Exists(path) ? path : null;
	}

	/// <summary>
	/// Finds the newest built gameplay assembly for a project.
	/// </summary>
	/// <remarks>
	/// A developer's own <c>dotnet build</c> may have used Debug, and a
	/// host build may have used Release, so both conventional outputs are
	/// probed and the newest is returned. Versioned TFM folders are also
	/// searched, so a project that targets a newer runtime still resolves.
	/// </remarks>
	public static string? FindBuiltAssembly(string projectRoot)
	{
		string bin = Path.Combine(projectRoot, ScriptsFolder, "bin");

		if (!Directory.Exists(bin))
		{
			return null;
		}

		string? newest = null;
		DateTime newestWrite = DateTime.MinValue;

		foreach (string candidate in Directory.EnumerateFiles(
			bin,
			AssemblyName + ".dll",
			SearchOption.AllDirectories))
		{
			DateTime write = File.GetLastWriteTimeUtc(candidate);

			if (write > newestWrite)
			{
				newestWrite = write;
				newest = candidate;
			}
		}

		return newest;
	}

	/// <summary>
	/// Writes the game-code scaffold for one project.
	/// </summary>
	/// <param name="projectRoot">The VOLT project root.</param>
	/// <param name="projectName">
	/// The display name, used for the starter component's stable
	/// identifier.
	/// </param>
	/// <param name="engineRoot">
	/// The engine checkout to reference. Discovered when omitted.
	/// </param>
	/// <returns>
	/// The paths written and, when nothing was written, why. An existing
	/// project file is never overwritten: a developer's code outranks a
	/// template.
	/// </returns>
	public static GameplayScaffoldResult Create(
		string projectRoot,
		string projectName,
		string? engineRoot = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
		ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

		string fullRoot = Path.GetFullPath(projectRoot);
		string scripts = Path.Combine(fullRoot, ScriptsFolder);
		string projectPath = Path.Combine(scripts, ProjectFileName);
		string modulePath = Path.Combine(scripts, ModuleFileName);
		string assemblyPath = GetAssemblyPath(fullRoot);

		if (File.Exists(projectPath))
		{
			return new GameplayScaffoldResult(
				false,
				projectPath,
				assemblyPath,
				[],
				$"'{Path.GetRelativePath(fullRoot, projectPath)}' already " +
				"exists, so it was left unchanged.");
		}

		string? resolvedEngine = engineRoot;

		if (string.IsNullOrWhiteSpace(resolvedEngine) &&
			!TryFindEngineRoot(out resolvedEngine))
		{
			return new GameplayScaffoldResult(
				false,
				projectPath,
				assemblyPath,
				[],
				"No engine checkout was found to reference, so game code " +
				"was not scaffolded. Set VOLT_ROOT or pass the engine root.");
		}

		if (!File.Exists(Path.Combine(
			resolvedEngine,
			"src",
			"Volt.Editor.Core",
			"Volt.Editor.Core.csproj")))
		{
			return new GameplayScaffoldResult(
				false,
				projectPath,
				assemblyPath,
				[],
				$"The engine checkout at '{resolvedEngine}' has no " +
				"Volt.Editor.Core project.");
		}

		Directory.CreateDirectory(scripts);

		File.WriteAllText(
			projectPath,
			CreateProjectFile(resolvedEngine));

		File.WriteAllText(
			modulePath,
			CreateModuleSource(projectName));

		return new GameplayScaffoldResult(
			true,
			projectPath,
			assemblyPath,
			[projectPath, modulePath],
			null);
	}

	/// <summary>
	/// Gets the game project file text for a given engine checkout.
	/// </summary>
	public static string CreateProjectFile(string engineRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);

		string normalized = Path.GetFullPath(engineRoot)
			.Replace('\\', '/')
			.TrimEnd('/');

		StringBuilder builder = new();

		builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
		builder.AppendLine();
		builder.AppendLine("  <PropertyGroup>");
		builder.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
		builder.AppendLine("    <LangVersion>latest</LangVersion>");
		builder.AppendLine("    <Nullable>enable</Nullable>");
		builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
		builder.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
		builder.AppendLine(
			$"    <AssemblyName>{AssemblyName}</AssemblyName>");
		builder.AppendLine("    <RootNamespace>GameScripts</RootNamespace>");
		builder.AppendLine("  </PropertyGroup>");
		builder.AppendLine();
		builder.AppendLine("  <!-- The engine checkout this project builds against.");
		builder.AppendLine("       Override VoltRoot or set VOLT_ROOT on another machine. -->");
		builder.AppendLine("  <PropertyGroup>");
		builder.AppendLine(
			"    <VoltRoot Condition=\"'$(VoltRoot)' == ''\">$(VOLT_ROOT)</VoltRoot>");
		builder.AppendLine(
			$"    <VoltRoot Condition=\"'$(VoltRoot)' == ''\">{normalized}</VoltRoot>");
		builder.AppendLine("  </PropertyGroup>");
		builder.AppendLine();
		builder.AppendLine("  <ItemGroup>");
		builder.AppendLine(
			"    <ProjectReference Include=\"$(VoltRoot)/src/Volt.Editor.Core/Volt.Editor.Core.csproj\" />");
		builder.AppendLine("  </ItemGroup>");
		builder.AppendLine();
		builder.AppendLine("</Project>");

		return builder.ToString();
	}

	/// <summary>
	/// Gets the starter gameplay module text.
	/// </summary>
	/// <remarks>
	/// The starter code is a complete, working example rather than a
	/// placeholder: a component the inspector can add and edit, the
	/// unmanaged state a system iterates, the system that moves the entity
	/// from game input, and the module that registers all three. Every API
	/// it uses is public engine API.
	/// </remarks>
	public static string CreateModuleSource(string projectName)
	{
		string typeId = $"{Slugify(projectName)}.playerMotor";

		StringBuilder builder = new();

		builder.AppendLine("using System.Numerics;");
		builder.AppendLine("using Volt.Ecs;");
		builder.AppendLine("using Volt.Ecs.Component;");
		builder.AppendLine("using Volt.Ecs.Components.Spatial;");
		builder.AppendLine("using Volt.Editor.Core.Gameplay;");
		builder.AppendLine("using Volt.Editor.Core.Runtime;");
		builder.AppendLine("using Volt.Editor.Core.Scenes;");
		builder.AppendLine();
		builder.AppendLine("namespace GameScripts;");
		builder.AppendLine();
		builder.AppendLine("/// <summary>");
		builder.AppendLine("/// Authored data: what the inspector shows and the scene saves.");
		builder.AppendLine("/// </summary>");
		builder.AppendLine("[EditorComponent(PlayerMotorComponent.ComponentTypeId, \"Player Motor\")]");
		builder.AppendLine("public sealed class PlayerMotorComponent : AuthoredComponent");
		builder.AppendLine("{");
		builder.AppendLine($"\tpublic const string ComponentTypeId = \"{typeId}\";");
		builder.AppendLine();
		builder.AppendLine("\tpublic override string TypeId => ComponentTypeId;");
		builder.AppendLine();
		builder.AppendLine("\tpublic override string DisplayName => \"Player Motor\";");
		builder.AppendLine();
		builder.AppendLine("\t[EditorProperty(Minimum = 0f, Maximum = 20f)]");
		builder.AppendLine("\tpublic float Speed { get; set; } = 3f;");
		builder.AppendLine();
		builder.AppendLine("\tpublic Vector3 Axis { get; set; } = Vector3.UnitX;");
		builder.AppendLine("}");
		builder.AppendLine();
		builder.AppendLine("/// <summary>");
		builder.AppendLine("/// Native state: what a system iterates in contiguous storage.");
		builder.AppendLine("/// </summary>");
		builder.AppendLine("public struct PlayerMotorState");
		builder.AppendLine("{");
		builder.AppendLine("\tpublic Vector3 Axis;");
		builder.AppendLine();
		builder.AppendLine("\tpublic float Speed;");
		builder.AppendLine("}");
		builder.AppendLine();
		builder.AppendLine("/// <summary>");
		builder.AppendLine("/// Moves every entity with a motor from game input each fixed step.");
		builder.AppendLine("/// </summary>");
		builder.AppendLine("public sealed class PlayerMotorSystem : IGameplaySystem");
		builder.AppendLine("{");
		builder.AppendLine("\tpublic void Update(RuntimeScene scene, float deltaSeconds)");
		builder.AppendLine("\t{");
		builder.AppendLine("\t\tComponentQuery query = new(");
		builder.AppendLine("\t\t\trequired: new ComponentSignature(");
		builder.AppendLine("\t\t\t\tComponentRegistry.Register<PlayerMotorState>()));");
		builder.AppendLine();
		builder.AppendLine("\t\tfloat input = scene.Input.Horizontal;");
		builder.AppendLine();
		builder.AppendLine("\t\tforeach (WorldQueryChunk chunk in scene.World.Query(query))");
		builder.AppendLine("\t\t{");
		builder.AppendLine("\t\t\tSpan<PlayerMotorState> states =");
		builder.AppendLine("\t\t\t\tchunk.GetSpan<PlayerMotorState>();");
		builder.AppendLine();
		builder.AppendLine("\t\t\tReadOnlySpan<Entity> entities = chunk.GetEntities();");
		builder.AppendLine();
		builder.AppendLine("\t\t\tfor (int index = 0; index < states.Length; index++)");
		builder.AppendLine("\t\t\t{");
		builder.AppendLine("\t\t\t\tPlayerMotorState state = states[index];");
		builder.AppendLine();
		builder.AppendLine("\t\t\t\tref PositionComponent position =");
		builder.AppendLine("\t\t\t\t\tref scene.World.GetComponent<PositionComponent>(");
		builder.AppendLine("\t\t\t\t\t\tentities[index]);");
		builder.AppendLine();
		builder.AppendLine("\t\t\t\tposition.Value +=");
		builder.AppendLine("\t\t\t\t\tstate.Axis * (input * state.Speed * deltaSeconds);");
		builder.AppendLine("\t\t\t}");
		builder.AppendLine("\t\t}");
		builder.AppendLine("\t}");
		builder.AppendLine("}");
		builder.AppendLine();
		builder.AppendLine("/// <summary>");
		builder.AppendLine("/// The entry point VOLT discovers when it loads this assembly.");
		builder.AppendLine("/// </summary>");
		builder.AppendLine("public sealed class GameModule : IGameplayModule");
		builder.AppendLine("{");
		builder.AppendLine("\tpublic void Register(GameplayRegistrar registrar)");
		builder.AppendLine("\t{");
		builder.AppendLine("\t\tregistrar.AddComponent<PlayerMotorComponent>();");
		builder.AppendLine();
		builder.AppendLine(
			"\t\tregistrar.BindComponent<PlayerMotorComponent, PlayerMotorState>(");
		builder.AppendLine(
			"\t\t\tcomponent => new PlayerMotorState");
		builder.AppendLine("\t\t\t{");
		builder.AppendLine("\t\t\t\tAxis = component.Axis,");
		builder.AppendLine("\t\t\t\tSpeed = component.Speed");
		builder.AppendLine("\t\t\t});");
		builder.AppendLine();
		builder.AppendLine();
		builder.AppendLine("\t\t// Lets Play Mode show this component's live values in the ECS");
		builder.AppendLine("\t\t// panel: native storage has no field names of its own.");
		builder.AppendLine(
			"\t\tregistrar.DescribeComponent<PlayerMotorState>(");
		builder.AppendLine("\t\t\t\"Player Motor\",");
		builder.AppendLine("\t\t\tstate =>");
		builder.AppendLine("\t\t\t[");
		builder.AppendLine(
			"\t\t\t\tRuntimeFieldSnapshot.Number(\"Speed\", state.Speed),");
		builder.AppendLine(
			"\t\t\t\tRuntimeFieldSnapshot.Number(\"Axis X\", state.Axis.X)");
		builder.AppendLine("\t\t\t]);");
		builder.AppendLine();
		builder.AppendLine("\t\tregistrar.AddSystem<PlayerMotorSystem>();");
		builder.AppendLine("\t}");
		builder.AppendLine("}");

		return builder.ToString();
	}

	private static string Slugify(string projectName)
	{
		StringBuilder builder = new(projectName.Length);

		foreach (char character in projectName)
		{
			if (char.IsLetterOrDigit(character))
			{
				builder.Append(char.ToLowerInvariant(character));
			}
		}

		return builder.Length == 0 ? "game" : builder.ToString();
	}
}
