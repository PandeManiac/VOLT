using System.Text;

namespace Volt.Ui.Text;

/// <summary>
/// Editable text with a caret and a selection, expressed in UTF-16
/// indices.
/// </summary>
/// <remarks>
/// The model clamps carets to Unicode scalar boundaries, so a
/// surrogate pair is treated as one editable unit for movement and
/// deletion. It performs no UI layout and has no clipboard
/// dependency; a text field supplies both.
/// </remarks>
public sealed class UiTextEditor
{
	private string _text = string.Empty;

	public string Text => _text;

	public int CaretIndex { get; private set; }

	public int SelectionAnchor { get; private set; }

	public bool HasSelection => CaretIndex != SelectionAnchor;

	public int SelectionStart => Math.Min(CaretIndex, SelectionAnchor);

	public int SelectionEnd => Math.Max(CaretIndex, SelectionAnchor);

	public string SelectedText =>
		HasSelection
			? _text[SelectionStart..SelectionEnd]
			: string.Empty;

	public void SetText(string text, bool moveCaretToEnd = true)
	{
		ArgumentNullException.ThrowIfNull(text);
		_text = text;

		if (moveCaretToEnd)
		{
			CaretIndex = text.Length;
			SelectionAnchor = CaretIndex;
		}

		else
		{
			CaretIndex = ClampBoundary(text, CaretIndex);
			SelectionAnchor = ClampBoundary(text, SelectionAnchor);
		}
	}

	public void SelectAll()
	{
		SelectionAnchor = 0;
		CaretIndex = _text.Length;
	}

	public void ClearSelection()
	{
		SelectionAnchor = CaretIndex;
	}

	public void SetCaret(int index, bool extendSelection = false)
	{
		CaretIndex = ClampBoundary(_text, index);

		if (!extendSelection)
		{
			SelectionAnchor = CaretIndex;
		}
	}

	public void MoveCaret(
		int direction,
		bool extendSelection = false,
		bool toBoundary = false)
	{
		int target;

		if (toBoundary)
		{
			target = direction < 0 ? 0 : _text.Length;
		}

		else
		{
			target = MoveByRune(_text, CaretIndex, direction);
		}

		SetCaret(target, extendSelection);
	}

	public void MoveCaretToLineStart(bool extendSelection = false)
	{
		SetCaret(0, extendSelection);
	}

	public void MoveCaretToLineEnd(bool extendSelection = false)
	{
		SetCaret(_text.Length, extendSelection);
	}

	public bool Insert(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		if (text.Length == 0)
		{
			return false;
		}

		DeleteSelectionCore();

		string next = string.Concat(
			_text.AsSpan(0, CaretIndex),
			text,
			_text.AsSpan(CaretIndex));

		_text = next;
		CaretIndex += text.Length;
		SelectionAnchor = CaretIndex;

		return true;
	}

	public bool Backspace()
	{
		if (HasSelection)
		{
			return DeleteSelectionCore();
		}

		if (CaretIndex == 0)
		{
			return false;
		}

		int previous = MoveByRune(_text, CaretIndex, -1);
		_text = string.Concat(
			_text.AsSpan(0, previous),
			_text.AsSpan(CaretIndex));

		CaretIndex = previous;
		SelectionAnchor = previous;

		return true;
	}

	public bool Delete()
	{
		if (HasSelection)
		{
			return DeleteSelectionCore();
		}

		if (CaretIndex >= _text.Length)
		{
			return false;
		}

		int next = MoveByRune(_text, CaretIndex, 1);
		_text = string.Concat(
			_text.AsSpan(0, CaretIndex),
			_text.AsSpan(next));

		return true;
	}

	public string CopySelection()
	{
		return SelectedText;
	}

	public bool CutSelection()
	{
		if (!HasSelection)
		{
			return false;
		}

		return DeleteSelectionCore();
	}

	public bool Paste(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}

		return Insert(text);
	}

	/// <summary>
	/// Truncates a string at a maximum length without splitting a
	/// surrogate pair.
	/// </summary>
	public static string ClampLength(string text, int maximum)
	{
		ArgumentNullException.ThrowIfNull(text);
		maximum = Math.Max(0, maximum);

		if (text.Length <= maximum)
		{
			return text;
		}

		int length = maximum;

		if (length > 0 && char.IsHighSurrogate(text[length - 1]))
		{
			length--;
		}

		return text[..length];
	}

	/// <summary>
	/// Removes characters that must not appear in a single-line field.
	/// </summary>
	public static string SanitizeSingleLine(string text)
	{
		ArgumentNullException.ThrowIfNull(text);

		if (!text.Contains('\n') && !text.Contains('\r'))
		{
			return text;
		}

		return text
			.Replace("\r\n", " ", StringComparison.Ordinal)
			.Replace('\n', ' ')
			.Replace('\r', ' ');
	}

	private bool DeleteSelectionCore()
	{
		if (!HasSelection)
		{
			return false;
		}

		int start = SelectionStart;
		int end = SelectionEnd;

		_text = string.Concat(
			_text.AsSpan(0, start),
			_text.AsSpan(end));

		CaretIndex = start;
		SelectionAnchor = start;

		return true;
	}

	private static int MoveByRune(
		string text,
		int index,
		int direction)
	{
		index = Math.Clamp(index, 0, text.Length);

		if (direction < 0)
		{
			if (index == 0)
			{
				return 0;
			}

			if (index >= 2 && char.IsLowSurrogate(text[index - 1]))
			{
				return index - 2;
			}

			return index - 1;
		}

		if (direction > 0)
		{
			if (index >= text.Length)
			{
				return text.Length;
			}

			if (char.IsHighSurrogate(text[index]) &&
				index + 1 < text.Length)
			{
				return index + 2;
			}

			return index + 1;
		}

		return index;
	}

	private static int ClampBoundary(string text, int index)
	{
		index = Math.Clamp(index, 0, text.Length);

		if (index > 0 &&
			index < text.Length &&
			char.IsLowSurrogate(text[index]) &&
			char.IsHighSurrogate(text[index - 1]))
		{
			return index - 1;
		}

		return index;
	}
}
