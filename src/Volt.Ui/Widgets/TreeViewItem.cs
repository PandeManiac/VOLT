using Volt.Ui.Rendering;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A node in a <see cref="TreeView"/>.
/// </summary>
/// <remarks>
/// Items are plain model objects, not widgets. A tree with thousands
/// of rows still has only the visible rows painted; each item caches
/// its own text layout.
/// </remarks>
public sealed class TreeViewItem
{
	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;

	public TreeViewItem(string id, string text, object? tag = null)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException(
				"A tree item requires a stable identifier.",
				nameof(id));
		}

		Id = id;
		Text = text ?? string.Empty;
		Tag = tag;
	}

	public string Id { get; }

	public string Text { get; set; }

	public object? Tag { get; set; }

	public UiColor? IconColor { get; set; }

	/// <summary>
	/// Gets or sets the vector icon shown before the label. When set,
	/// it replaces the plain kind dot.
	/// </summary>
	public UiIcon Icon { get; set; }

	public bool IsExpanded { get; set; }

	public bool IsSelected { get; internal set; }

	public TreeViewItem? Parent { get; internal set; }

	public List<TreeViewItem> Children { get; } = [];

	public bool HasChildren => Children.Count > 0;

	public int Depth { get; internal set; }

	public TreeViewItem AddChild(TreeViewItem child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (child == this)
		{
			throw new ArgumentException(
				"An item cannot be its own child.",
				nameof(child));
		}

		for (TreeViewItem? current = this;
			current is not null;
			current = current.Parent)
		{
			if (current == child)
			{
				throw new InvalidOperationException(
					"The operation would create a cycle.");
			}
		}

		if (child.Parent is not null)
		{
			child.Parent.Children.Remove(child);
		}

		child.Parent = this;
		Children.Add(child);

		return child;
	}

	public void RemoveFromParent()
	{
		if (Parent is null)
		{
			return;
		}

		Parent.Children.Remove(this);
		Parent = null;
	}

	internal UiTextLayoutResult GetLayout(UiFont font, float width)
	{
		width = MathF.Max(0.001f, width);

		if (_layout is not null &&
			_layoutText == Text &&
			_layoutFont == font)
		{
			return _layout;
		}

		_layout = UiTextLayout.Create(
			Text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		_layoutText = Text;
		_layoutFont = font;

		return _layout;
	}

	internal void InvalidateLayout()
	{
		_layout = null;
	}
}
