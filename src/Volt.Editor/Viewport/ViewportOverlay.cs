using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Editor.Viewport;

/// <summary>
/// Identifies one control of the floating viewport chrome.
/// </summary>
internal enum ViewportOverlayControl
{
	/// <summary>No control.</summary>
	None = 0,

	/// <summary>Selects an authoring tool.</summary>
	Tool,

	/// <summary>Switches the gizmo between local and world space.</summary>
	ToggleSpace,

	/// <summary>Toggles grid snapping.</summary>
	ToggleSnap,

	/// <summary>Cycles the snap increment.</summary>
	CycleSnapStep,

	/// <summary>Toggles the ground grid.</summary>
	ToggleGrid,

	/// <summary>Frames the selection.</summary>
	FrameSelection
}

/// <summary>
/// One control of the floating chrome, placed in viewport pixels.
/// </summary>
/// <param name="Control">What the control does.</param>
/// <param name="Tool">The tool the control selects.</param>
/// <param name="Icon">The glyph drawn in the control, or none.</param>
/// <param name="Caption">Text drawn in the control, or empty.</param>
/// <param name="Label">The name shown in the control's tooltip.</param>
/// <param name="Bounds">The control's rectangle in viewport pixels.</param>
/// <param name="IsActive">Whether the control is engaged.</param>
/// <param name="IsEnabled">Whether the control can be used now.</param>
internal readonly record struct ViewportOverlayItem(
	ViewportOverlayControl Control,
	EditorTool Tool,
	UiIcon Icon,
	string Caption,
	string Label,
	UiRect Bounds,
	bool IsActive,
	bool IsEnabled);

/// <summary>
/// The floating chrome of the scene view: the tool cluster and the
/// display cluster, laid out over the rendered image.
/// </summary>
/// <remarks>
/// <para>
/// A viewport is a single widget that paints its own overlays, so its
/// controls are laid out here as data and hit-tested by the viewport
/// rather than added to the widget tree. The geometry is otherwise the
/// same as the toolbar's: the same theme styles, the same icon
/// painter, and the same state treatments, so a control in the
/// viewport looks and behaves like the control in the bar.
/// </para>
/// <para>
/// The layout depends only on the viewport rectangle, the interface
/// metrics, and the session, which is what makes the chrome testable
/// without a window and correct at any interface scale: the clusters
/// wrap onto another row instead of running off a narrow view.
/// </para>
/// </remarks>
internal sealed class ViewportOverlay
{
	/// <summary>
	/// The distance the clusters keep from the viewport edges.
	/// </summary>
	public const float Margin = 10f;

	private readonly List<ViewportOverlayItem> _items = [];
	private readonly List<UiRect> _groups = [];

	/// <summary>
	/// Gets the controls built by the last <see cref="Build"/> call.
	/// </summary>
	public IReadOnlyList<ViewportOverlayItem> Items => _items;

	/// <summary>
	/// Gets the backing plate of each contiguous cluster run.
	/// </summary>
	public IReadOnlyList<UiRect> Groups => _groups;

	/// <summary>
	/// Lays the chrome out for one viewport rectangle.
	/// </summary>
	/// <param name="bounds">The viewport rectangle in UI units.</param>
	/// <param name="session">The editor state the controls read.</param>
	/// <param name="metrics">The interface metrics.</param>
	/// <param name="font">The font the captions are measured with.</param>
	/// <param name="showGrid">Whether the ground grid is shown.</param>
	/// <param name="isPlaying">
	/// True while the scene runs, which disables the tools: a running
	/// game owns its objects, so authoring them would fight it.
	/// </param>
	public void Build(
		UiRect bounds,
		EditorSession session,
		UiMetrics metrics,
		UiFont font,
		bool showGrid,
		bool isPlaying)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(metrics);
		ArgumentNullException.ThrowIfNull(font);

		_items.Clear();
		_groups.Clear();

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return;
		}

		float edge = MathF.Max(18f, metrics.ToolButtonSize);
		float gap = MathF.Max(1f, metrics.TightSpacing);
		float groupPadding = MathF.Max(2f, metrics.TightSpacing * 0.75f);
		float margin = MathF.Min(Margin, MathF.Max(2f, bounds.Width * 0.05f));
		float left = bounds.X + margin;
		float right = bounds.Right - margin;

		if (right - left < edge)
		{
			return;
		}

		List<ViewportOverlayItem>[] clusters =
		[
			BuildToolCluster(session, metrics, edge, isPlaying),
			BuildDisplayCluster(session, metrics, font, edge, showGrid)
		];

		float x = left;
		float y = bounds.Y + margin;
		int runStart = -1;
		int runCluster = -1;
		float runTop = 0f;

		for (int cluster = 0; cluster < clusters.Length; cluster++)
		{
			foreach (ViewportOverlayItem item in clusters[cluster])
			{
				float width = MathF.Min(item.Bounds.Width, right - left);

				// A cluster wraps onto the next row rather than
				// shrinking a control below a usable size.
				if (x > left && x + width > right)
				{
					CloseRun(runStart, groupPadding, bounds);
					runStart = -1;

					x = left;
					y += edge + gap;
				}

				if (runStart < 0 || runCluster != cluster || runTop != y)
				{
					// A new cluster, or the same cluster starting another
					// row, ends the plate the previous controls were drawn
					// on.
					CloseRun(runStart, groupPadding, bounds);

					runStart = _items.Count;
					runCluster = cluster;
					runTop = y;
				}

				_items.Add(item with
				{
					Bounds = new UiRect(x, y, width, MathF.Min(edge, bounds.Height))
				});

				x += width + gap;
			}
		}

		CloseRun(runStart, groupPadding, bounds);
	}

	/// <summary>
	/// Finds the control under a pointer, topmost first.
	/// </summary>
	/// <returns>
	/// The control, or a value whose
	/// <see cref="ViewportOverlayItem.Control"/> is
	/// <see cref="ViewportOverlayControl.None"/> when the pointer is
	/// over none of them.
	/// </returns>
	public ViewportOverlayItem Pick(UiPoint pointer)
	{
		for (int index = _items.Count - 1; index >= 0; index--)
		{
			ViewportOverlayItem item = _items[index];

			if (item.Bounds.Contains(pointer))
			{
				return item;
			}
		}

		return default;
	}

	/// <summary>
	/// Gets the rectangle of a control, for a tooltip to hang from.
	/// </summary>
	public UiRect BoundsOf(ViewportOverlayControl control)
	{
		foreach (ViewportOverlayItem item in _items)
		{
			if (item.Control == control)
			{
				return item.Bounds;
			}
		}

		return UiRect.Empty;
	}

	/// <summary>
	/// Paints the chrome.
	/// </summary>
	public void Render(
		UiDrawList drawList,
		UiTheme theme,
		ViewportOverlayControl hovered,
		ViewportOverlayControl pressed)
	{
		ArgumentNullException.ThrowIfNull(drawList);
		ArgumentNullException.ThrowIfNull(theme);

		if (_items.Count == 0)
		{
			return;
		}

		UiStyle group = theme.GetOverlayStyle();

		foreach (UiRect plate in _groups)
		{
			drawList.AddRoundedRect(
				plate,
				theme.Metrics.CornerRadius,
				group.Background);

			drawList.AddBorder(
				plate,
				group.Border,
				group.BorderWidth,
				theme.Metrics.CornerRadius);
		}

		foreach (ViewportOverlayItem item in _items)
		{
			RenderItem(drawList, theme, item, hovered, pressed);
		}
	}

	private static void RenderItem(
		UiDrawList drawList,
		UiTheme theme,
		ViewportOverlayItem item,
		ViewportOverlayControl hovered,
		ViewportOverlayControl pressed)
	{
		UiWidgetState state = !item.IsEnabled
			? UiWidgetState.Disabled
			: pressed == item.Control
				? UiWidgetState.Pressed
				: hovered == item.Control
					? UiWidgetState.Hovered
					: UiWidgetState.Normal;

		UiStyle style = theme.GetIconButtonStyle(state, item.IsActive);

		if (style.Background.A > 0)
		{
			drawList.AddRoundedRect(
				item.Bounds,
				style.CornerRadius,
				style.Background);
		}

		if (style.BorderWidth > 0f && style.Border.A > 0)
		{
			drawList.AddBorder(
				item.Bounds,
				style.Border,
				style.BorderWidth,
				style.CornerRadius);
		}

		float icon = theme.Metrics.IconSize;
		float inset = (item.Bounds.Width - icon) * 0.5f;

		if (item.Caption.Length == 0)
		{
			UiIconPainter.Draw(
				drawList,
				item.Icon,
				new UiRect(
					item.Bounds.X + MathF.Max(2f, inset),
					item.Bounds.Center.Y - (icon * 0.5f),
					icon,
					icon),
				style.Foreground,
				1.5f);

			return;
		}

		UiTextLayoutResult layout = UiTextLayout.Create(
			item.Caption,
			theme.Fonts.MonoSmall,
			MathF.Max(8f, item.Bounds.Width - 6f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Center);

		drawList.AddText(
			layout,
			new UiPoint(
				item.Bounds.Center.X - (layout.Width * 0.5f),
				item.Bounds.Center.Y - (layout.Height * 0.5f)),
			style.Foreground);
	}

	private static List<ViewportOverlayItem> BuildToolCluster(
		EditorSession session,
		UiMetrics metrics,
		float edge,
		bool isPlaying)
	{
		_ = metrics;

		List<ViewportOverlayItem> cluster = [];

		foreach (EditorTool tool in Enum.GetValues<EditorTool>())
		{
			cluster.Add(new ViewportOverlayItem(
				ViewportOverlayControl.Tool,
				tool,
				EditorToolInfo.Icon(tool),
				string.Empty,
				$"{EditorToolInfo.Label(tool)} tool ({EditorToolInfo.Shortcut(tool)})",
				new UiRect(0f, 0f, edge, edge),
				tool == session.Tool,
				!isPlaying));
		}

		return cluster;
	}

	private static List<ViewportOverlayItem> BuildDisplayCluster(
		EditorSession session,
		UiMetrics metrics,
		UiFont font,
		float edge,
		bool showGrid)
	{
		string step = session.SnapStep.ToString(
			"0.###",
			System.Globalization.CultureInfo.InvariantCulture);

		float stepWidth = MathF.Min(
			edge * 2f,
			MathF.Max(edge, font.MeasureText(step) + (metrics.Spacing * 2f)));

		return
		[
			new ViewportOverlayItem(
				ViewportOverlayControl.ToggleSpace,
				EditorTool.Translate,
				session.LocalTransform ? UiIcon.Local : UiIcon.World,
				string.Empty,
				session.LocalTransform
					? "Using local gizmo space — click for world"
					: "Using world gizmo space — click for local",
				new UiRect(0f, 0f, edge, edge),
				session.LocalTransform,
				true),
			new ViewportOverlayItem(
				ViewportOverlayControl.ToggleSnap,
				EditorTool.Translate,
				UiIcon.Snap,
				string.Empty,
				"Grid snapping",
				new UiRect(0f, 0f, edge, edge),
				session.GridSnapping,
				true),
			new ViewportOverlayItem(
				ViewportOverlayControl.CycleSnapStep,
				EditorTool.Translate,
				UiIcon.None,
				step,
				"Snap increment — click to cycle",
				new UiRect(0f, 0f, stepWidth, edge),
				false,
				true),
			new ViewportOverlayItem(
				ViewportOverlayControl.ToggleGrid,
				EditorTool.Translate,
				UiIcon.Grid,
				string.Empty,
				"Ground grid",
				new UiRect(0f, 0f, edge, edge),
				showGrid,
				true),
			new ViewportOverlayItem(
				ViewportOverlayControl.FrameSelection,
				EditorTool.Translate,
				UiIcon.Frame,
				string.Empty,
				"Frame the selection (F)",
				new UiRect(0f, 0f, edge, edge),
				false,
				true)
		];
	}

	/// <summary>
	/// Records the backing plate of the run of controls that ends at
	/// the current building position.
	/// </summary>
	/// <remarks>
	/// A plate covers one row of one cluster, so a cluster that wrapped
	/// reads as two clusters rather than one plate spanning the gap.
	/// </remarks>
	private void CloseRun(int start, float padding, UiRect bounds)
	{
		if (start < 0 || start >= _items.Count)
		{
			return;
		}

		float left = float.MaxValue;
		float top = float.MaxValue;
		float right = float.MinValue;
		float bottom = float.MinValue;

		for (int index = start; index < _items.Count; index++)
		{
			UiRect item = _items[index].Bounds;

			left = MathF.Min(left, item.X);
			top = MathF.Min(top, item.Y);
			right = MathF.Max(right, item.Right);
			bottom = MathF.Max(bottom, item.Bottom);
		}

		UiRect plate = new UiRect(
			left,
			top,
			right - left,
			bottom - top).Inset(-padding);

		_groups.Add(new UiRect(
			MathF.Max(bounds.X, plate.X),
			MathF.Max(bounds.Y, plate.Y),
			MathF.Max(0f, MathF.Min(bounds.Right, plate.Right) - MathF.Max(bounds.X, plate.X)),
			MathF.Max(0f, MathF.Min(bounds.Bottom, plate.Bottom) - MathF.Max(bounds.Y, plate.Y))));
	}
}

/// <summary>
/// Draws the hover tooltip the viewport chrome shows.
/// </summary>
internal static class ViewportTooltip
{
	/// <summary>
	/// The gap between a control and its tooltip.
	/// </summary>
	public const float Gap = 6f;

	/// <summary>
	/// Paints a tooltip under an anchor, flipped above it and clamped
	/// when it would leave the viewport.
	/// </summary>
	/// <param name="drawList">The frame's draw list.</param>
	/// <param name="theme">The active theme.</param>
	/// <param name="anchor">
	/// The rectangle the tooltip describes. An empty anchor paints
	/// nothing.
	/// </param>
	/// <param name="bounds">The viewport rectangle.</param>
	/// <param name="text">The tooltip text.</param>
	public static void Draw(
		UiDrawList drawList,
		UiTheme theme,
		UiRect anchor,
		UiRect bounds,
		string text)
	{
		ArgumentNullException.ThrowIfNull(drawList);
		ArgumentNullException.ThrowIfNull(theme);

		if (anchor.IsEmpty || string.IsNullOrEmpty(text) || bounds.Width <= 20f)
		{
			return;
		}

		const float padding = 6f;

		UiTextLayoutResult layout = UiTextLayout.Create(
			text,
			theme.Fonts.Small,
			MathF.Max(40f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		float width = MathF.Min(
			bounds.Width - 4f,
			layout.Width + (padding * 2f));

		float height = layout.Height + (padding * 1.4f);

		float x = Math.Clamp(
			anchor.X,
			bounds.X + 2f,
			MathF.Max(bounds.X + 2f, bounds.Right - width - 2f));

		float y = anchor.Bottom + Gap;

		if (y + height > bounds.Bottom - 2f)
		{
			y = MathF.Max(bounds.Y + 2f, anchor.Y - height - Gap);
		}

		UiRect box = new(x, y, width, height);

		drawList.AddRoundedRect(
			box,
			theme.Metrics.SmallCornerRadius,
			theme.Palette.SurfaceOverlay);

		drawList.AddBorder(
			box,
			theme.Palette.BorderStrong,
			theme.Metrics.BorderWidth,
			theme.Metrics.SmallCornerRadius);

		drawList.AddText(
			layout,
			new UiPoint(box.X + padding, box.Y + (padding * 0.7f)),
			theme.Palette.Text);
	}
}
