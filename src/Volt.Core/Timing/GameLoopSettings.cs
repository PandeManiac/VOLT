namespace Volt.Core.Timing;

/// <summary>
/// Configures the fixed simulation rate and the overload limits of a
/// <see cref="GameLoop"/>.
/// </summary>
/// <remarks>
/// <para>
/// The settings are immutable. Invalid values are rejected when the
/// value is constructed, so a loop never starts with a rate that
/// cannot be represented or an overload policy that cannot bound
/// catch-up work.
/// </para>
/// <para>
/// The defaults describe a 60 Hz simulation that may execute at most
/// five fixed steps per application frame and treats any elapsed
/// interval above 250 ms as a hitch rather than simulation time.
/// </para>
/// </remarks>
public readonly record struct GameLoopSettings
{
	/// <summary>
	/// Default fixed simulation rate, in updates per second.
	/// </summary>
	public const int DefaultFixedUpdatesPerSecond = 60;

	/// <summary>
	/// Default upper bound for fixed steps executed by one frame.
	/// </summary>
	public const int DefaultMaxFixedStepsPerFrame = 5;

	/// <summary>
	/// Smallest supported fixed simulation rate, in updates per second.
	/// </summary>
	public const int MinimumFixedUpdatesPerSecond = 1;

	/// <summary>
	/// Largest supported fixed simulation rate, in updates per second.
	/// </summary>
	/// <remarks>
	/// The bound keeps the accumulator's integer units far from
	/// overflow for any accepted frame delta and is well above the
	/// rates an application normally needs.
	/// </remarks>
	public const int MaximumFixedUpdatesPerSecond = 1000;

	/// <summary>
	/// Smallest supported fixed-step cap for one frame.
	/// </summary>
	public const int MinimumMaxFixedStepsPerFrame = 1;

	/// <summary>
	/// Largest supported fixed-step cap for one frame.
	/// </summary>
	public const int MaximumMaxFixedStepsPerFrame = 100;

	/// <summary>
	/// Smallest supported elapsed-time clamp.
	/// </summary>
	public const int MinimumMaximumFrameDeltaMilliseconds = 1;

	/// <summary>
	/// Largest supported elapsed-time clamp.
	/// </summary>
	public const int MaximumMaximumFrameDeltaMilliseconds = 10_000;

	/// <summary>
	/// Gets the default settings.
	/// </summary>
	/// <remarks>
	/// The explicit arguments matter: a struct's implicit parameterless
	/// construction zero-initializes its fields instead of calling this
	/// type's optional-parameter constructor.
	/// </remarks>
	public static GameLoopSettings Default => new(
		DefaultFixedUpdatesPerSecond,
		DefaultMaxFixedStepsPerFrame,
		DefaultMaximumFrameDelta);

	/// <summary>
	/// Gets the default elapsed-time clamp.
	/// </summary>
	public static TimeSpan DefaultMaximumFrameDelta =>
		TimeSpan.FromMilliseconds(250);

	/// <summary>
	/// Creates validated settings.
	/// </summary>
	/// <param name="fixedUpdatesPerSecond">
	/// Fixed simulation rate in updates per second.
	/// </param>
	/// <param name="maxFixedStepsPerFrame">
	/// Upper bound for fixed steps executed by one application frame.
	/// </param>
	/// <param name="maximumFrameDelta">
	/// Largest elapsed interval accepted as simulation time. Time
	/// beyond it is dropped. The default is 250 milliseconds.
	/// </param>
	/// <exception cref="ArgumentOutOfRangeException">
	/// A value is outside its supported range.
	/// </exception>
	public GameLoopSettings(
		int fixedUpdatesPerSecond = DefaultFixedUpdatesPerSecond,
		int maxFixedStepsPerFrame = DefaultMaxFixedStepsPerFrame,
		TimeSpan? maximumFrameDelta = null)
	{
		if (fixedUpdatesPerSecond < MinimumFixedUpdatesPerSecond ||
			fixedUpdatesPerSecond > MaximumFixedUpdatesPerSecond)
		{
			throw new ArgumentOutOfRangeException(
				nameof(fixedUpdatesPerSecond),
				fixedUpdatesPerSecond,
				$"The fixed simulation rate must be between " +
				$"{MinimumFixedUpdatesPerSecond} and " +
				$"{MaximumFixedUpdatesPerSecond} updates per second.");
		}

		if (maxFixedStepsPerFrame < MinimumMaxFixedStepsPerFrame ||
			maxFixedStepsPerFrame > MaximumMaxFixedStepsPerFrame)
		{
			throw new ArgumentOutOfRangeException(
				nameof(maxFixedStepsPerFrame),
				maxFixedStepsPerFrame,
				$"The fixed-step cap must be between " +
				$"{MinimumMaxFixedStepsPerFrame} and " +
				$"{MaximumMaxFixedStepsPerFrame}.");
		}

		TimeSpan delta = maximumFrameDelta ?? DefaultMaximumFrameDelta;

		if (delta < TimeSpan.FromMilliseconds(
				MinimumMaximumFrameDeltaMilliseconds) ||
			delta > TimeSpan.FromMilliseconds(
				MaximumMaximumFrameDeltaMilliseconds))
		{
			throw new ArgumentOutOfRangeException(
				nameof(maximumFrameDelta),
				delta,
				$"The maximum frame delta must be between " +
				$"{MinimumMaximumFrameDeltaMilliseconds} and " +
				$"{MaximumMaximumFrameDeltaMilliseconds} " +
				$"milliseconds.");
		}

		FixedUpdatesPerSecond = fixedUpdatesPerSecond;
		MaxFixedStepsPerFrame = maxFixedStepsPerFrame;
		MaximumFrameDelta = delta;
	}

	/// <summary>
	/// Gets the fixed simulation rate, in updates per second.
	/// </summary>
	public int FixedUpdatesPerSecond { get; }

	/// <summary>
	/// Gets the upper bound for fixed steps executed by one
	/// application frame.
	/// </summary>
	/// <remarks>
	/// When more steps are due, the excess is dropped rather than
	/// carried into the next frame, so sustained overload can never
	/// build an ever-growing catch-up backlog.
	/// </remarks>
	public int MaxFixedStepsPerFrame { get; }

	/// <summary>
	/// Gets the largest elapsed interval accepted as simulation
	/// time.
	/// </summary>
	public TimeSpan MaximumFrameDelta { get; }

	/// <summary>
	/// Rejects a settings value that was created without running the
	/// validating constructor, such as <c>default</c>.
	/// </summary>
	internal void Validate()
	{
		if (FixedUpdatesPerSecond < MinimumFixedUpdatesPerSecond ||
			FixedUpdatesPerSecond > MaximumFixedUpdatesPerSecond)
		{
			throw new ArgumentOutOfRangeException(
				nameof(FixedUpdatesPerSecond),
				FixedUpdatesPerSecond,
				$"The fixed simulation rate must be between " +
				$"{MinimumFixedUpdatesPerSecond} and " +
				$"{MaximumFixedUpdatesPerSecond} updates per second.");
		}

		if (MaxFixedStepsPerFrame < MinimumMaxFixedStepsPerFrame ||
			MaxFixedStepsPerFrame > MaximumMaxFixedStepsPerFrame)
		{
			throw new ArgumentOutOfRangeException(
				nameof(MaxFixedStepsPerFrame),
				MaxFixedStepsPerFrame,
				$"The fixed-step cap must be between " +
				$"{MinimumMaxFixedStepsPerFrame} and " +
				$"{MaximumMaxFixedStepsPerFrame}.");
		}

		if (MaximumFrameDelta < TimeSpan.FromMilliseconds(
				MinimumMaximumFrameDeltaMilliseconds) ||
			MaximumFrameDelta > TimeSpan.FromMilliseconds(
				MaximumMaximumFrameDeltaMilliseconds))
		{
			throw new ArgumentOutOfRangeException(
				nameof(MaximumFrameDelta),
				MaximumFrameDelta,
				$"The maximum frame delta must be between " +
				$"{MinimumMaximumFrameDeltaMilliseconds} and " +
				$"{MaximumMaximumFrameDeltaMilliseconds} " +
				$"milliseconds.");
		}
	}
}
