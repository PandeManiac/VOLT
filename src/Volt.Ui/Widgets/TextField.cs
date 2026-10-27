using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;

namespace Volt.Ui.Widgets;

/// <summary>
/// A single-line editable text field with selection, caret, clipboard,
/// and Unicode text input.
/// </summary>
/// <remarks>
/// Physical keys and committed text are separate: navigation and
/// editing commands arrive through key events, while characters
/// arrive through <see cref="UiContext.TextInput"/>. IME composition
/// is not inserted into the document by the current platform bridge;
/// the context exposes the composition preview separately.
/// </remarks>
public class TextField : Widget
{
	private readonly UiTextEditor _editor = new();

	private string _placeholder = string.Empty;
	private UiFont? _font;
	private bool _isReadOnly;
	private int _maximumLength = int.MaxValue;
	private bool _dragging;
	private float _scrollX;
	private string _textAtFocus = string.Empty;

	private UiTextLayoutResult? _layout;
	private string? _layoutText;
	private UiFont? _layoutFont;
	private float _layoutWidth = -1f;
	private UiTextWrap _layoutWrap;

	public TextField()
	{
		IsFocusable = true;
		Cursor = UiCursor.IBeam;
		Padding = new UiThickness(7f, 3f);
		MinHeight = 20f;
		ClipToBounds = true;
	}

	public TextField(string text)
		: this()
	{
		Text = text;
	}

	public event Action<TextField>? TextChanged;

	public event Action<TextField>? EditingEnded;

	public string Text
	{
		get => _editor.Text;
		set
		{
			if (_editor.Text == value)
			{
				return;
			}

			_editor.SetText(value ?? string.Empty);
			_layout = null;
			InvalidateVisual();
		}
	}

	public string Placeholder
	{
		get => _placeholder;
		set => _placeholder = value ?? string.Empty;
	}

	public UiFont? Font
	{
		get => _font;
		set
		{
			if (_font == value)
			{
				return;
			}

			_font = value;
			_layout = null;
			InvalidateMeasure();
		}
	}

	public bool IsReadOnly
	{
		get => _isReadOnly;
		set => _isReadOnly = value;
	}

	public int MaximumLength
	{
		get => _maximumLength;
		set => _maximumLength = Math.Max(0, value);
	}

	public int CaretIndex => _editor.CaretIndex;

	public int SelectionStart => _editor.SelectionStart;

	public int SelectionEnd => _editor.SelectionEnd;

	public bool HasSelection => _editor.HasSelection;

	public void SelectAll()
	{
		_editor.SelectAll();
		InvalidateVisual();
	}

	public void SetSelection(int anchor, int caret)
	{
		_editor.SetCaret(anchor);
		_editor.SetCaret(caret, extendSelection: true);
		InvalidateVisual();
	}

	protected virtual UiFont ResolveFont()
	{
		if (_font is not null)
		{
			return _font;
		}

		if (Context is not UiContext context)
		{
			throw new InvalidOperationException(
				"The text field is not attached to a UI context.");
		}

		return DefaultFont(context.Theme);
	}

	protected virtual UiFont DefaultFont(UiTheme theme)
	{
		return theme.Fonts.Body;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		UiFont font = ResolveFont();
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		float minimumHeight = MathF.Max(
			0f,
			theme.Metrics.FieldHeight - Padding.Vertical);

		return new UiSize(
			160f,
			MathF.Max(font.LineHeight, minimumHeight));
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiStyle style = theme.GetTextFieldStyle(
			this.GetVisualState(),
			HasFocus);

		drawList.AddRoundedRect(
			GlobalVisualBounds,
			style.CornerRadius,
			style.Background);

		if (style.BorderWidth > 0f)
		{
			drawList.AddBorder(
				GlobalVisualBounds,
				style.Border,
				style.BorderWidth,
				style.CornerRadius);
		}

		UiFont font = ResolveFont();
		UiRect content = GlobalContentBounds;

		if (content.IsEmpty)
		{
			return;
		}

		bool showPlaceholder = Text.Length == 0 && !HasFocus;
		string displayText = showPlaceholder ? Placeholder : Text;
		UiTextLayoutResult layout = EnsureLayout(
			font,
			displayText,
			content.Width);

		float textHeight = layout.Height;
		float originY = content.Y +
			MathF.Max(0f, (content.Height - textHeight) * 0.5f);
		float originX = content.X - _scrollX;

		drawList.PushClip(content);

		try
		{
			if (!showPlaceholder && HasSelection)
			{
				DrawSelection(
					drawList,
					layout,
					new UiPoint(originX, originY),
					theme.Palette.Selection);
			}

			drawList.AddText(
				layout,
				new UiPoint(originX, originY),
				showPlaceholder
					? theme.Palette.TextDisabled
					: style.Foreground);

			if (HasFocus && !showPlaceholder)
			{
				DrawCaret(
					drawList,
					layout,
					new UiPoint(originX, originY),
					theme.Palette.Accent);
			}
		}

		finally
		{
			drawList.PopClip();
		}
	}

	protected internal override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();
		_dragging = true;
		CapturePointer(pointer.Button);
		UpdateCaretFromPoint(
			pointer.GlobalPosition,
			(pointer.Modifiers & UiKeyModifiers.Shift) != 0);

		return true;
	}

	protected internal override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (!_dragging)
		{
			return false;
		}

		UpdateCaretFromPoint(
			pointer.GlobalPosition,
			extendSelection: true);

		return true;
	}

	protected internal override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (!_dragging)
		{
			return false;
		}

		_dragging = false;
		ReleasePointer();
		return true;
	}

	protected internal override bool OnKeyDown(UiKeyEvent key)
	{
		bool shift = key.HasModifier(UiKeyModifiers.Shift);
		bool control = key.HasModifier(UiKeyModifiers.Control) ||
			key.HasModifier(UiKeyModifiers.Super);

		if (control)
		{
			switch (key.Key)
			{
				case UiKey.A:
					_editor.SelectAll();
					InvalidateVisual();
					return true;

				case UiKey.C:
					CopySelection();
					return true;

				case UiKey.X:
					if (!_isReadOnly)
					{
						CopySelection();

						if (_editor.CutSelection())
						{
							_layout = null;
							InvalidateVisual();
							RaiseTextChanged();
						}
					}

					return true;

				case UiKey.V:
					if (!_isReadOnly)
					{
						string? pasted = Context?.Clipboard.GetText();

						if (!string.IsNullOrEmpty(pasted))
						{
							InsertCommittedText(pasted);
						}
					}

					return true;
			}
		}

		switch (key.Key)
		{
			case UiKey.Left:
				MoveCaret(-1, shift);
				return true;

			case UiKey.Right:
				MoveCaret(1, shift);
				return true;

			case UiKey.Home:
				_editor.MoveCaretToLineStart(shift);
				AfterCaretChange();
				return true;

			case UiKey.End:
				_editor.MoveCaretToLineEnd(shift);
				AfterCaretChange();
				return true;

			case UiKey.Backspace when !_isReadOnly:
				ReplaceSelection(string.Empty, backspace: true);
				return true;

			case UiKey.Delete when !_isReadOnly:
				ReplaceSelection(string.Empty, delete: true);
				return true;

			case UiKey.Enter:
				CommitEditing();
				return true;

			case UiKey.Escape:
				if (_editor.Text != _textAtFocus)
				{
					_editor.SetText(_textAtFocus);
					_layout = null;
					InvalidateVisual();
					RaiseTextChanged();
				}

				ClearFocus();
				return true;
		}

		return false;
	}

	protected internal override bool OnTextInput(UiTextEvent text)
	{
		if (_isReadOnly)
		{
			return true;
		}

		InsertCommittedText(text.Text);
		return true;
	}

	protected internal override void OnClick(int clickCount)
	{
		if (clickCount == 2)
		{
			SelectWordAtCaret();
		}

		else if (clickCount >= 3)
		{
			_editor.SelectAll();
			InvalidateVisual();
		}
	}

	protected internal override void OnFocusChanged(bool focused)
	{
		if (focused)
		{
			_textAtFocus = _editor.Text;
			_editor.SelectAll();
			InvalidateVisual();
		}

		else
		{
			_dragging = false;
			CommitEditing();
		}
	}

	/// <summary>
	/// Commits the field's text. Numeric fields parse and normalize
	/// here.
	/// </summary>
	protected virtual void CommitText()
	{
	}

	protected void ReplaceText(string text, bool notify = true)
	{
		if (_editor.Text == text)
		{
			return;
		}

		_editor.SetText(text);
		_layout = null;
		InvalidateVisual();

		if (notify)
		{
			RaiseTextChanged();
		}
	}

	protected void CommitEditing()
	{
		CommitText();
		EditingEnded?.Invoke(this);
	}

	protected void MoveCaret(int direction, bool extendSelection)
	{
		_editor.MoveCaret(direction, extendSelection);
		AfterCaretChange();
	}

	protected void AfterCaretChange()
	{
		InvalidateVisual();
	}

	protected void RaiseTextChanged()
	{
		TextChanged?.Invoke(this);
	}

	private void InsertCommittedText(string raw)
	{
		string text = UiTextEditor.SanitizeSingleLine(raw);
		int selected = _editor.SelectionEnd - _editor.SelectionStart;
		int available = _maximumLength -
			Math.Max(0, _editor.Text.Length - selected);

		if (available <= 0)
		{
			return;
		}

		text = UiTextEditor.ClampLength(text, available);

		if (text.Length == 0)
		{
			return;
		}

		ReplaceSelection(text);
	}

	private void ReplaceSelection(
		string replacement,
		bool backspace = false,
		bool delete = false)
	{
		bool changed;

		if (backspace)
		{
			changed = _editor.Backspace();
		}

		else if (delete)
		{
			changed = _editor.Delete();
		}

		else
		{
			changed = _editor.Insert(replacement);
		}

		if (!changed)
		{
			return;
		}

		_layout = null;
		InvalidateVisual();
		RaiseTextChanged();
	}

	private void UpdateCaretFromPoint(
		UiPoint globalPosition,
		bool extendSelection)
	{
		UiFont font = ResolveFont();
		UiRect content = GlobalContentBounds;
		UiTextLayoutResult layout = EnsureLayout(
			font,
			Text,
			content.Width);

		float x = globalPosition.X - (content.X - _scrollX);
		float y = globalPosition.Y - content.Y;

		int index = layout.GetIndexAt(new UiPoint(x, y));

		_editor.SetCaret(index, extendSelection);
		InvalidateVisual();
	}

	private void SelectWordAtCaret()
	{
		string text = _editor.Text;

		if (text.Length == 0)
		{
			return;
		}

		int caret = Math.Clamp(_editor.CaretIndex, 0, text.Length);

		if (caret == text.Length)
		{
			caret--;
		}

		if (caret > 0 &&
			char.IsLowSurrogate(text[caret]) &&
			char.IsHighSurrogate(text[caret - 1]))
		{
			caret--;
		}

		int start = caret;
		int end = caret;

		while (start > 0 && !IsWordSeparator(text[start - 1]))
		{
			start--;
		}

		while (end < text.Length && !IsWordSeparator(text[end]))
		{
			end++;
		}

		_editor.SetCaret(start);
		_editor.SetCaret(end, extendSelection: true);
		InvalidateVisual();
	}

	private static bool IsWordSeparator(char character)
	{
		return char.IsWhiteSpace(character) ||
			char.IsPunctuation(character) ||
			char.IsSymbol(character);
	}

	private void CopySelection()
	{
		if (!HasSelection)
		{
			return;
		}

		Context?.Clipboard.SetText(_editor.SelectedText);
	}

	private void DrawSelection(
		UiDrawList drawList,
		UiTextLayoutResult layout,
		UiPoint origin,
		UiColor color)
	{
		int start = _editor.SelectionStart;
		int end = _editor.SelectionEnd;
		float lineHeight = layout.Font.LineHeight;

		for (int index = 0; index < layout.LineCount; index++)
		{
			UiTextLine line = layout.GetLine(index);
			int lineStart = Math.Max(start, line.Start);
			int lineEnd = Math.Min(end, line.End);

			if (lineStart >= lineEnd)
			{
				continue;
			}

			float x1 = layout.GetCaretPosition(lineStart).X;
			float x2 = layout.GetCaretPosition(lineEnd).X;

			drawList.AddRect(
				new UiRect(
					origin.X + x1,
					origin.Y + line.Top,
					MathF.Max(1f, x2 - x1),
					lineHeight),
				color);
		}
	}

	private void DrawCaret(
		UiDrawList drawList,
		UiTextLayoutResult layout,
		UiPoint origin,
		UiColor color)
	{
		double blink = Context?.TimeSeconds ?? 0.0;

		if (blink % 1.0 > 0.55)
		{
			return;
		}

		UiPoint caret = layout.GetCaretPosition(_editor.CaretIndex);

		drawList.AddRect(
			new UiRect(
				origin.X + caret.X,
				origin.Y + caret.Y + 1f,
				1.5f,
				MathF.Max(1f, layout.Font.LineHeight - 2f)),
			color);
	}

	private UiTextLayoutResult EnsureLayout(
		UiFont font,
		string text,
		float width)
	{
		width = float.IsFinite(width)
			? MathF.Max(0.001f, width)
			: MathF.Max(0.001f, font.MeasureText(text));

		if (_layout is not null &&
			_layoutText == text &&
			_layoutFont == font &&
			_layoutWidth == width &&
			_layoutWrap == UiTextWrap.NoWrap)
		{
			UpdateScroll(font, _layout, width);
			return _layout;
		}

		_layout = UiTextLayout.Create(
			text,
			font,
			width,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		_layoutText = text;
		_layoutFont = font;
		_layoutWidth = width;
		_layoutWrap = UiTextWrap.NoWrap;

		UpdateScroll(font, _layout, width);

		return _layout;
	}

	private void UpdateScroll(
		UiFont font,
		UiTextLayoutResult layout,
		float width)
	{
		if (!HasFocus)
		{
			_scrollX = 0f;
			return;
		}

		float caret = layout.GetCaretPosition(_editor.CaretIndex).X;
		float margin = MathF.Min(8f, width * 0.25f);
		float maximum = MathF.Max(0f, layout.Width - width + margin);

		if (caret - _scrollX > width - margin)
		{
			_scrollX = caret - width + margin;
		}

		else if (caret - _scrollX < margin)
		{
			_scrollX = MathF.Max(0f, caret - margin);
		}

		_scrollX = Math.Clamp(_scrollX, 0f, maximum);
	}
}
