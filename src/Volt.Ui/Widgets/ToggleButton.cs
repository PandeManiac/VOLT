using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// A button that holds a checked state.
/// </summary>
public class ToggleButton : Button
{
	private bool _isChecked;

	public ToggleButton()
	{
	}

	public ToggleButton(string text)
		: base(text)
	{
	}

	public event Action<ToggleButton>? Toggled;

	public bool IsChecked
	{
		get => _isChecked;
		set
		{
			if (_isChecked == value)
			{
				return;
			}

			_isChecked = value;
			IsSelected = value;
			InvalidateVisual();
		}
	}

	protected override UiStyle ResolveStyle(UiWidgetState state)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		return theme.GetButtonStyle(state, _isChecked);
	}

	protected internal override void OnClick(int clickCount)
	{
		IsChecked = !IsChecked;
		Toggled?.Invoke(this);
		base.OnClick(clickCount);
	}
}
