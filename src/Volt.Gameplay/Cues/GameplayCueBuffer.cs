namespace Volt.Gameplay.Cues;

/// <summary>Optional bounded FIFO for cue delivery at a presentation frame boundary.</summary>
/// <remarks>Enqueue copies immutable payloads. Capacity grows geometrically up to the configured event limit; overflow throws before insertion, so no transition is silently lost. Not thread-safe.</remarks>
public sealed class GameplayCueBuffer : IDisposable
{
	private GameplayCueEvent[] _events;
	private int _head;
	private int _count;
	private bool _disposed;

	/// <summary>Creates a buffer with initial capacity and a hard maximum.</summary>
	public GameplayCueBuffer(int initialCapacity = 64, int maximumCapacity = 65536)
	{
		if (initialCapacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(initialCapacity));
		}

		if (maximumCapacity < initialCapacity)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumCapacity));
		}

		_events = new GameplayCueEvent[initialCapacity];
		MaximumCapacity = maximumCapacity;
	}

	/// <summary>Maximum number of buffered transitions.</summary>
	public int MaximumCapacity { get; }
	/// <summary>Number of queued transitions.</summary>
	public int Count => _count;
	/// <summary>Current event array capacity.</summary>
	public int Capacity => _events.Length;

	/// <summary>Copies one transition into the FIFO.</summary>
	public void Enqueue(GameplayCueEvent cueEvent)
	{
		ThrowIfDisposed();
		if (_count == MaximumCapacity)
		{
			throw new InvalidOperationException($"Gameplay cue buffer reached its maximum capacity ({MaximumCapacity}); no event was enqueued.");
		}

		if (_count == _events.Length)
		{
			Grow();
		}

		_events[(_head + _count) % _events.Length] = cueEvent;
		_count++;
	}

	/// <summary>Copies up to destination length queued transitions in FIFO order and removes them.</summary>
	public int Drain(Span<GameplayCueEvent> destination)
	{
		ThrowIfDisposed();
		int copied = Math.Min(destination.Length, _count);
		for (int index = 0; index < copied; index++)
		{
			destination[index] = _events[_head];
			_events[_head] = default;
			_head = (_head + 1) % _events.Length;
		}
		_count -= copied;
		if (_count == 0)
		{
			_head = 0;
		}

		return copied;
	}

	/// <summary>Clears queued transitions without changing capacity.</summary>
	public void Clear()
	{
		ThrowIfDisposed();
		while (_count > 0) { _events[_head] = default; _head = (_head + 1) % _events.Length; _count--; }
		_head = 0;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_events = []; _head = 0; _count = 0; _disposed = true;
	}

	private void Grow()
	{
		int size = Math.Min(MaximumCapacity, checked(_events.Length * 2));
		if (size <= _events.Length)
		{
			throw new InvalidOperationException("Gameplay cue buffer cannot grow further.");
		}

		GameplayCueEvent[] grown = new GameplayCueEvent[size];
		for (int index = 0; index < _count; index++)
		{
			grown[index] = _events[(_head + index) % _events.Length];
		}

		_events = grown; _head = 0;
	}
	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
