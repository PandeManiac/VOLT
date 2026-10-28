namespace Volt.Editor;

/// <summary>
/// A rolling window of managed bytes allocated between editor frames.
/// </summary>
/// <remarks>
/// <para>
/// The window has a fixed size, so the profiler reports recent
/// allocation pressure with a constant memory cost instead of growing
/// a list for the lifetime of the editor. Totals are maintained as
/// samples enter and leave the window, which keeps recording O(1).
/// </para>
/// <para>
/// Values are the managed allocation counter's delta, not a heap
/// measurement: a frame that allocates and collects within the same
/// interval still shows the allocation, which is the number that
/// points at avoidable garbage in a frame loop.
/// </para>
/// </remarks>
public sealed class MemoryTimeline
{
	/// <summary>
	/// The default number of frames kept, matching
	/// <see cref="FrameTimeline.DefaultCapacity"/>.
	/// </summary>
	public const int DefaultCapacity = 240;

	private readonly long[] _samples;
	private int _next;
	private int _count;
	private long _windowTotal;

	/// <summary>
	/// Creates a memory window with a fixed number of frame slots.
	/// </summary>
	/// <param name="capacity">The number of frames to keep.</param>
	public MemoryTimeline(int capacity = DefaultCapacity)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		_samples = new long[capacity];
	}

	/// <summary>
	/// Gets the number of frames the window holds.
	/// </summary>
	public int Capacity => _samples.Length;

	/// <summary>
	/// Gets the number of frames recorded, up to <see cref="Capacity"/>.
	/// </summary>
	public int Count => _count;

	/// <summary>
	/// Gets whether no frame has been recorded.
	/// </summary>
	public bool IsEmpty => _count == 0;

	/// <summary>
	/// Gets the bytes allocated during the most recent frame.
	/// </summary>
	public long LastBytes => _count == 0
		? 0
		: _samples[(_next - 1 + _samples.Length) % _samples.Length];

	/// <summary>
	/// Gets the bytes allocated across the frames in the window.
	/// </summary>
	public long TotalBytesInWindow => _windowTotal;

	/// <summary>
	/// Gets the mean bytes allocated per frame in the window.
	/// </summary>
	public double AverageBytesPerFrame => _count == 0
		? 0.0
		: (double)_windowTotal / _count;

	/// <summary>
	/// Gets the largest per-frame allocation in the window.
	/// </summary>
	public long PeakBytesInWindow
	{
		get
		{
			long peak = 0;

			for (int index = 0; index < _count; index++)
			{
				peak = Math.Max(peak, _samples[index]);
			}

			return peak;
		}
	}

	/// <summary>
	/// Records the bytes one frame allocated.
	/// </summary>
	/// <param name="allocatedBytes">
	/// The allocation delta for the frame. Negative deltas cannot come
	/// from a monotonic allocation counter and are rejected rather than
	/// silently hiding a measurement bug.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="allocatedBytes"/> is negative.
	/// </exception>
	public void Record(long allocatedBytes)
	{
		if (allocatedBytes < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(allocatedBytes),
				"A frame cannot allocate a negative number of bytes.");
		}

		if (_count == _samples.Length)
		{
			// The slot about to be overwritten leaves the window, so
			// its bytes must leave the running total with it.
			_windowTotal -= _samples[_next];
		}

		else
		{
			_count++;
		}

		_samples[_next] = allocatedBytes;
		_windowTotal += allocatedBytes;
		_next = (_next + 1) % _samples.Length;
	}

	/// <summary>
	/// Copies the window into a span, oldest first.
	/// </summary>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of samples written, which is <see cref="Count"/> clamped
	/// to the span's length.
	/// </returns>
	public int CopySamples(Span<double> destination)
	{
		int copied = Math.Min(_count, destination.Length);
		int start = _count == _samples.Length ? _next : 0;

		// When the window has wrapped, the oldest sample sits at the
		// write cursor; before that, the samples are already in order.
		int offset = _count == _samples.Length
			? 0
			: _count - copied;

		for (int index = 0; index < copied; index++)
		{
			int sample = (start + offset + index) % _samples.Length;

			destination[index] = _samples[sample];
		}

		return copied;
	}

	/// <summary>
	/// Forgets every recorded frame.
	/// </summary>
	public void Clear()
	{
		_next = 0;
		_count = 0;
		_windowTotal = 0;
	}
}
