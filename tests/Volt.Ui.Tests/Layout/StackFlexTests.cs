using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Layout;

/// <summary>
/// Verifies how a stack distributes leftover main-axis space: only
/// children that declare <see cref="Widget.FlexGrow"/> absorb it, and
/// the remaining children stay inside the stack's own bounds.
/// </summary>
/// <remarks>
/// This is the contract the editor shell depends on. A container that
/// fills the space it is given (a <see cref="Panel"/>) reports that
/// space as its desired size, so putting one in a stack without
/// declaring it flexible makes the stack's content taller than the
/// stack and places every later child outside it.
/// </remarks>
public sealed class StackFlexTests
{
	[Fact]
	public void FixedFlexibleFixedRows_KeepTheTrailingRowAtTheBottomEdge()
	{
		using UiTestHost host = new(800f, 600f);
		StackPanel stack = host.Add(new StackPanel { Spacing = 0f });

		Panel header = stack.Add(new Panel { Height = UiLength.Points(28f) });
		Panel middle = stack.Add(new Panel { FlexGrow = 1f });
		Panel footer = stack.Add(new Panel { Height = UiLength.Points(26f) });

		host.Layout();

		Assert.Equal(0f, header.GlobalVisualBounds.Y, 2);
		Assert.Equal(28f, header.GlobalVisualBounds.Height, 2);

		Assert.Equal(28f, middle.GlobalVisualBounds.Y, 2);
		Assert.Equal(574f, middle.GlobalVisualBounds.Bottom, 2);

		Assert.Equal(574f, footer.GlobalVisualBounds.Y, 2);
		Assert.Equal(600f, footer.GlobalVisualBounds.Bottom, 2);
	}

	[Fact]
	public void NestedFlexibleContent_StaysInsideTheSurface()
	{
		// The shell nests two stacks: the outer one holds the toolbar
		// and the dock area, and the dock area holds the flexible dock
		// host. Both levels must resolve to the surface height.
		using UiTestHost host = new(800f, 600f);
		StackPanel root = host.Add(new StackPanel { Spacing = 0f });

		Panel toolbar = root.Add(new Panel { Height = UiLength.Points(40f) });
		Panel workspace = root.Add(new Panel { FlexGrow = 1f });
		Panel footer = root.Add(new Panel { Height = UiLength.Points(26f) });

		StackPanel inner = workspace.Add(new StackPanel { Spacing = 0f });
		inner.Add(new Panel { Height = UiLength.Points(24f) });
		Panel dock = inner.Add(new Panel { FlexGrow = 1f });

		host.Layout();

		UiRect surface = new(0f, 0f, 800f, 600f);

		foreach (Widget widget in new Widget[] { toolbar, workspace, footer, dock })
		{
			UiRect bounds = widget.GlobalVisualBounds;

			Assert.False(
				bounds.Intersect(surface).IsEmpty,
				$"{widget.GetType().Name} must be arranged on the surface: " +
				$"{bounds.Y:0.#}..{bounds.Bottom:0.#}.");

			Assert.True(
				bounds.Bottom <= surface.Bottom + 0.01f,
				$"{widget.GetType().Name} must not extend past the " +
				$"surface: bottom={bounds.Bottom:0.#}.");
		}

		Assert.Equal(40f, toolbar.GlobalVisualBounds.Bottom, 2);
		Assert.Equal(40f, workspace.GlobalVisualBounds.Y, 2);
		Assert.Equal(574f, workspace.GlobalVisualBounds.Bottom, 2);
		Assert.Equal(600f, footer.GlobalVisualBounds.Bottom, 2);
		Assert.Equal(
			footer.GlobalVisualBounds.Y,
			dock.GlobalVisualBounds.Bottom,
			2);
	}

	[Fact]
	public void FlexibleChild_IsMeasuredWithoutClaimingTheWholeAxis()
	{
		// Measuring a flexible child against the full axis would make
		// the stack report a desired size larger than its surface even
		// when the layout fits.
		using UiTestHost host = new(400f, 300f);
		StackPanel stack = host.Add(new StackPanel { Spacing = 0f });

		stack.Add(new Panel { Height = UiLength.Points(20f) });
		stack.Add(new Panel { FlexGrow = 1f });

		host.Layout();

		Assert.True(
			stack.DesiredSize.Height <= 300f + 0.01f,
			$"A fitting stack must not desire more height than it was " +
			$"offered: desired={stack.DesiredSize.Height:0.#}.");
	}
}
