using System.Globalization;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Panels;

/// <summary>
/// Reports live frame pacing plus the engine counts the editor can
/// collect without a GPU.
/// </summary>
/// <remarks>
/// The shell records every frame interval in
/// <see cref="EditorSession.Frames"/>; this panel samples that window
/// ten times a second and draws it as a graph. Sampling on a timer
/// rather than on every frame keeps the labels and the graph from
/// rebuilding sixty times a second for a display no one can read that
/// fast.
/// </remarks>
internal sealed class ProfilerPanel : Panel, IFrameTick
{
	private const double SampleIntervalSeconds = 0.1;

	private readonly EditorSession _session;
	private readonly Label _frame;
	private readonly FrameGraphView _graph;
	private readonly Label _memory;
	private readonly Label _scene;
	private readonly Label _runtime;
	private readonly Label _history;
	private readonly Label _note;
	private double _elapsed;
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

		_frame = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
		};

		_graph = new FrameGraphView();

		_memory = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
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
			"The graph records the editor's own frame intervals over " +
			"the last 240 frames; the dashed line is a 60 fps budget. " +
			"Allocations count managed bytes between frames, so a frame " +
			"that allocates and collects still shows its garbage. GPU " +
			"pass timings are not captured yet.")
		{
			Font = Context?.Theme.Fonts.Small,
			Wrap = UiTextWrap.Wrap,
			TextColor = Context?.Theme.Palette.TextDisabled
		};

		Button refresh = new("Refresh")
		{
			HorizontalAlignment = UiAlignment.Start,
			MinHeight = 24f
		};

		refresh.Clicked += _ => Refresh();

		stack.Add(heading);
		stack.Add(_frame);
		stack.Add(_graph);
		stack.Add(_memory);
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

	public void Tick(double deltaSeconds, float dpiScale)
	{
		if (_disposed || !double.IsFinite(deltaSeconds))
		{
			return;
		}

		_elapsed += deltaSeconds;

		if (_elapsed < SampleIntervalSeconds)
		{
			return;
		}

		_elapsed = 0.0;
		RefreshFrame();
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

		RefreshFrame();
		InvalidateVisual();
	}

	private void RefreshFrame()
	{
		FrameTimeline frames = _session.Frames;
		MemoryTimeline allocations = _session.Allocations;

		_graph.Capture(frames);

		_frame.Text = frames.IsEmpty
			? "Frame: waiting for the first frame"
			: string.Format(
				CultureInfo.InvariantCulture,
				"Frame: {0:0.00} ms avg  ·  {1:0.00} ms min  ·  " +
				"{2:0.00} ms max  ·  {3:0} fps  ·  {4}/{5} samples",
				frames.AverageMilliseconds,
				frames.MinimumMilliseconds,
				frames.MaximumMilliseconds,
				frames.FramesPerSecond,
				frames.Count,
				frames.Capacity);

		_memory.Text = allocations.IsEmpty
			? "Allocations: waiting for the first frame"
			: string.Format(
				CultureInfo.InvariantCulture,
				"Allocations: {0}/frame avg  ·  {1} last  ·  " +
				"{2} in window  ·  heap {3}  ·  GC {4}/{5}/{6}",
				FormatBytes(allocations.AverageBytesPerFrame),
				FormatBytes(allocations.LastBytes),
				FormatBytes(allocations.TotalBytesInWindow),
				FormatBytes(GC.GetTotalMemory(forceFullCollection: false)),
				GC.CollectionCount(0),
				GC.CollectionCount(1),
				GC.CollectionCount(2));
	}

	private static string FormatBytes(double bytes)
	{
		if (bytes >= 1024.0 * 1024.0)
		{
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0:0.0} MiB",
				bytes / (1024.0 * 1024.0));
		}

		if (bytes >= 1024.0)
		{
			return string.Format(
				CultureInfo.InvariantCulture,
				"{0:0.0} KiB",
				bytes / 1024.0);
		}

		return string.Format(
			CultureInfo.InvariantCulture,
			"{0:0} B",
			bytes);
	}
}

/// <summary>
/// Plots recent frame intervals against a frame budget.
/// </summary>
/// <remarks>
/// The graph is deliberately plain: one line per interval, tinted by
/// whether that interval missed the budget, with the budget drawn as a
/// reference line. It owns its sample buffer, so sampling it every
/// tenth of a second allocates nothing.
/// </remarks>
internal sealed class FrameGraphView : Widget
{
	private readonly double[] _samples;
	private int _count;

	public FrameGraphView(int capacity = FrameTimeline.DefaultCapacity)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		_samples = new double[capacity];
		MinHeight = 72f;
		ClipToBounds = true;
	}

	/// <summary>
	/// Gets or sets the interval that counts as missing the frame
	/// budget, in milliseconds.
	/// </summary>
	public double BudgetMilliseconds { get; set; } = 1000.0 / 60.0;

	/// <summary>
	/// Copies the timeline's window into the graph and repaints it.
	/// </summary>
	public void Capture(FrameTimeline timeline)
	{
		ArgumentNullException.ThrowIfNull(timeline);

		_count = timeline.CopySamples(_samples);
		InvalidateVisual();
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: 240f,
			72f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalContentBounds;

		if (bounds.Width <= 8f || bounds.Height <= 8f)
		{
			return;
		}

		drawList.AddRoundedRect(
			bounds,
			theme.Metrics.SmallCornerRadius,
			theme.Palette.WindowBackground);

		const float Inset = 5f;

		UiRect plot = new(
			bounds.X + Inset,
			bounds.Y + Inset,
			MathF.Max(1f, bounds.Width - (Inset * 2f)),
			MathF.Max(1f, bounds.Height - (Inset * 2f)));

		double ceiling = Math.Max(BudgetMilliseconds * 1.5, 1.0);

		for (int index = 0; index < _count; index++)
		{
			ceiling = Math.Max(ceiling, _samples[index]);
		}

		float budgetY = plot.Bottom -
			(float)(BudgetMilliseconds / ceiling) * plot.Height;

		drawList.AddLine(
			new UiPoint(plot.X, budgetY),
			new UiPoint(plot.Right, budgetY),
			1f,
			theme.Palette.BorderStrong);

		if (_count < 2)
		{
			UiTextLayoutResult waiting = UiTextLayout.Create(
				"Recording frames…",
				theme.Fonts.Small,
				plot.Width,
				UiTextWrap.NoWrap,
				UiTextAlignment.Start);

			drawList.AddText(
				waiting,
				new UiPoint(plot.X + 2f, plot.Y + 2f),
				theme.Palette.TextDisabled);

			return;
		}

		float step = plot.Width / (_count - 1);

		for (int index = 1; index < _count; index++)
		{
			float previousY = PlotY(_samples[index - 1], ceiling, plot);
			float currentY = PlotY(_samples[index], ceiling, plot);

			UiColor color = _samples[index] > BudgetMilliseconds
				? theme.Palette.Warning
				: theme.Palette.Success;

			drawList.AddLine(
				new UiPoint(plot.X + ((index - 1) * step), previousY),
				new UiPoint(plot.X + (index * step), currentY),
				1.25f,
				color);
		}
	}

	private static float PlotY(double milliseconds, double ceiling, UiRect plot)
	{
		double normalized = Math.Clamp(milliseconds / ceiling, 0.0, 1.0);

		return plot.Bottom - ((float)normalized * plot.Height);
	}
}
