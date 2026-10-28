namespace Volt.Editor;

/// <summary>
/// A rolling window of editor frame intervals.
/// </summary>
/// <remarks>
/// <para>
/// Samples are recorded in seconds, because that is what the frame
/// loop speaks, and reported in milliseconds, because that is how a
/// frame budget is read. The window has a fixed size, so the profiler
/// costs the same memory no matter how long the editor runs and always
/// describes recent pacing rather than an average since startup.
/// </para>
/// <para>
/// Only real, positive intervals are recorded. A paused or simulated
/// frame that reports zero would otherwise drag every statistic
/// toward a frame rate the editor never achieved.
/// </para>
/// </remarks>
public sealed class FrameTimeline
{
	/// <summary>
	/// The default number of intervals kept, about four seconds at
	/// sixty frames per second.
	/// </summary>
	public const int DefaultCapacity = 240;

	private readonly double[] _samples;
	private int _next;
	private int _count;

	/// <summary>
	/// Creates a timeline with a fixed window.
	/// </summary>
	/// <param name="capacity">The number of intervals to keep.</param>
	public FrameTimeline(int capacity = DefaultCapacity)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		_samples = new double[capacity];
	}

	/// <summary>
	/// Gets the number of intervals the window holds.
	/// </summary>
	public int Capacity => _samples.Length;

	/// <summary>
	/// Gets the number of intervals recorded so far, up to
	/// <see cref="Capacity"/>.
	/// </summary>
	public int Count => _count;

	/// <summary>
	/// Gets whether no interval has been recorded.
	/// </summary>
	public bool IsEmpty => _count == 0;

	/// <summary>
	/// Gets the most recent interval in milliseconds.
	/// </summary>
	public double LatestMilliseconds => _count == 0
		? 0.0
		: _samples[PreviousIndex()] * 1000.0;

	/// <summary>
	/// Gets the mean interval in milliseconds.
	/// </summary>
	public double AverageMilliseconds
	{
		get
		{
			if (_count == 0)
			{
				return 0.0;
			}

			double total = 0.0;

			for (int index = 0; index < _count; index++)
			{
				total += _samples[index];
			}

			return total / _count * 1000.0;
		}
	}

	/// <summary>
	/// Gets the shortest interval in the window, in milliseconds.
	/// </summary>
	public double MinimumMilliseconds
	{
		get
		{
			if (_count == 0)
			{
				return 0.0;
			}

			double minimum = double.MaxValue;

			for (int index = 0; index < _count; index++)
			{
				minimum = Math.Min(minimum, _samples[index]);
			}

			return minimum * 1000.0;
		}
	}

	/// <summary>
	/// Gets the longest interval in the window, in milliseconds.
	/// </summary>
	public double MaximumMilliseconds
	{
		get
		{
			if (_count == 0)
			{
				return 0.0;
			}

			double maximum = 0.0;

			for (int index = 0; index < _count; index++)
			{
				maximum = Math.Max(maximum, _samples[index]);
			}

			return maximum * 1000.0;
		}
	}

	/// <summary>
	/// Gets the frame rate implied by the mean interval.
	/// </summary>
	public double FramesPerSecond
	{
		get
		{
			double average = AverageMilliseconds;

			return average > 0.0 ? 1000.0 / average : 0.0;
		}
	}

	/// <summary>
	/// Records one frame interval.
	/// </summary>
	/// <param name="deltaSeconds">
	/// The interval in seconds. Values that are not finite and positive
	/// are ignored.
	/// </param>
	public void Record(double deltaSeconds)
	{
		if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0.0)
		{
			return;
		}

		_samples[_next] = deltaSeconds;
		_next = (_next + 1) % _samples.Length;
		_count = Math.Min(_count + 1, _samples.Length);
	}

	/// <summary>
	/// Forgets every recorded interval.
	/// </summary>
	public void Clear()
	{
		_next = 0;
		_count = 0;
	}

	/// <summary>
	/// Copies the window into a span, oldest first, in milliseconds.
	/// </summary>
	/// <param name="destination">The destination span.</param>
	/// <returns>
	/// The number of milliseconds values written, which is
	/// <see cref="Count"/> clamped to the span's length.
	/// </returns>
	public int CopySamples(Span<double> destination)
	{
		int copied = Math.Min(_count, destination.Length);
		int start = _count == _samples.Length ? _next : 0;

		// When the window has wrapped, the oldest interval sits at the
		// write cursor; before that, the samples are already in order.
		int offset = _count == _samples.Length
			? 0
			: _count - copied;

		for (int index = 0; index < copied; index++)
		{
			int sample = (start + offset + index) % _samples.Length;

			destination[index] = _samples[sample] * 1000.0;
		}

		return copied;
	}

	private int PreviousIndex()
	{
		return (_next - 1 + _samples.Length) % _samples.Length;
	}
}
