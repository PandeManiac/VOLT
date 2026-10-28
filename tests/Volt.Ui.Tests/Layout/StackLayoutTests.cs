using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Layout;

public sealed class StackLayoutTests
{
	[Fact]
	public void HorizontalStack_ArrangesFixedChildrenWithSpacing()
	{
		using UiTestHost host = new(400f, 200f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Horizontal)
			{
				Spacing = 5f,
				ClipToBounds = true
			});

		Panel first = stack.Add(
			new Panel
			{
				Width = UiLength.Points(100f),
				Height = UiLength.Points(30f),
				HorizontalAlignment = UiAlignment.Start,
				VerticalAlignment = UiAlignment.Start
			});

		Panel second = stack.Add(
			new Panel
			{
				Width = UiLength.Points(50f),
				Height = UiLength.Points(20f),
				HorizontalAlignment = UiAlignment.Start,
				VerticalAlignment = UiAlignment.Start
			});

		host.Layout();

		Assert.Equal(100f, first.Bounds.Width);
		Assert.Equal(50f, second.Bounds.Width);
		Assert.Equal(105f, second.Bounds.X);
	}

	[Fact]
	public void VerticalStack_ArrangesChildrenAtContentOrigin()
	{
		using UiTestHost host = new(300f, 300f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Vertical)
			{
				Spacing = 4f,
				Padding = new UiThickness(10f),
				Margin = new UiThickness(5f)
			});

		Panel child = new()
		{
			Width = UiLength.Points(100f),
			Height = UiLength.Points(20f),
			HorizontalAlignment = UiAlignment.Start,
			VerticalAlignment = UiAlignment.Start
		};

		stack.Add(child);
		host.Layout();

		Assert.Equal(15f, child.GlobalBounds.X);
		Assert.Equal(15f, child.GlobalBounds.Y);
	}

	[Fact]
	public void FlexGrow_DistributesLeftoverMainAxisSpace()
	{
		using UiTestHost host = new(300f, 100f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Horizontal));

		Panel fixedChild = stack.Add(
			new Panel
			{
				Width = UiLength.Points(100f)
			}) as Panel ?? throw new InvalidOperationException();

		Panel flexible = stack.Add(
			new Panel
			{
				FlexGrow = 1f
			});

		host.Layout();

		Assert.Equal(100f, fixedChild.Bounds.Width);
		Assert.Equal(200f, flexible.Bounds.Width);
		Assert.Equal(100f, flexible.Bounds.X);
	}

	[Fact]
	public void FlexShrink_RespectsMinimumSize()
	{
		using UiTestHost host = new(100f, 100f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Horizontal));

		Panel a = stack.Add(
			new Panel
			{
				Width = UiLength.Points(100f),
				MinWidth = 30f,
				FlexShrink = 1f
			});

		Panel b = stack.Add(
			new Panel
			{
				Width = UiLength.Points(100f),
				MinWidth = 30f,
				FlexShrink = 1f
			});

		host.Layout(40f);

		Assert.Equal(30f, a.Bounds.Width);
		Assert.Equal(30f, b.Bounds.Width);
	}

	[Fact]
	public void PercentSizes_ResolveAgainstAvailableSpace()
	{
		using UiTestHost host = new(400f, 200f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Horizontal));

		Panel half = stack.Add(
			new Panel
			{
				Width = UiLength.Percent(0.5f),
				Height = UiLength.Percent(0.25f)
			});

		host.Layout();

		Assert.Equal(200f, half.Bounds.Width);
		Assert.Equal(50f, half.Bounds.Height);
	}

	[Fact]
	public void MinAndMaxWidth_ClampResolvedSize()
	{
		using UiTestHost host = new(400f, 200f);
		StackPanel stack = host.Add(
			new StackPanel(UiOrientation.Horizontal));

		Panel child = stack.Add(
			new Panel
			{
				Width = UiLength.Points(10f),
				MinWidth = 80f,
				MaxWidth = 120f
			});

		host.Layout();

		Assert.Equal(80f, child.Bounds.Width);
	}
}
