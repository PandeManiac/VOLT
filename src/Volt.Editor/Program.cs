using Volt.Editor.Core.Projects;
using Volt.Ui.Docking;
using Volt.Ui.Styling;
using Volt.Ui.Vulkan;

namespace Volt.Editor;

/// <summary>
/// Starts the VOLT editor window.
/// </summary>
/// <remarks>
/// The editor is an ordinary Volt application: it creates one SDL
/// window, hosts a <see cref="VulkanUiHost"/>, and adds a single
/// <see cref="EditorShell"/> built from Volt.Ui widgets. No other UI
/// toolkit is involved.
/// </remarks>
internal static class Program
{
	private const string ValidationArgument = "--validation";
	private const string ProjectArgument = "--project";
	private const string FramesArgument = "--frames";
	private const string WorkspaceFolderName = ".volt";
	private const string WorkspaceFileName = "workspace.json";

	[STAThread]
	private static int Main(string[] args)
	{
		if (!TryParseArguments(
				args,
				out bool validation,
				out string? projectPath,
				out int? frameLimit,
				out string? error))
		{
			Console.Error.WriteLine(error);
			Console.Error.WriteLine(
				"Usage: Volt.Editor [--validation] " +
				"[--project <folder>] [--frames <count>]");

			return 2;
		}

		try
		{
			return Run(validation, projectPath, frameLimit);
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine();
			Console.Error.WriteLine("VOLT Editor failed to start:");
			Console.Error.WriteLine(exception);

			return 1;
		}
	}

	private static int Run(
		bool validation,
		string? projectPath,
		int? frameLimit)
	{
		using EditorSession session = new();

		if (projectPath is not null)
		{
			try
			{
				session.OpenProject(projectPath);
			}

			catch (Exception exception)
			{
				Console.Error.WriteLine(
					$"Could not open project '{projectPath}': " +
					exception.Message);

				return 1;
			}
		}

		string? projectRoot = session.Project?.RootPath;
		DockLayout layout = LoadWorkspace(projectRoot);

		using VulkanUiHost host = new(
			new VulkanUiHostOptions
			{
				Title = "VOLT Editor",
				EnableValidation = validation,
				Theme = UiTheme.CreateDark(),
				Size = new Volt.Platform.Windows.WindowSize(1600, 960)
			});

		using EditorShell shell = new(session, projectRoot, layout);

		host.Ui.Add(shell);
		shell.ExitRequested += host.RequestExit;

		int frames = 0;

		host.Run(
			ui =>
			{
				session.Update(ui.DeltaSeconds);
				shell.Update(ui.DeltaSeconds);

				frames++;

				if (frameLimit is int limit && frames >= limit)
				{
					ui.RequestExit();
				}
			});

		SaveWorkspace(session.Project?.RootPath, shell.Layout);

		return 0;
	}

	private static DockLayout LoadWorkspace(string? projectRoot)
	{
		if (projectRoot is null)
		{
			return DockLayout.CreateEditorDefault();
		}

		string path = WorkspacePath(projectRoot);

		if (!File.Exists(path))
		{
			return DockLayout.CreateEditorDefault();
		}

		try
		{
			DockLayout saved = DockLayout.Deserialize(
				File.ReadAllText(path));

			DockLayout known = DockLayout.CreateEditorDefault();

			foreach (string panelId in known.Panels.Keys)
			{
				if (!saved.Panels.ContainsKey(panelId))
				{
					return known;
				}
			}

			return saved;
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Ignoring unreadable workspace layout: " +
				exception.Message);

			return DockLayout.CreateEditorDefault();
		}
	}

	private static void SaveWorkspace(
		string? projectRoot,
		DockLayout layout)
	{
		if (projectRoot is null)
		{
			return;
		}

		try
		{
			string path = WorkspacePath(projectRoot);
			string directory = Path.GetDirectoryName(path)
				?? projectRoot;

			Directory.CreateDirectory(directory);

			string temporary = path + ".tmp";

			File.WriteAllText(temporary, layout.Serialize());
			File.Move(temporary, path, overwrite: true);
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Could not save the workspace layout: " +
				exception.Message);
		}
	}

	private static string WorkspacePath(string projectRoot)
	{
		return Path.Combine(
			projectRoot,
			WorkspaceFolderName,
			WorkspaceFileName);
	}

	private static bool TryParseArguments(
		string[] args,
		out bool validation,
		out string? projectPath,
		out int? frameLimit,
		out string? error)
	{
		validation = false;
		projectPath = null;
		frameLimit = null;
		error = null;

		for (int index = 0; index < args.Length; index++)
		{
			switch (args[index])
			{
				case ValidationArgument:
					validation = true;
					break;

				case ProjectArgument:
					if (index + 1 >= args.Length)
					{
						error = "--project requires a folder.";
						return false;
					}

					projectPath = Path.GetFullPath(args[++index]);
					break;

				case FramesArgument:
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

		if (projectPath is not null &&
			!Directory.Exists(projectPath))
		{
			error = $"Project folder '{projectPath}' does not exist.";
			return false;
		}

		return true;
	}
}
