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
/// Bounded undo and redo history for authored state.
/// </summary>
public sealed class UndoStack
{
	private readonly Stack<IEditorCommand> _undo = [];
	private readonly Stack<IEditorCommand> _redo = [];

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

	public void Execute(IEditorCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);

		command.Execute();

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
		Changed?.Invoke(this);
	}

	private void Trim()
	{
		if (_undo.Count <= Capacity)
		{
			return;
		}

		IEditorCommand[] retained = _undo
			.Take(Capacity)
			.Reverse()
			.ToArray();

		_undo.Clear();

		foreach (IEditorCommand command in retained)
		{
			_undo.Push(command);
		}
	}
}

/// <summary>
/// Groups several primitive commands into one user-visible step, such
/// as a property drag or a multi-entity transform.
/// </summary>
public sealed class CompositeCommand : IEditorCommand
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
}
