using Volt.Editor.Core.Runtime;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

public sealed class ProfilerPanel : Panel
{
	private readonly EditorSession _session;
	private readonly Label _scene;
	private readonly Label _runtime;
	private readonly Label _history;
	private readonly Label _note;
	private bool _disposed;

	public ProfilerPanel(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		Padding = new UiThickness(10f);

		StackPanel stack = new()
		{
			Spacing = 6f
		};

		Label heading = new("VOLT Performance Snapshot")
		{
			Font = Context?.Theme.Fonts.Heading
		};

		_scene = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
		};

		_runtime = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
		};

		_history = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
		};

		_note = new Label(
			"Frame-time capture and allocation counters are wired to " +
			"the runtime host; this panel reports engine-side counts " +
			"that are available headlessly.")
		{
			Font = Context?.Theme.Fonts.Small,
			Wrap = Volt.Ui.Text.UiTextWrap.Wrap,
			TextColor = Context?.Theme.Palette.TextDisabled
		};

		Button refresh = new("Refresh")
		{
			HorizontalAlignment = UiAlignment.Start,
			MinHeight = 24f
		};

		refresh.Clicked += _ => Refresh();

		stack.Add(heading);
		stack.Add(_scene);
		stack.Add(_runtime);
		stack.Add(_history);
		stack.Add(refresh);
		stack.Add(_note);

		Add(stack);

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.PlayStateChanged += OnSessionChanged;
		_session.RuntimeUpdated += OnSessionChanged;

		Refresh();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
		_session.PlayStateChanged -= OnSessionChanged;
		_session.RuntimeUpdated -= OnSessionChanged;
		base.Dispose();
	}

	private void OnSessionChanged(EditorSession session)
	{
		Refresh();
	}

	private void Refresh()
	{
		_scene.Text =
			$"Scene entities: {_session.Scene?.EntityCount ?? 0}   " +
			$"Selection: {_session.Selection.Count}";

		RuntimeScene? runtime = _session.Play?.Runtime;

		_runtime.Text = runtime is null
			? $"Runtime: {_session.PlayState}"
			: $"Runtime: {_session.PlayState}   " +
				$"World entities: {runtime.World.Count}   " +
				$"Renderable: {runtime.RenderableCount}   " +
				$"Ticks: {runtime.TickCount}";

		_history.Text =
			$"Undo: {_session.Undo.UndoCount}   " +
			$"Redo: {_session.Undo.RedoCount}   " +
			$"Grid snap: {(_session.GridSnapping ? _session.SnapStep.ToString("0.###") : "off")}";

		InvalidateVisual();
	}
}
