using System.Numerics;
using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Platform.Input;
using Volt.Platform.Windows;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Vulkan;
using Volt.Ui.Widgets;

namespace Volt.PlayDemo;

/// <summary>
/// Runs a VOLT game project without the editor.
/// </summary>
/// <remarks>
/// <para>
/// This is an ordinary VOLT application: it opens a project the same
/// way the editor does, loads its startup scene, starts a
/// <see cref="PlaySession"/>, and presents a <see cref="GameView"/>
/// built from Volt.Ui. No editor assembly is loaded and the editor
/// executable is not running.
/// </para>
/// <para>
/// The shipped sample project is created and edited by
/// <c>Volt.Editor</c>; this program only reads it. Its player object
/// uses the built-in move behaviour with authored axis, speed, and
/// input control, so game logic lives in scene data rather than in
/// this host.
/// </para>
/// </remarks>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		if (!TryParseArguments(
				args,
				out string? projectPath,
				out string? createPath,
				out bool validation,
				out int? frameLimit,
				out string? error))
		{
			Console.Error.WriteLine(error);

			return 2;
		}

		try
		{
			if (createPath is not null)
			{
				CreateProject(createPath);
				return 0;
			}

			return Run(
				projectPath ?? FindSampleProject(),
				validation,
				frameLimit);
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine();
			Console.Error.WriteLine("Volt.PlayDemo failed:");
			Console.Error.WriteLine(exception);

			return 1;
		}
	}

	private static int Run(
		string projectPath,
		bool validation,
		int? frameLimit)
	{
		EditorProject project = File.Exists(
			Path.Combine(projectPath, EditorProject.ManifestFileName))
			? EditorProject.Open(projectPath)
			: EditorProject.Create(projectPath, "Play Demo");

		SceneDocument scene = project.LoadStartupScene();

		using PlaySession play = new(scene);

		play.Start();

		using VulkanUiHost host = new(
			new VulkanUiHostOptions
			{
				Title = $"VOLT Game — {project.Name}",
				EnableValidation = validation,
				Theme = UiTheme.CreateDark(),
				Size = new WindowSize(1280, 800)
			});

		Console.WriteLine($"GPU: {host.DeviceName}");
		Console.WriteLine($"Validation: {host.ValidationLayersEnabled}");

		UiTheme theme = host.Ui.Theme;

		Label state = new("Playing")
		{
			Font = theme.Fonts.Heading,
			TextColor = theme.Palette.Text
		};

		Label statistics = new(string.Empty)
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted,
			VerticalAlignment = UiAlignment.Center
		};

		GameView view = new(play)
		{
			FlexGrow = 1f
		};

		StackPanel header = new(UiOrientation.Horizontal)
		{
			Spacing = 12f,
			Padding = new UiThickness(12f, 8f)
		};

		header.Add(state);
		header.Add(new Spacer());
		header.Add(statistics);

		Label help = new(
			"Move: A/D or Left/Right   •   Pause: Space   •   " +
			"Restart: R   •   Quit: Esc")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted,
			HorizontalAlignment = UiAlignment.Center
		};

		StackPanel root = new()
		{
			Spacing = 0f
		};

		root.Add(header);
		root.Add(view);
		root.Add(
			new Panel
			{
				Height = UiLength.Points(30f),
				Padding = new UiThickness(12f, 6f)
			}.Also(panel => panel.Add(help)));

		host.Ui.Add(root);

		int frames = 0;

		host.Run(
			window =>
			{
				InputSnapshot input = window.Window.Input.Snapshot;

				play.Input.MoveLeft =
					input.IsKeyDown(KeyCode.A) ||
					input.IsKeyDown(KeyCode.Left);

				play.Input.MoveRight =
					input.IsKeyDown(KeyCode.D) ||
					input.IsKeyDown(KeyCode.Right);

				play.Input.MoveForward =
					input.IsKeyDown(KeyCode.W) ||
					input.IsKeyDown(KeyCode.Up);

				play.Input.MoveBackward =
					input.IsKeyDown(KeyCode.S) ||
					input.IsKeyDown(KeyCode.Down);

				if (input.WasKeyPressed(KeyCode.Space))
				{
					if (play.State == PlayState.Playing)
					{
						play.Pause();
					}
					else if (play.State == PlayState.Paused)
					{
						play.Resume();
					}
				}

				if (input.WasKeyPressed(KeyCode.R))
				{
					play.Stop();
					play.Start();
				}

				if (input.WasKeyPressed(KeyCode.Escape))
				{
					window.RequestExit();
				}

				play.Update((float)window.DeltaSeconds);

				state.Text = play.State switch
				{
					PlayState.Playing => "Playing",
					PlayState.Paused => "Paused",
					_ => "Stopped"
				};

				RuntimeScene? runtime = play.Runtime;

				statistics.Text = runtime is null
					? "No runtime scene"
					: $"{runtime.Entities.Count} entities   •   " +
					  $"{runtime.TickCount} ticks   •   " +
					  $"{runtime.RenderableCount} renderable   •   " +
					  $"fixed {play.FixedDelta * 1000f:0.00} ms";

				frames++;

				if (frameLimit is int limit && frames >= limit)
				{
					window.RequestExit();
				}
			});

		play.Stop();

		Console.WriteLine(
			$"Presented {host.PresentedFrames} accepted frames.");

		if (frameLimit is int required &&
			host.PresentedFrames != required)
		{
			Console.Error.WriteLine(
				$"Presented {host.PresentedFrames} frames, but " +
				$"{required} were requested.");

			return 1;
		}

		return 0;
	}

	/// <summary>
	/// Creates the sample project used by this demo, including a
	/// player object driven by the built-in move behaviour.
	/// </summary>
	private static void CreateProject(string path)
	{
		EditorProject project = EditorProject.Create(path, "Play Demo");

		SceneDocument scene = project.LoadStartupScene();

		SceneEntity player = scene.CreateEntity("Player");

		player.Transform.Position = new Vector3(-3f, 0f, 0f);

		player.AddComponent(
			new MeshRendererData
			{
				Mesh = "builtin:cube",
				Material = "builtin:default"
			});

		player.AddComponent(
			new MoveBehaviourData
			{
				Axis = Vector3.UnitX,
				Speed = 3f,
				Range = 0f,
				Automatic = false
			});

		project.SaveScene(scene, project.StartupScenePath);

		Console.WriteLine(
			$"Created VOLT project '{project.Name}' at " +
			$"'{project.RootPath}'.");

		Console.WriteLine(
			$"Startup scene: {project.StartupScenePath}");
	}

	private static string FindSampleProject()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			string candidate = Path.Combine(
				directory.FullName,
				"samples",
				"Volt.PlayDemo",
				"SampleGame");

			if (File.Exists(
				Path.Combine(candidate, EditorProject.ManifestFileName)))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		return Path.Combine(
			Environment.CurrentDirectory,
			"SampleGame");
	}

	private static bool TryParseArguments(
		string[] args,
		out string? projectPath,
		out string? createPath,
		out bool validation,
		out int? frameLimit,
		out string? error)
	{
		projectPath = null;
		createPath = null;
		validation = false;
		frameLimit = null;
		error = null;

		for (int index = 0; index < args.Length; index++)
		{
			switch (args[index])
			{
				case "--project":
					if (index + 1 >= args.Length)
					{
						error = "--project requires a folder.";
						return false;
					}

					projectPath = Path.GetFullPath(args[++index]);
					break;

				case "--create":
					if (index + 1 >= args.Length)
					{
						error = "--create requires a folder.";
						return false;
					}

					createPath = Path.GetFullPath(args[++index]);
					break;

				case "--validation":
					validation = true;
					break;

				case "--frames":
					if (index + 1 >= args.Length ||
						!int.TryParse(args[++index], out int frames) ||
						frames <= 0)
					{
						error = "--frames requires a positive count.";
						return false;
					}

					frameLimit = frames;
					break;

				default:
					error = $"Unknown argument '{args[index]}'.";
					return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Applies one configuration action to a newly created widget.
	/// </summary>
	private static T Also<T>(this T widget, Action<T> configure)
		where T : Widget
	{
		configure(widget);
		return widget;
	}
}
