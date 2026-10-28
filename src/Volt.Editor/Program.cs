using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Input;
using Volt.Editor.Rendering;
using Volt.Ui.Docking;
using Volt.Ui.Rendering;
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
	private const string WidthArgument = "--width";
	private const string HeightArgument = "--height";
	private const string UiScaleArgument = "--ui-scale";
	private const string SnapshotArgument = "--snapshot";
	private const string SnapshotStateArgument = "--snapshot-state";
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
				out int? width,
				out int? height,
				out float? uiScale,
				out string? snapshotPath,
				out string? snapshotState,
				out string? error))
		{
			Console.Error.WriteLine(error);
			Console.Error.WriteLine(
				"Usage: Volt.Editor [--validation] " +
				"[--project <folder>] [--frames <count>] " +
				"[--width <pixels>] [--height <pixels>] " +
				"[--ui-scale <factor>] [--snapshot <file>] " +
				"[--snapshot-state <workspace|authoring|start>]");

			return 2;
		}

		try
		{
			return Run(
				validation,
				projectPath,
				frameLimit,
				width,
				height,
				uiScale,
				snapshotPath,
				snapshotState);
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
		int? frameLimit,
		int? width,
		int? height,
		float? uiScale,
		string? snapshotPath,
		string? snapshotState)
	{
		if (snapshotPath is not null)
		{
			return CaptureSnapshot(
				projectPath,
				width,
				height,
				uiScale,
				snapshotState,
				snapshotPath);
		}

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
		EditorSettings settings = EditorPreferences.Load(projectRoot);
		DockLayout layout = LoadWorkspace(projectRoot);

		session.TargetFrameRate = settings.TargetFrameRate;

		Volt.Platform.Windows.WindowSize initialWindowSize = new(
			width ?? settings.WindowWidth,
			height ?? settings.WindowHeight);

		using VulkanUiHost host = new(
			new VulkanUiHostOptions
			{
				Title = "VOLT Editor",
				EnableValidation = validation,
				Theme = UiTheme.CreateDark(),
				Size = initialWindowSize
			});

		Console.WriteLine($"GPU: {host.DeviceName}");
		Console.WriteLine($"Validation: {host.ValidationLayersEnabled}");

		using EditorSceneRenderer? sceneRenderer = CreateSceneRenderer(host);

		using EditorShell shell = new(
			session,
			projectRoot,
			layout,
			sceneRenderer);

		// The start screen can open a project after startup. Preferences
		// follow whichever project is open so its saved settings are not
		// replaced by the startup project's values on exit.
		using EditorPreferences preferences = new(session, shell, settings);

		if (sceneRenderer is not null)
		{
			// Viewport passes are recorded into the frame's own command
			// buffer before the UI pass, which is what lets one
			// submission fill a render target and then sample it.
			host.BeforeUiPass = commandBuffer =>
			{
				sceneRenderer.FrameSlot = host.CurrentFrameSlot;
				sceneRenderer.RecordFrame(commandBuffer);
			};
		}

		// A command-line scale wins for this run; otherwise the scale
		// saved with the workspace is restored. Either way it is applied
		// before the first frame, which keeps startup free of a visible
		// rebuild.
		shell.ApplyUiScale(uiScale ?? settings.InterfaceScale);

		host.Ui.Add(shell);
		shell.ExitRequested += host.RequestExit;
		host.ConfirmClose = shell.ConfirmWindowClose;

		// Free-look asks for the window's native relative pointer mode
		// through the UI context. The capture is disposed before the
		// host, so the pointer is always given back while the window
		// still exists.
		using WindowPointerCapture pointerCapture =
			WindowPointerCapture.Attach(host.Window);

		host.Ui.PointerCapture = pointerCapture;

		Console.WriteLine(
			$"Window: {host.Ui.Surface.Width:0}x{host.Ui.Surface.Height:0} " +
			$"at interface scale {shell.UiScale * 100f:0}%");

		int frames = 0;
		int projectRevision = preferences.ProjectRevision;
		Volt.Platform.Windows.WindowSize sizeAtProjectOpen = initialWindowSize;

		host.Run(
			ui =>
			{
				// A project opened from the start screen cannot resize the
				// window that already exists. Remember the size it was
				// opened at, so an unchanged window does not overwrite the
				// size that project saved for its next launch.
				if (preferences.ProjectRevision != projectRevision)
				{
					projectRevision = preferences.ProjectRevision;
					sizeAtProjectOpen = host.Window.Size;
				}

				session.Update(ui.DeltaSeconds);
				shell.Update(ui.DeltaSeconds, ui.DpiScale);

				frames++;

				if (frameLimit is int limit && frames >= limit)
				{
					ui.RequestExit();
				}
			});

		// A maximized or minimized window does not report a useful restore
		// size: the first would reopen a normal window at screen size, and
		// the second may report no drawable area. Keep the project's saved
		// size in both cases. The same applies when the window still has
		// the size it was created with, because a project opened from the
		// start screen could not be resized to its own saved size mid-run.
		int savedWidth = preferences.Current.WindowWidth;
		int savedHeight = preferences.Current.WindowHeight;

		if (!host.Window.IsMaximized)
		{
			Volt.Platform.Windows.WindowSize windowSize = host.Window.Size;

			bool sizeChanged =
				windowSize.Width != sizeAtProjectOpen.Width ||
				windowSize.Height != sizeAtProjectOpen.Height;

			if (windowSize.IsPositive && sizeChanged)
			{
				savedWidth = windowSize.Width;
				savedHeight = windowSize.Height;
			}
		}

		SaveWorkspace(session.Project?.RootPath, shell.Layout);
		preferences.Save(savedWidth, savedHeight);

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
	/// Writes the editor's chrome to a PNG without opening a window.
	/// </summary>
	/// <remarks>
	/// Without a project the snapshot uses a temporary starter project, so
	/// the image shows the real workspace rather than the start screen;
	/// the folder is removed once the image exists. The
	/// <c>--snapshot-state</c> preset can instead capture the start screen,
	/// or select a mesh and activate Move so the inspector is populated,
	/// which is what the editor guide's screenshots use.
	/// </remarks>
	private static int CaptureSnapshot(
		string? projectPath,
		int? width,
		int? height,
		float? uiScale,
		string? snapshotState,
		string snapshotPath)
	{
		string state = snapshotState ?? "workspace";
		string? temporary = null;

		using EditorSession session = new();

		try
		{
			if (state == "start")
			{
				// The start screen is the interface before a project exists.
			}

			else if (projectPath is not null)
			{
				session.OpenProject(projectPath);
			}

			else
			{
				temporary = Path.Combine(
					Path.GetTempPath(),
					$"volt-snapshot-{Guid.NewGuid():N}");

				session.NewProject(temporary, "Snapshot");
			}

			if (state == "authoring")
			{
				SceneEntity? mesh = session.Scene?.Entities.FirstOrDefault(
					entity => entity.GetComponent<MeshRendererData>() is not null);

				if (mesh is null)
				{
					throw new InvalidOperationException(
						"The authoring snapshot needs a scene with a mesh object.");
				}

				session.Select(mesh.Id);
				session.Tool = EditorTool.Translate;
			}

			string fullPath = Path.GetFullPath(snapshotPath);
			string? folder = Path.GetDirectoryName(fullPath);

			if (folder is not null)
			{
				Directory.CreateDirectory(folder);
			}

			UiRasterImage image = EditorSnapshot.Capture(
				session,
				fullPath,
				width ?? 1600,
				height ?? 960,
				uiScale: uiScale ?? 1f,
				defaultProjectFolder: state == "start"
					? "~/VoltGame"
					: null);

			Console.WriteLine(
				$"Wrote {fullPath} ({image.Width}x{image.Height}).");

			return 0;
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Could not write the snapshot: {exception.Message}");

			return 1;
		}

		finally
		{
			if (temporary is not null)
			{
				try
				{
					Directory.Delete(temporary, recursive: true);
				}

				catch (IOException)
				{
					// A leftover temporary folder must not fail a snapshot.
				}

				catch (UnauthorizedAccessException)
				{
					// The same: the image is what matters.
				}
			}
		}
	}

	/// <summary>
	/// Creates the viewport renderer, or reports why it is unavailable.
	/// </summary>
	/// <remarks>
	/// A missing or malformed scene shader must not stop the editor from
	/// opening: without a renderer the viewports draw their overlays and
	/// state, and the reason is printed once at startup.
	/// </remarks>
	private static EditorSceneRenderer? CreateSceneRenderer(
		VulkanUiHost host)
	{
		try
		{
			return new EditorSceneRenderer(
				host,
				SceneShaderPath("scene.vert.spv"),
				SceneShaderPath("scene.frag.spv"));
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine(
				"Viewport rendering is unavailable; the scene and game " +
				$"views will draw their overlays only: {exception.Message}");

			return null;
		}
	}

	/// <summary>
	/// Resolves a compiled scene shader next to the executable.
	/// </summary>
	/// <remarks>
	/// The shaders travel with the build output because the graphics
	/// project copies them, and a project reference makes that copy
	/// transitive.
	/// </remarks>
	private static string SceneShaderPath(string fileName)
	{
		return Path.Combine(
			AppContext.BaseDirectory,
			"Shaders",
			fileName);
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
		out int? width,
		out int? height,
		out float? uiScale,
		out string? snapshotPath,
		out string? snapshotState,
		out string? error)
	{
		validation = false;
		projectPath = null;
		frameLimit = null;
		width = null;
		height = null;
		uiScale = null;
		snapshotPath = null;
		snapshotState = null;
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

				case WidthArgument:
					if (index + 1 >= args.Length ||
						!int.TryParse(args[++index], out int widthPixels) ||
						widthPixels < 640)
					{
						error = "--width requires at least 640 pixels.";
						return false;
					}

					width = widthPixels;
					break;

				case HeightArgument:
					if (index + 1 >= args.Length ||
						!int.TryParse(args[++index], out int heightPixels) ||
						heightPixels < 480)
					{
						error = "--height requires at least 480 pixels.";
						return false;
					}

					height = heightPixels;
					break;

				case SnapshotArgument:
					if (index + 1 >= args.Length)
					{
						error = "--snapshot requires a file path.";
						return false;
					}

					snapshotPath = Path.GetFullPath(args[++index]);
					break;

				case SnapshotStateArgument:
					if (index + 1 >= args.Length)
					{
						error = "--snapshot-state requires workspace, authoring, or start.";
						return false;
					}

					snapshotState = args[++index].ToLowerInvariant();

					if (snapshotState is not ("workspace" or "authoring" or "start"))
					{
						error = "--snapshot-state must be workspace, authoring, or start.";
						return false;
					}

					break;

				case UiScaleArgument:
					if (index + 1 >= args.Length ||
						!float.TryParse(
							args[++index],
							System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture,
							out float scale) ||
						!float.IsFinite(scale) ||
						scale <= 0f)
					{
						error = "--ui-scale requires a positive factor.";
						return false;
					}

					uiScale = scale;
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

		if (snapshotState is not null && snapshotPath is null)
		{
			error = "--snapshot-state requires --snapshot.";
			return false;
		}

		if (snapshotState == "start" && projectPath is not null)
		{
			error = "--snapshot-state start cannot be combined with --project.";
			return false;
		}

		return true;
	}
}
