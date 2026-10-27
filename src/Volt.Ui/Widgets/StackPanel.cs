using Volt.Ui.Layout;

namespace Volt.Ui.Widgets;

/// <summary>
/// Stacks children horizontally or vertically with spacing, flexible
/// growth, and shrinking.
/// </summary>
/// <remarks>
/// The content size along the main axis is the sum of child desired
/// sizes plus spacing. During arrangement, leftover main-axis space
/// is distributed to children by <see cref="Widget.FlexGrow"/>; a
/// deficit is taken from children with <see cref="Widget.FlexShrink"/>
/// while respecting their minimum dimensions. The cross axis uses the
/// child's alignment.
/// </remarks>
public class StackPanel : Widget
{
	private readonly List<float> _mainSizes = [];

	private UiOrientation _orientation;

	public StackPanel()
		: this(UiOrientation.Vertical)
	{
	}

	public StackPanel(UiOrientation orientation)
	{
		_orientation = orientation;
	}

	public UiOrientation Orientation
	{
		get => _orientation;
		set
		{
			if (_orientation == value)
			{
				return;
			}

			_orientation = value;
			InvalidateMeasure();
		}
	}

	public float Spacing { get; set; }

	private bool IsHorizontal => _orientation == UiOrientation.Horizontal;

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		float main = 0f;
		float cross = 0f;
		int visible = 0;

		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!child.IsVisible)
			{
				continue;
			}

			UiSize childAvailable = contentAvailable;

			// A flexible child with an automatic main-axis size must not
			// claim all available space during measurement; its share is
			// assigned during arrangement.
			if (child.FlexGrow > 0f)
			{
				if (IsHorizontal && child.Width.IsAuto)
				{
					childAvailable = childAvailable with { Width = 0f };
				}

				else if (!IsHorizontal && child.Height.IsAuto)
				{
					childAvailable = childAvailable with { Height = 0f };
				}
			}

			UiSize desired = MeasureChild(child, childAvailable);

			main += IsHorizontal
				? desired.Width
				: desired.Height;

			cross = MathF.Max(
				cross,
				IsHorizontal ? desired.Height : desired.Width);

			visible++;
		}

		if (visible > 1)
		{
			main += Spacing * (visible - 1);
		}

		return IsHorizontal
			? new UiSize(main, cross)
			: new UiSize(cross, main);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		_mainSizes.Clear();

		int visible = 0;

		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!child.IsVisible)
			{
				continue;
			}

			_mainSizes.Add(
				IsHorizontal
					? child.DesiredSize.Width
					: child.DesiredSize.Height);

			visible++;
		}

		if (visible == 0)
		{
			return;
		}

		float available = IsHorizontal
			? contentBounds.Width
			: contentBounds.Height;

		float totalSpacing = Spacing * MathF.Max(0, visible - 1);
		float total = totalSpacing;

		for (int index = 0; index < _mainSizes.Count; index++)
		{
			total += _mainSizes[index];
		}

		float flexGrow = 0f;
		float flexShrink = 0f;

		int visibleIndex = 0;

		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!child.IsVisible)
			{
				continue;
			}

			flexGrow += child.FlexGrow;
			flexShrink += child.FlexShrink;
			visibleIndex++;
		}

		if (total < available && flexGrow > 0f)
		{
			float extra = available - total;
			visibleIndex = 0;

			for (int index = 0; index < Children.Count; index++)
			{
				Widget child = Children[index];

				if (!child.IsVisible)
				{
					continue;
				}

				if (child.FlexGrow > 0f)
				{
					_mainSizes[visibleIndex] +=
						extra * (child.FlexGrow / flexGrow);
				}

				visibleIndex++;
			}
		}

		else if (total > available && flexShrink > 0f)
		{
			float deficit = total - available;
			float remaining = deficit;

			// Shrink proportionally to the requested weight. Minimum
			// sizes act as a floor, so a second pass can redistribute
			// whatever a floored child could not give back.
			for (int pass = 0; pass < 2 && remaining > 0f; pass++)
			{
				float capacity = 0f;
				visibleIndex = 0;

				for (int index = 0; index < Children.Count; index++)
				{
					Widget child = Children[index];

					if (!child.IsVisible)
					{
						continue;
					}

					if (child.FlexShrink > 0f)
					{
						capacity += MathF.Max(
							0f,
							_mainSizes[visibleIndex] -
								MinimumMain(child));
					}

					visibleIndex++;
				}

				if (capacity <= 0f)
				{
					break;
				}

				float take = MathF.Min(remaining, capacity);
				visibleIndex = 0;

				for (int index = 0; index < Children.Count; index++)
				{
					Widget child = Children[index];

					if (!child.IsVisible)
					{
						continue;
					}

					if (child.FlexShrink > 0f)
					{
						float slack = MathF.Max(
							0f,
							_mainSizes[visibleIndex] -
								MinimumMain(child));

						_mainSizes[visibleIndex] -=
							take * (slack / capacity);
					}

					visibleIndex++;
				}

				remaining -= take;
			}
		}

		float position = 0f;
		visibleIndex = 0;

		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!child.IsVisible)
			{
				continue;
			}

			float size = MathF.Max(0f, _mainSizes[visibleIndex++]);

			UiRect slot = IsHorizontal
				? new UiRect(
					contentBounds.X + position,
					contentBounds.Y,
					size,
					contentBounds.Height)
				: new UiRect(
					contentBounds.X,
					contentBounds.Y + position,
					contentBounds.Width,
					size);

			ArrangeChildCore(
				child,
				slot,
				forceWidth: IsHorizontal,
				forceHeight: !IsHorizontal);

			position += size + Spacing;
		}
	}

	private float MinimumMain(Widget child)
	{
		return IsHorizontal
			? child.MinWidth + child.Margin.Horizontal
			: child.MinHeight + child.Margin.Vertical;
	}
}
