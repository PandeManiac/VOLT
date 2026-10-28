using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;

namespace Volt.Ui.Docking;

/// <summary>
/// Visualizes one <see cref="DockSplitNode"/> and owns the splitter
/// between its two child views.
/// </summary>
internal sealed class DockSplitView : Widget
{
	private readonly DockSplitNode _node;
	private readonly Widget _first;
	private readonly Widget _second;
	private readonly Splitter _splitter;

	public DockSplitView(
		DockSplitNode node,
		Widget first,
		Widget second)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);

		_node = node;
		_first = first;
		_second = second;
		_splitter = new Splitter(node.Orientation)
		{
			Width = UiLength.Auto,
			Height = UiLength.Auto
		};

		_splitter.Dragged += OnSplitterDragged;

		Add(first);
		Add(_splitter);
		Add(second);
	}

	internal DockSplitNode Node => _node;

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float thickness = theme.Metrics.DockSplitterThickness;

		float width = float.IsFinite(contentAvailable.Width)
			? contentAvailable.Width
			: 320f;

		float height = float.IsFinite(contentAvailable.Height)
			? contentAvailable.Height
			: 240f;

		if (_node.Orientation == UiOrientation.Horizontal)
		{
			float childWidth = MathF.Max(0f, (width - thickness) * 0.5f);
			MeasureChild(_first, new UiSize(childWidth, height));
			MeasureChild(_second, new UiSize(childWidth, height));
		}

		else
		{
			float childHeight = MathF.Max(0f, (height - thickness) * 0.5f);
			MeasureChild(_first, new UiSize(width, childHeight));
			MeasureChild(_second, new UiSize(width, childHeight));
		}

		return new UiSize(width, height);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		float thickness = MathF.Min(
			theme.Metrics.DockSplitterThickness,
			_node.Orientation == UiOrientation.Horizontal
				? contentBounds.Width
				: contentBounds.Height);

		if (_node.Orientation == UiOrientation.Horizontal)
		{
			float usable = MathF.Max(0f, contentBounds.Width - thickness);
			float firstWidth = usable * _node.Ratio;
			float secondWidth = usable - firstWidth;

			ArrangeChildExact(
				_first,
				new UiRect(
					contentBounds.X,
					contentBounds.Y,
					firstWidth,
					contentBounds.Height));

			ArrangeChildExact(
				_splitter,
				new UiRect(
					contentBounds.X + firstWidth,
					contentBounds.Y,
					thickness,
					contentBounds.Height));

			ArrangeChildExact(
				_second,
				new UiRect(
					contentBounds.X + firstWidth + thickness,
					contentBounds.Y,
					secondWidth,
					contentBounds.Height));
		}

		else
		{
			float usable = MathF.Max(0f, contentBounds.Height - thickness);
			float firstHeight = usable * _node.Ratio;
			float secondHeight = usable - firstHeight;

			ArrangeChildExact(
				_first,
				new UiRect(
					contentBounds.X,
					contentBounds.Y,
					contentBounds.Width,
					firstHeight));

			ArrangeChildExact(
				_splitter,
				new UiRect(
					contentBounds.X,
					contentBounds.Y + firstHeight,
					contentBounds.Width,
					thickness));

			ArrangeChildExact(
				_second,
				new UiRect(
					contentBounds.X,
					contentBounds.Y + firstHeight + thickness,
					contentBounds.Width,
					secondHeight));
		}
	}

	private void OnSplitterDragged(Splitter splitter, float delta)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalVisualBounds;

		float length = _node.Orientation == UiOrientation.Horizontal
			? bounds.Width
			: bounds.Height;

		float usable = MathF.Max(
			1f,
			length - theme.Metrics.DockSplitterThickness);

		_node.Ratio += delta / usable;
		InvalidateArrange();
	}
}
