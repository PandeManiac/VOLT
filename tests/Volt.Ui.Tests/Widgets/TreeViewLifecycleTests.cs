using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Widgets;

public sealed class TreeViewLifecycleTests
{
	[Fact]
	public void Clear_ResetsItemStateAndNotifiesSelectionObservers()
	{
		TreeView tree = new();
		TreeViewItem root = new("root", "Root");
		TreeViewItem child = root.AddChild(new TreeViewItem("child", "Child"));
		tree.AddRoot(root);
		tree.SetSelection([child]);
		int notifications = 0;
		tree.SelectionChanged += (_, selection) =>
		{
			notifications++;
			Assert.Empty(selection);
		};

		tree.Clear();

		Assert.Empty(tree.Roots);
		Assert.Empty(tree.SelectedItems);
		Assert.False(child.IsSelected);
		Assert.Equal(1, notifications);
	}

	[Fact]
	public void RemoveRoot_ClearsSelectionFromItsSubtree()
	{
		TreeView tree = new();
		TreeViewItem removed = new("removed", "Removed");
		TreeViewItem child = removed.AddChild(new TreeViewItem("child", "Child"));
		TreeViewItem retained = new("retained", "Retained");
		tree.AddRoot(removed);
		tree.AddRoot(retained);
		tree.SetSelection([child, retained]);
		int notifications = 0;
		tree.SelectionChanged += (_, selection) =>
		{
			notifications++;
			Assert.Equal([retained], selection);
		};

		Assert.True(tree.RemoveRoot(removed));

		Assert.False(child.IsSelected);
		Assert.True(retained.IsSelected);
		Assert.Equal([retained], tree.SelectedItems);
		Assert.Equal(1, notifications);
	}

	[Fact]
	public void RemoveRoot_WhenItemIsNotARootDoesNotDetachItFromItsParent()
	{
		TreeView tree = new();
		TreeViewItem root = new("root", "Root");
		TreeViewItem child = root.AddChild(new TreeViewItem("child", "Child"));
		tree.AddRoot(root);

		Assert.False(tree.RemoveRoot(child));

		Assert.Same(root, child.Parent);
		Assert.Contains(child, root.Children);
	}
}
