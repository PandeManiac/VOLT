using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class ScrollViewTests
{
	[Fact]
	public void Wheel_ScrollsAndClampsToContent()
	{
		using UiTestHost host = new(400f, 300f);
		StackPanel rows = new();

		for (int index = 0; index < 30; index++)
		{
			rows.Add(new Panel
			{
				Height = UiLength.Points(20f),
				DrawBackground = false
			});
		}

		ScrollView scroll = host.Add(
			new ScrollView
			{
				Width = UiLength.Points(200f),
				Height = UiLength.Points(100f),
				HorizontalAlignment = UiAlignment.Start,
				VerticalAlignment = UiAlignment.Start,
				Content = rows
			});

		host.Layout();

		Assert.True(scroll.MaximumScroll > 0f);

		Assert.True(host.Context.Wheel(
			new UiPoint(0f, -1f),
			scroll.GlobalContentBounds.Center));

		Assert.True(scroll.ScrollOffset.Y > 0f);

		host.Context.Wheel(
			new UiPoint(0f, -1000f),
			scroll.GlobalContentBounds.Center);

		Assert.Equal(scroll.MaximumScroll, scroll.ScrollOffset.Y, 3);

		host.Context.Wheel(
			new UiPoint(0f, 1000f),
			scroll.GlobalContentBounds.Center);

		Assert.Equal(0f, scroll.ScrollOffset.Y, 3);
	}

	[Fact]
	public void ContentShrink_ClampsExistingOffsetAfterLayout()
	{
		using UiTestHost host = new(400f, 300f);
		StackPanel rows = new();

		for (int index = 0; index < 20; index++)
		{
			rows.Add(new Panel
			{
				Height = UiLength.Points(20f)
			});
		}

		ScrollView scroll = host.Add(
			new ScrollView
			{
				Width = UiLength.Points(200f),
				Height = UiLength.Points(100f),
				HorizontalAlignment = UiAlignment.Start,
				Content = rows
			});

		host.Layout();
		scroll.SetScrollOffset(200f);

		rows.ClearChildren();
		host.Layout();

		Assert.Equal(0f, scroll.MaximumScroll);
		Assert.Equal(0f, scroll.ScrollOffset.Y);
	}
}
