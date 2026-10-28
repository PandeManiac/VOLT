using Volt.Ui.Layout;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Layout;

public sealed class LabelLayoutTests
{
	[Fact]
	public void WrappedLabel_ProducesMultipleLines()
	{
		using UiTestHost host = new(320f, 200f);
		StackPanel stack = host.Add(new StackPanel());
		Label label = stack.Add(
			new Label(
				"The quick brown fox jumps over the lazy dog " +
				"while the editor lays out a narrow panel.")
			{
				Wrap = UiTextWrap.Wrap,
				Width = UiLength.Points(100f)
			});

		host.Layout();

		Assert.True(label.Bounds.Height > label.FontHeightForTests());
		Assert.True(label.Bounds.Width <= 100f);
	}

	[Fact]
	public void LabelWithPadding_AccountsForContentBox()
	{
		using UiTestHost host = new(300f, 100f);
		StackPanel stack = host.Add(new StackPanel());
		Label label = stack.Add(
			new Label("Hello")
			{
				Padding = new UiThickness(5f, 3f)
			});

		host.Layout();

		// ContentBounds is relative to the widget's own outer bounds,
		// so padding alone moves it. The label's position inside its
		// parent must not leak into it.
		Assert.Equal(5f, label.ContentBounds.X);
		Assert.Equal(3f, label.ContentBounds.Y);
		Assert.Equal(
			label.VisualBounds.Width - 10f,
			label.ContentBounds.Width);
	}

	[Fact]
	public void ZeroSizedSurface_DoesNotThrowAndCollapsesLayout()
	{
		using UiTestHost host = new(0f, 0f);
		StackPanel stack = host.Add(new StackPanel());
		Label label = stack.Add(new Label("Hello"));

		host.Layout(0f, 0f);

		Assert.Equal(0f, stack.Bounds.Width);
		Assert.Equal(0f, label.Bounds.Width);
		Assert.Equal(0, host.Context.Draw().Count);
	}

	[Fact]
	public void FontAtDoubleDpi_PreservesLogicalLineHeight()
	{
		UiFont normal = new(UiFontKind.Sans, 13f, 1f);
		UiFont hidpi = new(UiFontKind.Sans, 13f, 2f);

		Assert.Equal(2f, hidpi.DpiScale);
		Assert.InRange(
			hidpi.LineHeight,
			normal.LineHeight - 1f,
			normal.LineHeight + 1f);
		Assert.True(hidpi.GetGlyph('A').Size.Width > 0f);
	}
}

internal static class LabelTestExtensions
{
	public static float FontHeightForTests(this Label label)
	{
		return label.Context!.Theme.Fonts.Body.LineHeight;
	}
}
