using Volt.Ui.Widgets;
using Volt.Ui.Widgets.Properties;

namespace Volt.Ui.Tests.Widgets;

public sealed class PropertyGridTests
{
	[Fact]
	public void RefreshingEnumValue_DoesNotWriteBackToTheModel()
	{
		using UiTestHost host = new();
		PropertyGrid grid = host.Add(new PropertyGrid());
		int selected = 0;
		int writes = 0;

		grid.SetProperties(
		[
			new EnumProperty(
				"Mode",
				["First", "Second"],
				() => selected,
				value =>
				{
					selected = value;
					writes++;
				})
		]);

		Dropdown dropdown = Descendants(grid).OfType<Dropdown>().Single();
		Assert.Equal(0, dropdown.SelectedIndex);
		Assert.Equal(0, writes);

		selected = 1;
		grid.RefreshValues();

		Assert.Equal(1, dropdown.SelectedIndex);
		Assert.Equal(1, selected);
		Assert.Equal(0, writes);
	}

	private static IEnumerable<Widget> Descendants(Widget root)
	{
		foreach (Widget child in root.Children)
		{
			yield return child;
			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}
}
