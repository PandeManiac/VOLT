namespace Volt.Ui.Widgets;

/// <summary>
/// A container that arranges children at explicitly stored slots.
/// </summary>
/// <remarks>
/// The canvas itself fills its available space and is transparent to
/// hit testing by default; its children remain interactive. It is the
/// overlay primitive used by popups, tooltips, and viewport overlays.
/// Slots are relative to the canvas content area and are not affected
/// by child alignment.
/// </remarks>
public class CanvasPanel : Widget
{
	private readonly Dictionary<Widget, UiRect> _slots = [];

	public CanvasPanel()
	{
		IsHitTestVisible = false;
		ClipToBounds = true;
	}

	public void SetSlot(Widget child, UiRect slot)
	{
		ArgumentNullException.ThrowIfNull(child);
		_slots[child] = slot;
		InvalidateArrange();
	}

	public UiRect GetSlot(Widget child)
	{
		ArgumentNullException.ThrowIfNull(child);

		return _slots.TryGetValue(child, out UiRect slot)
			? slot
			: UiRect.Empty;
	}

	public bool RemoveSlot(Widget child)
	{
		ArgumentNullException.ThrowIfNull(child);

		bool removed = _slots.Remove(child);
		InvalidateArrange();

		return removed;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!_slots.TryGetValue(child, out UiRect slot))
			{
				continue;
			}

			MeasureChild(
				child,
				new UiSize(slot.Width, slot.Height));
		}

		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: 0f,
			float.IsFinite(contentAvailable.Height)
				? contentAvailable.Height
				: 0f);
	}

	protected override void ArrangeContent(UiRect contentBounds)
	{
		for (int index = 0; index < Children.Count; index++)
		{
			Widget child = Children[index];

			if (!_slots.TryGetValue(child, out UiRect slot))
			{
				continue;
			}

			ArrangeChildExact(
				child,
				new UiRect(
					contentBounds.X + slot.X,
					contentBounds.Y + slot.Y,
					slot.Width,
					slot.Height));
		}
	}

	protected override void OnDetached()
	{
		_slots.Clear();
	}
}
