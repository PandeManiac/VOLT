namespace Volt.Ui.Widgets;

/// <summary>
/// An invisible flexible gap.
/// </summary>
public sealed class Spacer : Widget
{
	public Spacer(float grow = 1f)
	{
		FlexGrow = grow;
	}
}
