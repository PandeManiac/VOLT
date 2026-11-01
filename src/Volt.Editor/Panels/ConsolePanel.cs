using System.Globalization;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

/// <summary>
/// The editor's diagnostic log, with per-severity filtering.
/// </summary>
/// <remarks>
/// Entries come from the session, so the panel reports what actually
/// happened in the editor rather than a decorative stream. Each row
/// carries its severity icon and tint, and the header buttons show how
/// many entries of each kind exist so a collapsed warning count is
/// never hidden.
/// </remarks>
public sealed class ConsolePanel : Panel
{
	private readonly EditorSession _session;
	private readonly StackPanel _rows;
	private readonly ScrollView _scroll;
	private readonly EmptyStateView _empty;
	private readonly Panel _emptyHost;
	private readonly ToolButton _info;
	private readonly ToolButton _warnings;
	private readonly ToolButton _errors;
	private readonly IconButton _clear;
	private readonly Dropdown _source;

	private bool _disposed;

	public ConsolePanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		ClipToBounds = true;

		StackPanel stack = new()
		{
			Spacing = 5f,
			Padding = new UiThickness(8f, 6f, 8f, 6f)
		};

		StackPanel header = new(UiOrientation.Horizontal)
		{
			Spacing = 4f
		};

		EditorConsoleFilter initial = _session.ConsoleFilter;

		_info = CreateFilter(UiIcon.Info, "Show information messages", () =>
			SetFilter(_session.ConsoleFilter with
			{
				ShowInfo = !_session.ConsoleFilter.ShowInfo
			}));

		_warnings = CreateFilter(UiIcon.Warning, "Show warnings", () =>
			SetFilter(_session.ConsoleFilter with
			{
				ShowWarnings = !_session.ConsoleFilter.ShowWarnings
			}));

		_errors = CreateFilter(UiIcon.Error, "Show errors", () =>
			SetFilter(_session.ConsoleFilter with
			{
				ShowErrors = !_session.ConsoleFilter.ShowErrors
			}));

		_info.IsActive = initial.ShowInfo;
		_warnings.IsActive = initial.ShowWarnings;
		_errors.IsActive = initial.ShowErrors;

		_clear = new IconButton(UiIcon.Trash, "Clear the console")
		{
			Width = UiLength.Points(26f),
			Height = UiLength.Points(24f)
		};
		_clear.Clicked += _ => _session.ClearLog();

		// Severity and source answer different questions: how loud a
		// message is, and who said it. Both filters are independent so
		// warnings from validation and from a running game can be read
		// apart.
		_source = new Dropdown
		{
			MinWidth = 104f,
			MinHeight = 24f
		};

		_source.SetItems(
			["All sources", "Editor", "Validation", "Game"],
			SourceIndex(initial.Source));

		_source.SelectionChanged += _ =>
		{
			EditorLogSource? source = _source.SelectedIndex switch
			{
				1 => EditorLogSource.Editor,
				2 => EditorLogSource.Validation,
				3 => EditorLogSource.Game,
				_ => null
			};

			SetFilter(_session.ConsoleFilter with { Source = source });
		};

		header.Add(_info);
		header.Add(_warnings);
		header.Add(_errors);
		header.Add(_source);
		header.Add(new Spacer());
		header.Add(_clear);

		_rows = new StackPanel
		{
			Spacing = 1f
		};

		_scroll = new ScrollView
		{
			FlexGrow = 1f,
			MinHeight = 40f,
			Content = _rows
		};

		_empty = new EmptyStateView(
			UiIcon.Console,
			"Console is clear",
			"Editor actions, warnings, and engine diagnostics appear here.");

		// The empty state is an overlay rather than a row in the stack:
		// it centers itself over the message list and adds no minimum
		// height, which keeps the panel usable in a short bottom dock.
		_emptyHost = new Panel
		{
			DrawBackground = false,
			IsHitTestVisible = false
		};

		_emptyHost.Add(_empty);

		stack.Add(header);
		stack.Add(_scroll);

		Add(stack);
		Add(_emptyHost);

		_session.LogChanged += OnLogChanged;
		_session.SettingsChanged += OnSettingsChanged;
	}

	protected override void OnAttached()
	{
		// Rows carry the theme's fonts, so they are built once the panel
		// knows which context (and therefore which font set) it belongs to.
		// Rebuilding also replaces any row created while detached.
		Rebuild();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_session.LogChanged -= OnLogChanged;
		_session.SettingsChanged -= OnSettingsChanged;
		base.Dispose();
	}

	private static ToolButton CreateFilter(
		UiIcon icon,
		string tooltip,
		Action toggled)
	{
		ToolButton button = new(icon, string.Empty)
		{
			TooltipText = tooltip,
			MinHeight = 24f,
			MinWidth = 46f,
			IsActive = true
		};

		button.Clicked += _ => toggled();
		return button;
	}

	private void OnLogChanged(EditorSession session)
	{
		Rebuild();
	}

	private void Rebuild()
	{
		for (int index = _rows.Children.Count - 1; index >= 0; index--)
		{
			_rows.Children[index].Dispose();
		}

		_rows.ClearChildren();

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		IReadOnlyList<EditorLogEntry> entries = _session.LogEntries;
		EditorConsoleFilter filter = _session.ConsoleFilter;

		int info = entries.Count(entry => entry.Level == EditorLogLevel.Info);
		int warnings = entries.Count(entry => entry.Level == EditorLogLevel.Warning);
		int errors = entries.Count(entry => entry.Level == EditorLogLevel.Error);

		_info.Text = info.ToString(CultureInfo.InvariantCulture);
		_warnings.Text = warnings.ToString(CultureInfo.InvariantCulture);
		_errors.Text = errors.ToString(CultureInfo.InvariantCulture);

		SynchChrome(filter);

		int start = Math.Max(0, entries.Count - 200);
		int shown = 0;

		for (int index = start; index < entries.Count; index++)
		{
			EditorLogEntry entry = entries[index];

			if (!filter.Shows(entry.Level, entry.Source))
			{
				continue;
			}

			_rows.Add(BuildRow(entry, theme));
			shown++;
		}

		_emptyHost.IsVisible = shown == 0;
		_scroll.IsVisible = shown > 0;

		if (shown == 0)
		{
			_empty.Title = entries.Count == 0
				? "Console is clear"
				: "All messages are filtered out";

			_empty.Description = entries.Count == 0
				? "Editor actions, warnings, and engine diagnostics appear here."
				: "Enable a severity or source filter above to show its messages.";
		}

		_scroll.InvalidateMeasure();
		_scroll.ScrollToEnd();
	}

	/// <summary>
	/// Writes the filter to the session, which owns it and raises the
	/// settings event the preferences coordinator persists.
	/// </summary>
	private void SetFilter(EditorConsoleFilter filter)
	{
		_session.ConsoleFilter = filter;
		SynchChrome(filter);
		Rebuild();
	}

	private void OnSettingsChanged(EditorSession session)
	{
		SynchChrome(session.ConsoleFilter);
		Rebuild();
	}

	/// <summary>
	/// Makes the header controls show the filter the session holds.
	/// </summary>
	/// <remarks>
	/// A scale change rebuilds the panels, and the preferences coordinator
	/// can replace the filter when a project is adopted, so the controls
	/// follow the value instead of owning it.
	/// </remarks>
	private void SynchChrome(EditorConsoleFilter filter)
	{
		_info.IsActive = filter.ShowInfo;
		_warnings.IsActive = filter.ShowWarnings;
		_errors.IsActive = filter.ShowErrors;

		int index = SourceIndex(filter.Source);

		if (_source.SelectedIndex != index)
		{
			_source.SelectedIndex = index;
		}
	}

	private static int SourceIndex(EditorLogSource? source)
	{
		return source switch
		{
			EditorLogSource.Editor => 1,
			EditorLogSource.Validation => 2,
			EditorLogSource.Game => 3,
			_ => 0
		};
	}

	private static Widget BuildRow(EditorLogEntry entry, UiTheme theme)
	{
		Panel row = new()
		{
			DrawBackground = false,
			MinHeight = 20f
		};

		StackPanel content = new(UiOrientation.Horizontal)
		{
			Spacing = 6f,
			Padding = new UiThickness(2f, 1f)
		};

		UiColor tint = EditorIcons.LogColor(entry.Level, theme.Palette);

		content.Add(new IconView(EditorIcons.ForLogLevel(entry.Level))
		{
			Width = UiLength.Points(12f),
			Height = UiLength.Points(12f),
			Color = tint,
			VerticalAlignment = UiAlignment.Center
		});

		content.Add(new Label(entry.Timestamp.ToString(
			"HH:mm:ss",
			CultureInfo.InvariantCulture))
		{
			Font = theme.Fonts.MonoSmall,
			TextColor = theme.Palette.TextDisabled,
			Width = UiLength.Points(58f),
			VerticalAlignment = UiAlignment.Center
		});

		content.Add(new Label(entry.Message)
		{
			Font = theme.Fonts.Small,
			TextColor = entry.Level == EditorLogLevel.Info
				? theme.Palette.TextMuted
				: tint,
			Wrap = UiTextWrap.Wrap,
			FlexGrow = 1f
		});

		row.Add(content);
		return row;
	}
}
