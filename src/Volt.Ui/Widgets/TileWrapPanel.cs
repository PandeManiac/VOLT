using Volt.Ui.Layout;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// Arranges fixed-size children into rows that wrap to the width.
/// </summary>
/// <remarks>
/// The panel is the grid behind the project browser's tile view. It
/// measures every child once at the tile size, then lays them out at
/// uniform cells, so a directory with many assets scrolls as one
/// document instead of growing rows without bound. Children keep
/// their own content; the panel only decides where each cell sits.
/// </remarks>
public sealed class TileWrapPanel : Widget
{
	private float _tileWidth;
	private float _tileHeight;
	private float _spacing;
	private float _columns = 1f;

	public TileWrapPanel()
	{
		ClipToBounds = true;
	}

	/// <summary>
	/// Gets or sets the cell width. Zero uses the theme tile width.
	/// </summary>
	public float TileWidth
	{
		get => _tileWidth;
		set => _tileWidth = MathF.Max(1f, value);
	}

	public float TileHeight
	{
		get => _tileHeight;
		set => _tileHeight = MathF.Max(1f, value);
	}

	public float Spacing
	{
		get => _spacing;
		set => _spacing = MathF.Max(0f, value);
	}

	/// <summary>
	/// Gets the number of columns the last arrangement used.
	/// </summary>
	public int Columns => (int)_columns;

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float tileWidth = _tileWidth > 0f
			? _tileWidth
			: theme.Metrics.TileWidth;

		float tileHeight = _tileHeight > 0f
			? _tileHeight
			: theme.Metrics.TileHeight;

		float spacing = _spacing > 0f
			? _spacing
			: theme.Metrics.TightSpacing;

		for (int index = 0; index < Children.Count; index++)
		{
			MeasureChild(
				Children[index],
				new UiSize(tileWidth, tileHeight));
		}

		if (!float.IsFinite(contentAvailable.Width))
		{
			return new UiSize(
				MathF.Max(tileWidth, Children.Count * tileWidth),
				0f);
		}

		float usable = MathF.Max(0f, contentAvailable.Width - Padding.Horizontal);
		int columns = Math.Max(
			1,
			(int)((usable + spacing) / (tileWidth + spacing)));

		int rows = Children.Count == 0
			? 0
			: (Children.Count + columns - 1) / columns;

		float height = rows == 0
			? 0f
			: (rows * tileHeight) + ((rows - 1) * spacing);

		return new UiSize(
			contentAvailable.Width,
			height + Padding.Vertical);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float tileWidth = _tileWidth > 0f
			? _tileWidth
			: theme.Metrics.TileWidth;

		float tileHeight = _tileHeight > 0f
			? _tileHeight
			: theme.Metrics.TileHeight;

		float spacing = _spacing > 0f
			? _spacing
			: theme.Metrics.TightSpacing;

		int columns = Math.Max(
			1,
			(int)((contentBounds.Width + spacing) / (tileWidth + spacing)));

		_columns = columns;

		for (int index = 0; index < Children.Count; index++)
		{
			int column = index % columns;
			int row = index / columns;

			ArrangeChildExact(
				Children[index],
				new UiRect(
					contentBounds.X + (column * (tileWidth + spacing)),
					contentBounds.Y + (row * (tileHeight + spacing)),
					tileWidth,
					tileHeight));
		}
	}
}
