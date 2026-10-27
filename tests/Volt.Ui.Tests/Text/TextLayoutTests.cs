using Volt.Ui.Input;
using Volt.Ui.Text;

namespace Volt.Ui.Tests.Text;

public sealed class TextLayoutTests
{
	[Fact]
	public void Wrapping_RespectsAvailableWidth()
	{
		UiFont font = new(UiFontKind.Sans, 13f);
		UiTextLayoutResult layout = UiTextLayout.Create(
			"The quick brown fox jumps over the lazy dog",
			font,
			120f,
			UiTextWrap.Wrap);

		Assert.True(layout.LineCount > 1);

		for (int index = 0; index < layout.LineCount; index++)
		{
			Assert.True(layout.GetLine(index).Width <= 120.01f);
		}
	}

	[Fact]
	public void CaretPosition_RoundTripsThroughLine()
	{
		UiFont font = new(UiFontKind.Sans, 13f);
		UiTextLayoutResult layout = UiTextLayout.Create(
			"Alpha",
			font,
			200f);

		UiPoint caret = layout.GetCaretPosition(3);
		int index = layout.GetIndexAt(caret);

		Assert.InRange(index, 2, 4);
	}

	[Fact]
	public void UnicodeGlyph_IsRasterized()
	{
		UiFont font = new(UiFontKind.Sans, 13f);
		UiGlyph glyph = font.GetGlyph('é');

		Assert.False(glyph.IsEmpty);
		Assert.True(glyph.Advance > 0f);
		Assert.True(glyph.Uv.Width > 0f);
	}

	[Fact]
	public void EditorBackspace_RemovesWholeSurrogatePair()
	{
		UiTextEditor editor = new();
		editor.SetText("a😀b");

		// a = 1 UTF-16 unit; the emoji is a surrogate pair.
		editor.SetCaret(3);
		Assert.True(editor.Backspace());

		Assert.Equal("ab", editor.Text);
		Assert.Equal(1, editor.CaretIndex);
	}

	[Fact]
	public void EditorInsert_ReplacesSelection()
	{
		UiTextEditor editor = new();
		editor.SetText("hello");
		editor.SetCaret(1);
		editor.SetCaret(4, extendSelection: true);

		editor.Insert("i");

		Assert.Equal("hio", editor.Text);
		Assert.Equal(2, editor.CaretIndex);
	}

	[Fact]
	public void TextField_UsesClipboardForCopyAndPaste()
	{
		using UiTestHost host = new(400f, 200f);
		Volt.Ui.Widgets.TextField field = host.Add(
			new Volt.Ui.Widgets.TextField("copy me"));

		host.Layout();
		field.Focus();

		Assert.True(host.Context.KeyDown(
			UiKey.A,
			UiKeyModifiers.Control));
		Assert.True(host.Context.KeyDown(
			UiKey.C,
			UiKeyModifiers.Control));

		Assert.Equal("copy me", host.Context.Clipboard.GetText());

		host.Context.TextInput("X");

		Assert.Equal("X", field.Text);
	}
}
