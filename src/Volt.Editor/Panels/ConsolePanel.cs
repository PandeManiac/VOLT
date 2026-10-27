using System.Globalization;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

public sealed class ConsolePanel : Panel
{
	private readonly EditorSession _session;
	private readonly StackPanel _rows;
	private readonly ScrollView _scroll;
	private bool _disposed;

	public ConsolePanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(6f);

		StackPanel stack = new()
		{
			Spacing = 4f
		};

		Button clear = new("Clear")
		{
			MinHeight = 22f,
			HorizontalAlignment = UiAlignment.Start
		};

		clear.Clicked += _ => _session.ClearLog();

		stack.Add(clear);

		_rows = new StackPanel
		{
			Spacing = 1f
		};

		_scroll = new ScrollView
		{
			FlexGrow = 1f,
			MinHeight = 60f,
			Content = _rows
		};

		stack.Add(_scroll);
		Add(stack);

		_session.LogChanged += OnLogChanged;
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
		base.Dispose();
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

		UiTheme? theme = Context?.Theme;
		IReadOnlyList<EditorLogEntry> entries = _session.LogEntries;
		int start = Math.Max(0, entries.Count - 200);

		for (int index = start; index < entries.Count; index++)
		{
			EditorLogEntry entry = entries[index];

			string time = entry.Timestamp.ToString(
				"HH:mm:ss",
				CultureInfo.InvariantCulture);

			Label row = new($"[{time}] {entry.Message}")
			{
				Font = theme?.Fonts.MonoSmall,
				Wrap = UiTextWrap.Wrap,
				TextColor = entry.Level switch
				{
					EditorLogLevel.Error => theme?.Palette.Danger,
					EditorLogLevel.Warning => theme?.Palette.Warning,
					_ => theme?.Palette.TextMuted
				}
			};

			_rows.Add(row);
		}

		if (entries.Count == 0)
		{
			_rows.Add(new Label("Console is empty.")
			{
				Font = theme?.Fonts.Small,
				TextColor = theme?.Palette.TextDisabled
			});
		}

		_scroll.InvalidateMeasure();
		_scroll.ScrollToEnd();
	}
}
