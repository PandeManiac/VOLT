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
/// Reports live frame pacing and allocation pressure plus the engine
/// counts the editor can collect without a GPU.
/// </summary>
/// <remarks>
/// The shell records every frame interval and allocation delta in
/// <see cref="EditorSession.Frames"/> and
/// <see cref="EditorSession.Allocations"/>; this panel samples those
/// windows ten times a second and draws them. Sampling on a timer
/// rather than on every frame keeps the labels and graphs from
/// rebuilding sixty times a second for a display no one can read that
/// fast.
/// </remarks>
internal sealed class ProfilerPanel : Panel, IFrameTick
{
	private const double SampleIntervalSeconds = 0.1;

	private readonly EditorSession _session;
	private readonly Label _frame;
	private readonly TimelineGraphView _frameGraph;
	private readonly Label _memory;
	private readonly TimelineGraphView _allocationGraph;
	private readonly Label _scene;
	private readonly Label _runtime;
	private readonly Label _history;
	private readonly Label _note;
	private double _elapsed;
	private bool _disposed;

	/// <summary>
	/// How many times the panel has sampled the session. The tests use it
	/// to check that the sample timer, not the simulation rate, sets the
	/// pace.
	/// </summary>
	internal int RefreshCount { get; private set; }

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

		_frameGraph = new TimelineGraphView
		{
			EmptyText = "Recording frames…"
		};

		_memory = new Label(string.Empty)
		{
			Font = Context?.Theme.Fonts.Small
		};

		_allocationGraph = new TimelineGraphView
		{
			EmptyText = "Recording allocations…"
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
			"The graphs record the editor's own frame intervals and " +
			"managed allocations over the last 240 frames; the dashed " +
			"line is the target frame budget. Allocations count bytes allocated " +
			"between frames, so a frame that allocates and collects still " +
			"shows its garbage. GPU pass timings are not captured yet.")
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
		stack.Add(_frameGraph);
		stack.Add(_memory);
		stack.Add(_allocationGraph);
		stack.Add(_scene);
		stack.Add(_runtime);
		stack.Add(_history);
		stack.Add(refresh);
		stack.Add(_note);

		Add(stack);

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.PlayStateChanged += OnSessionChanged;

		// RuntimeUpdated is deliberately not subscribed: it fires once per
		// frame that advanced a fixed step, and Tick samples on its own
		// timer. Listening would format every label, sample the GC, and
		// recapture both graphs at frame rate, inflating the very
		// allocations this panel reports.
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
		RefreshCount++;

		FrameTimeline frames = _session.Frames;
		MemoryTimeline allocations = _session.Allocations;

		_frameGraph.BudgetMilliseconds = _session.TargetFrameMilliseconds;
		_frameGraph.Capture(frames);
		_allocationGraph.Capture(allocations);

		_frame.Text = frames.IsEmpty
			? "Frame: waiting for the first frame"
			: string.Format(
				CultureInfo.InvariantCulture,
				"Frame: {0:0.00} ms avg  ·  {1:0.00} ms min  ·  " +
				"{2:0.00} ms max  ·  {3:0} fps  ·  target {4} fps  ·  " +
				"{5}/{6} samples",
				frames.AverageMilliseconds,
				frames.MinimumMilliseconds,
				frames.MaximumMilliseconds,
				frames.FramesPerSecond,
				_session.TargetFrameRate,
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
/// Plots one rolling sample window, optionally against a budget.
/// </summary>
/// <remarks>
/// The graph is deliberately plain: one line per sample, tinted by
/// whether the sample missed the budget when one is set, with the
/// budget drawn as a reference line. It owns its sample buffer, so
/// sampling it every tenth of a second allocates nothing, and it
/// shares one implementation between frame times and allocations
/// rather than duplicating the plotting rules.
/// </remarks>
internal sealed class TimelineGraphView : Widget
{
	private readonly double[] _samples;
	private int _count;

	public TimelineGraphView(int capacity = FrameTimeline.DefaultCapacity)
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
	/// Gets or sets the value that counts as missing the budget, in the
	/// samples' own unit. Null draws no reference line and never warns.
	/// </summary>
	public double? BudgetMilliseconds { get; set; }

	/// <summary>
	/// Gets or sets the caption shown until two samples exist.
	/// </summary>
	public string EmptyText { get; set; } = "Recording…";

	/// <summary>
	/// Copies the frame window into the graph and repaints it.
	/// </summary>
	public void Capture(FrameTimeline timeline)
	{
		ArgumentNullException.ThrowIfNull(timeline);

		_count = timeline.CopySamples(_samples);
		InvalidateVisual();
	}

	/// <summary>
	/// Copies the allocation window into the graph and repaints it.
	/// </summary>
	public void Capture(MemoryTimeline timeline)
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

		double ceiling = 1.0;

		if (BudgetMilliseconds is double budget)
		{
			ceiling = Math.Max(ceiling, budget * 1.5);
		}

		for (int index = 0; index < _count; index++)
		{
			ceiling = Math.Max(ceiling, _samples[index]);
		}

		if (BudgetMilliseconds is double budgetLine)
		{
			float budgetY = plot.Bottom -
				(float)(budgetLine / ceiling) * plot.Height;

			drawList.AddLine(
				new UiPoint(plot.X, budgetY),
				new UiPoint(plot.Right, budgetY),
				1f,
				theme.Palette.BorderStrong);
		}

		if (_count < 2)
		{
			UiTextLayoutResult waiting = UiTextLayout.Create(
				EmptyText,
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

			UiColor color = BudgetMilliseconds is double limit &&
				_samples[index] > limit
					? theme.Palette.Warning
					: theme.Palette.Success;

			drawList.AddLine(
				new UiPoint(plot.X + ((index - 1) * step), previousY),
				new UiPoint(plot.X + (index * step), currentY),
				1.25f,
				color);
		}
	}

	private static float PlotY(double value, double ceiling, UiRect plot)
	{
		double normalized = Math.Clamp(value / ceiling, 0.0, 1.0);

		return plot.Bottom - ((float)normalized * plot.Height);
	}
}
