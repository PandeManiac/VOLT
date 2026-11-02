namespace Volt.Editor.Core.Editing;

/// <summary>
/// A reversible editor operation.
/// </summary>
public interface IEditorCommand
{
	string Description { get; }

	void Execute();

	void Undo();
}

/// <summary>
/// A command that stores an asset path and can follow a project-level
/// rename or deletion without losing its history entry.
/// </summary>
public interface IAssetReferenceCommand
{
	void RemapAssetReference(string oldId, string newId);
}

/// <summary>
/// Bounded undo and redo history for authored state.
/// </summary>
public sealed class UndoStack
{
	private readonly Stack<IEditorCommand> _undo = [];
	private readonly Stack<IEditorCommand> _redo = [];
	private int _savedDepth = -1;

	public UndoStack(int capacity = 256)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		Capacity = capacity;
	}

	public event Action<UndoStack>? Changed;

	public int Capacity { get; }

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	public int UndoCount => _undo.Count;

	public int RedoCount => _redo.Count;

	public string? UndoDescription =>
		_undo.Count > 0 ? _undo.Peek().Description : null;

	public string? RedoDescription =>
		_redo.Count > 0 ? _redo.Peek().Description : null;

	/// <summary>
	/// Whether the history sits exactly where the document was last
	/// written, so the authored state matches what is on disk.
	/// </summary>
	/// <remarks>
	/// Depth alone cannot answer this after a divergent edit, so an
	/// execute whose redo region held the saved depth forgets it: the
	/// document moved on rather than returning to the written state.
	/// </remarks>
	public bool IsAtSavedDepth =>
		_savedDepth >= 0 && _savedDepth == _undo.Count;

	/// <summary>
	/// Records the current depth as the state on disk.
	/// </summary>
	public void MarkSaved()
	{
		_savedDepth = _undo.Count;
	}

	public void Execute(IEditorCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);

		command.Execute();

		// A new command replaces the redo region, so a saved depth that
		// lived there is gone even if the count happens to match again.
		if (_savedDepth > _undo.Count)
		{
			_savedDepth = -1;
		}

		_undo.Push(command);
		_redo.Clear();
		Trim();
		Changed?.Invoke(this);
	}

	public bool Undo()
	{
		if (_undo.Count == 0)
		{
			return false;
		}

		IEditorCommand command = _undo.Pop();
		command.Undo();
		_redo.Push(command);
		Changed?.Invoke(this);

		return true;
	}

	public bool Redo()
	{
		if (_redo.Count == 0)
		{
			return false;
		}

		IEditorCommand command = _redo.Pop();
		command.Execute();
		_undo.Push(command);
		Changed?.Invoke(this);

		return true;
	}

	public void Clear()
	{
		_undo.Clear();
		_redo.Clear();
		_savedDepth = -1;
		Changed?.Invoke(this);
	}

	/// <summary>
	/// Moves saved asset references in both history directions to the
	/// asset's new identity. Existing commands keep their order and
	/// descriptions; only the paths they restore are updated.
	/// </summary>
	public void RemapAssetReference(string oldId, string newId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
		ArgumentNullException.ThrowIfNull(newId);

		RemapCommands(_undo, oldId, newId);
		RemapCommands(_redo, oldId, newId);
	}

	private static void RemapCommands(
		IEnumerable<IEditorCommand> commands,
		string oldId,
		string newId)
	{
		foreach (IEditorCommand command in commands)
		{
			if (command is IAssetReferenceCommand assetCommand)
			{
				assetCommand.RemapAssetReference(oldId, newId);
			}
		}
	}

	private void Trim()
	{
		if (_undo.Count <= Capacity)
		{
			return;
		}

		// Only the commands that actually leave the history move the saved
		// depth, and a saved state that leaves it can no longer be reached.
		int removed = _undo.Count - Capacity;

		IEditorCommand[] retained = _undo
			.Take(Capacity)
			.Reverse()
			.ToArray();

		_undo.Clear();

		foreach (IEditorCommand command in retained)
		{
			_undo.Push(command);
		}

		if (_savedDepth >= 0)
		{
			_savedDepth = Math.Max(-1, _savedDepth - removed);
		}
	}
}

/// <summary>
/// Groups several primitive commands into one user-visible step, such
/// as a property drag or a multi-entity transform.
/// </summary>
public sealed class CompositeCommand : IEditorCommand, IAssetReferenceCommand
{
	private readonly IReadOnlyList<IEditorCommand> _commands;

	public CompositeCommand(
		string description,
		IEnumerable<IEditorCommand> commands)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(description);
		ArgumentNullException.ThrowIfNull(commands);

		Description = description;
		_commands = [.. commands];
	}

	public string Description { get; }

	public void Execute()
	{
		foreach (IEditorCommand command in _commands)
		{
			command.Execute();
		}
	}

	public void Undo()
	{
		for (int index = _commands.Count - 1; index >= 0; index--)
		{
			_commands[index].Undo();
		}
	}

	public void RemapAssetReference(string oldId, string newId)
	{
		foreach (IEditorCommand command in _commands)
		{
			if (command is IAssetReferenceCommand assetCommand)
			{
				assetCommand.RemapAssetReference(oldId, newId);
			}
		}
	}
}
