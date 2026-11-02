using Volt.Editor.Core.Editing;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the saved depth of the history surviving the capacity trim.
/// </summary>
/// <remarks>
/// Trimming drops the oldest commands, so a saved depth moves down by
/// exactly that many. Undoing everything left after a trim still returns
/// to the saved state, which a depth moved by the retained count would
/// miss.
/// </remarks>
public sealed class UndoStackSaveDepthTests
{
	[Fact]
	public void UndoingEverythingAfterATrimIsStillTheSavedState()
	{
		UndoStack undo = new(capacity: 3);

		undo.Execute(new Step());
		undo.MarkSaved();

		Assert.True(undo.IsAtSavedDepth);

		undo.Execute(new Step());
		undo.Execute(new Step());
		undo.Execute(new Step());

		// The first command left the history, so the saved state is now
		// the bottom of what remains.
		Assert.Equal(3, undo.UndoCount);
		Assert.False(undo.IsAtSavedDepth);

		Assert.True(undo.Undo());
		Assert.True(undo.Undo());
		Assert.True(undo.Undo());

		Assert.True(undo.IsAtSavedDepth);
	}

	[Fact]
	public void ASavedStateThatLeavesTheHistoryIsForgotten()
	{
		UndoStack undo = new(capacity: 2);

		undo.MarkSaved();
		undo.Execute(new Step());
		undo.Execute(new Step());
		undo.Execute(new Step());

		Assert.False(undo.IsAtSavedDepth);

		Assert.True(undo.Undo());
		Assert.True(undo.Undo());

		// Undoing everything retained is not the written state: the
		// command that produced it was dropped.
		Assert.False(undo.IsAtSavedDepth);
	}

	private sealed class Step : IEditorCommand
	{
		public string Description => "Step";

		public void Execute()
		{
		}

		public void Undo()
		{
		}
	}
}
