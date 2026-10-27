using Volt.Platform.Windows;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Vulkan;
using Volt.Ui.Widgets;

namespace Volt.UiDemo;

/// <summary>
/// Demonstrates a standalone Volt.Ui application with no editor
/// dependency.
/// </summary>
/// <remarks>
/// The whole interface is built from public Volt.Ui widgets, laid out
/// by the retained layout engine, painted into a draw list, and
/// presented by the Vulkan backend. The demo exercises nested stacks,
/// interaction, scrolling, tabs, a tree view, menus, and theming.
/// </remarks>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		bool validation = false;
		int? frameLimit = null;

		for (int index = 0; index < args.Length; index++)
		{
			switch (args[index])
			{
				case "--validation":
					validation = true;
					break;

				case "--frames":
					if (index + 1 >= args.Length ||
						!int.TryParse(args[++index], out int frames) ||
						frames <= 0)
					{
						Console.Error.WriteLine(
							"--frames requires a positive count.");

						return 2;
					}

					frameLimit = frames;
					break;

				default:
					Console.Error.WriteLine(
						$"Unknown argument '{args[index]}'.");

					return 2;
			}
		}

		try
		{
			return Run(validation, frameLimit);
		}

		catch (Exception exception)
		{
			Console.Error.WriteLine();
			Console.Error.WriteLine("Volt.Ui demo failed:");
			Console.Error.WriteLine(exception);

			return 1;
		}
	}

	private static int Run(bool validation, int? frameLimit)
	{
		using VulkanUiHost host = new(
			new VulkanUiHostOptions
			{
				Title = "VOLT UI Demo",
				EnableValidation = validation,
				Theme = UiTheme.CreateDark(),
				Size = new WindowSize(1280, 800)
			});

		Console.WriteLine(
			host.PresentationFencesEnabled
				? "Presentation fences: enabled."
				: "Presentation fences: unavailable; swapchain " +
					"generations retire through device idle.");

		UiContext ui = host.Ui;
		UiTheme theme = ui.Theme;

		Label status = new("Ready")
		{
			Font = theme.Fonts.Small,
			FlexGrow = 1f,
			VerticalAlignment = UiAlignment.Center
		};

		Label counter = new("Clicks: 0")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted,
			VerticalAlignment = UiAlignment.Center
		};

		StackPanel root = new()
		{
			Spacing = 0f
		};

		root.Add(BuildMenuBar(theme, status));
		root.Add(BuildToolbar(theme, status));
		root.Add(
			BuildWorkspace(theme, status, counter));
		root.Add(BuildStatusBar(theme, status, counter));

		ui.Add(root);

		// A shortcut installed once on the context works even while a
		// numeric field holds focus, because shortcuts are resolved
		// only when the focused widget ignores the key.
		ui.Shortcuts.Add(
			new UiShortcut(
				UiKey.F1,
				UiKeyModifiers.None,
				() => status.Text = "Shortcut F1: Volt.Ui shortcuts work."));

		int frames = 0;

		host.Run(
			window =>
			{
				frames++;

				if (frameLimit is int limit && frames >= limit)
				{
					window.RequestExit();
				}
			});

		return 0;
	}

	private static MenuBar BuildMenuBar(UiTheme theme, Label status)
	{
		MenuBar menuBar = new();

		MenuItem file = menuBar.AddMenu("File");
		file.AddChild(
			new MenuItem("New", () => status.Text = "File > New"));
		file.AddChild(
			new MenuItem("Open…", () => status.Text = "File > Open"));
		file.AddChild(MenuItem.Separator);
		file.AddChild(
			new MenuItem("Exit", () => Environment.Exit(0))
			{
				Shortcut = "Alt+F4"
			});

		MenuItem edit = menuBar.AddMenu("Edit");
		edit.AddChild(
			new MenuItem("Undo", () => status.Text = "Edit > Undo")
			{
				Shortcut = "Ctrl+Z"
			});
		edit.AddChild(
			new MenuItem("Redo", () => status.Text = "Edit > Redo")
			{
				Shortcut = "Ctrl+Y"
			});

		MenuItem help = menuBar.AddMenu("Help");
		help.AddChild(
			new MenuItem("About Volt.Ui", () => status.Text =
				"Volt.Ui is VOLT's native UI framework."));

		_ = theme;

		return menuBar;
	}

	private static Widget BuildToolbar(UiTheme theme, Label status)
	{
		StackPanel toolbar = new(UiOrientation.Horizontal)
		{
			Spacing = 6f,
			Padding = new UiThickness(8f, 6f)
		};

		Button select = new("Select")
		{
			IsSelected = true
		};

		Button move = new("Move");
		Button rotate = new("Rotate");

		select.Clicked += _ => status.Text = "Tool: Select";
		move.Clicked += _ => status.Text = "Tool: Move";
		rotate.Clicked += _ => status.Text = "Tool: Rotate";

		toolbar.Add(select);
		toolbar.Add(move);
		toolbar.Add(rotate);
		toolbar.Add(new Separator(UiOrientation.Vertical));

		CheckBox snapping = new("Snap")
		{
			IsChecked = true
		};

		snapping.CheckedChanged += box => status.Text =
			box.IsChecked ? "Snapping enabled." : "Snapping disabled.";

		toolbar.Add(snapping);
		toolbar.Add(new Spacer());
		toolbar.Add(
			new Label("Volt.Ui")
			{
				Font = theme.Fonts.Small,
				TextColor = theme.Palette.Accent,
				VerticalAlignment = UiAlignment.Center
			});

		return toolbar;
	}

	private static Widget BuildWorkspace(
		UiTheme theme,
		Label status,
		Label counter)
	{
		TreeView tree = new()
		{
			Width = UiLength.Points(240f)
		};

		TreeViewItem scene = new("scene", "Sample Scene");
		scene.AddChild(new TreeViewItem("camera", "Main Camera"));
		scene.AddChild(new TreeViewItem("light", "Directional Light"));

		TreeViewItem player = new("player", "Player");
		player.AddChild(new TreeViewItem("body", "Body"));
		player.AddChild(new TreeViewItem("hitbox", "Hitbox"));
		scene.AddChild(player);

		scene.IsExpanded = true;
		player.IsExpanded = true;

		tree.AddRoot(scene);
		tree.AddRoot(new TreeViewItem("world", "World"));

		tree.SelectionChanged += (_, items) =>
		{
			TreeViewItem? selected = items.Count > 0 ? items[0] : null;

			status.Text = selected is null
				? "Hierarchy selection cleared."
				: $"Selected '{selected.Text}'.";
		};

		TabView tabs = new()
		{
			FlexGrow = 1f
		};

		tabs.AddPage(
			"controls",
			"Controls",
			BuildControlsPage(theme, status, counter));

		tabs.AddPage(
			"layout",
			"Layout",
			BuildLayoutPage(theme));

		tabs.AddPage(
			"scroll",
			"Scroll",
			BuildScrollPage(theme));

		DockLayoutHost host = new(tree, tabs, theme);

		return host;
	}

	private static Widget BuildControlsPage(
		UiTheme theme,
		Label status,
		Label counter)
	{
		StackPanel content = new()
		{
			Spacing = 12f,
			Padding = new UiThickness(18f)
		};

		content.Add(
			new Label("Interactive controls")
			{
				Font = theme.Fonts.Heading,
				TextColor = theme.Palette.Text
			});

		content.Add(
			new Label(
				"Every control below is a retained Volt.Ui widget. " +
				"Focus, hover, pointer capture, and keyboard input " +
				"are routed by the UI context, not by the demo.")
			{
				Font = theme.Fonts.Body,
				TextColor = theme.Palette.TextMuted,
				Wrap = UiTextWrap.Wrap
			});

		Label nameLabel = new("Name: (empty)")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted
		};

		TextField name = new(string.Empty)
		{
			Placeholder = "Type a name…",
			MaximumLength = 48
		};

		name.TextChanged += field =>
			nameLabel.Text = field.Text.Length == 0
				? "Name: (empty)"
				: $"Name: {field.Text}";

		content.Add(new Label("Text field") { Font = theme.Fonts.Small });
		content.Add(name);
		content.Add(nameLabel);

		Label amountLabel = new("Value: 0.35")
		{
			Font = theme.Fonts.Small,
			TextColor = theme.Palette.TextMuted
		};

		Slider slider = new()
		{
			Minimum = 0f,
			Maximum = 1f,
			Step = 0.01f,
			Value = 0.35f
		};

		ProgressBar progress = new()
		{
			Value = 0.35f,
			Height = UiLength.Points(10f)
		};

		slider.ValueChanged += control =>
		{
			amountLabel.Text = $"Value: {control.Value:0.00}";
			progress.Value = control.Value;
		};

		content.Add(new Label("Slider") { Font = theme.Fonts.Small });
		content.Add(slider);
		content.Add(amountLabel);
		content.Add(progress);

		StackPanel buttons = new(UiOrientation.Horizontal)
		{
			Spacing = 8f
		};

		int clicks = 0;

		Button primary = new("Primary action")
		{
			IsPrimary = true
		};

		primary.Clicked += _ =>
		{
			clicks++;
			counter.Text = $"Clicks: {clicks}";
			status.Text = $"Primary action clicked {clicks} time(s).";
		};

		Button toggle = new("Toggle");

		toggle.Clicked += button =>
		{
			button.IsSelected = !button.IsSelected;
			status.Text = button.IsSelected
				? "Toggle is on."
				: "Toggle is off.";
		};

		buttons.Add(primary);
		buttons.Add(toggle);

		content.Add(buttons);

		return content;
	}

	private static Widget BuildLayoutPage(UiTheme theme)
	{
		StackPanel page = new()
		{
			Spacing = 12f,
			Padding = new UiThickness(18f)
		};

		page.Add(
			new Label("Nested layout")
			{
				Font = theme.Fonts.Heading
			});

		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 10f,
			Height = UiLength.Points(140f)
		};

		for (int index = 0; index < 3; index++)
		{
			Panel card = new()
			{
				FlexGrow = 1f,
				Background = theme.Palette.SurfaceRaised,
				DrawBorder = true,
				BorderColor = theme.Palette.Border
			};

			card.Add(
				new Label($"Card {index + 1}")
				{
					Font = theme.Fonts.Body,
					HorizontalAlignment = UiAlignment.Center,
					VerticalAlignment = UiAlignment.Center
				});

			row.Add(card);
		}

		page.Add(row);

		page.Add(
			new Label(
				"Cards share available width through flex growth and " +
				"resolve their text alignment inside a measured " +
				"bounds rectangle.")
			{
				Font = theme.Fonts.Small,
				TextColor = theme.Palette.TextMuted,
				Wrap = UiTextWrap.Wrap
			});

		return page;
	}

	private static Widget BuildScrollPage(UiTheme theme)
	{
		StackPanel list = new()
		{
			Spacing = 4f,
			Padding = new UiThickness(18f)
		};

		for (int index = 0; index < 60; index++)
		{
			list.Add(
				new Label($"Scrollable row {index + 1}")
				{
					Font = theme.Fonts.Small,
					TextColor = index % 2 == 0
						? theme.Palette.Text
						: theme.Palette.TextMuted
				});
		}

		ScrollView scroll = new()
		{
			Content = list
		};

		return scroll;
	}

	private static Widget BuildStatusBar(
		UiTheme theme,
		Label status,
		Label counter)
	{
		Panel bar = new()
		{
			Height = UiLength.Points(26f),
			Background = theme.Palette.Surface
		};

		StackPanel row = new(UiOrientation.Horizontal)
		{
			Spacing = 8f,
			Padding = new UiThickness(8f, 0f)
		};

		row.Add(status);
		row.Add(counter);
		bar.Add(row);

		return bar;
	}

	/// <summary>
	/// Composes a hierarchy tree and a tabbed content area beside a
	/// draggable splitter without requiring the docking subsystem.
	/// </summary>
	private sealed class DockLayoutHost : StackPanel
	{
		public DockLayoutHost(TreeView tree, TabView tabs, UiTheme theme)
			: base(UiOrientation.Horizontal)
		{
			Spacing = 0f;
			FlexGrow = 1f;

			Panel treePanel = new()
			{
				Width = UiLength.Points(250f),
				Background = theme.Palette.Surface,
				Padding = new UiThickness(6f)
			};

			treePanel.Add(tree);

			Splitter splitter = new(UiOrientation.Horizontal)
			{
				Width = UiLength.Points(5f)
			};

			Panel content = new()
			{
				FlexGrow = 1f,
				Background = theme.Palette.WindowBackground
			};

			content.Add(tabs);

			Add(treePanel);
			Add(splitter);
			Add(content);
		}
	}
}
