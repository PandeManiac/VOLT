using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class ColorFieldTests
{
	[Fact]
	public void EditingRgbHex_PreservesExistingAlpha()
	{
		using UiTestHost host = new();
		ColorField field = host.Add(new ColorField());
		field.Value = new UiColor(255, 0, 0, 64);
		TextField hex = field.Children.OfType<TextField>().Single();
		Assert.Equal("#FF0000", hex.Text);

		Assert.True(hex.Focus());
		hex.Text = "#00FF00";
		hex.Commit();

		Assert.Equal(new UiColor(0, 255, 0, 64), field.Value);
	}
}
