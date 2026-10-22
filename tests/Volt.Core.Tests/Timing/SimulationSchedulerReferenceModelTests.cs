using System.Globalization;
using Volt.Core.Timing;

namespace Volt.Core.Tests.Timing;

/// <summary>
/// Drives a deterministic random operation sequence through
/// <see cref="SimulationScheduler"/> and a deliberately simple
/// reference model.
/// </summary>
/// <remarks>
/// <para>
/// The model keeps one entry per logical registration in a plain
/// list and decides whether an occurrence is due from an exact
/// rational simulation-time inequality:
/// <c>(step - origin) * periodDenominator &gt;= 60 * occurrence *
/// periodNumerator</c>. It never computes a heap key the way the
/// scheduler does, never re-inserts an entry, and never uses the
/// scheduler's numerator/denominator encoding, so a shared deadline
/// bug cannot hide behind identical code.
/// </para>
/// <para>
/// Each seed runs a bounded number of operations. On failure the
/// message names the seed, the operation number, the operation, and
/// the first mismatching record.
/// </para>
/// </remarks>
public sealed class SimulationSchedulerReferenceModelTests
{
	private const int FixedUpdatesPerSecond = 60;
	private const long FrameNanoseconds = 16_666_667;

	[Theory]
	[InlineData(1u)]
	[InlineData(7u)]
	[InlineData(424242u)]
	[InlineData(20261021u)]
	public void RandomOperations_MatchTheReferenceModel(uint seed)
	{
		Random random = new((int)seed);
		Harness harness = new();

		for (int operation = 0; operation < 400; operation++)
		{
			string description = harness.Generate(random);

			harness.Compare(seed, operation, description);
		}

		harness.Compare(seed, 400, "final state");
		Assert.True(harness.InvocationCount > 50);

		harness.Dispose();
	}

	private sealed class Harness : IDisposable
	{
		private readonly FakeMonotonicClock _clock = new();
		private readonly GameLoop _loop;
		private readonly ReferenceScheduler _model = new();
		private readonly Dictionary<int, ScheduledHandle> _handles = [];
		private readonly List<RecordedInvocation> _actual = [];
		private readonly List<int> _activeIds = [];

		private int _nextId;
		private long _elapsedSteps;

		public Harness()
		{
			_loop = new GameLoop(
				new GameLoopSettings(
					FixedUpdatesPerSecond,
					maxFixedStepsPerFrame: 100,
					maximumFrameDelta: TimeSpan.FromSeconds(10)),
				_clock);
		}

		public int InvocationCount => _actual.Count;

		public string Generate(Random random)
		{
			int choice = random.Next(0, 16);

			switch (choice)
			{
				case 0:
				case 1:
				case 2:
					{
						int hz = new[]
						{
						1, 2, 3, 5, 7, 9, 10, 11, 15, 20, 30, 60
					}[random.Next(0, 12)];

						return Register(
							RegistrationKind.Hertz,
							hz,
							RandomAction(random),
							random);
					}

				case 3:
				case 4:
				case 5:
					{
						int milliseconds = new[]
						{
						17, 100, 250, 750, 1000, 2500
					}[random.Next(0, 6)];

						return Register(
							RegistrationKind.Interval,
							milliseconds,
							RandomAction(random),
							random);
					}

				case 6:
				case 7:
				case 8:
					{
						int milliseconds = new[]
						{
						0, 100, 750, 2000, 5000
					}[random.Next(0, 5)];

						return Register(
							RegistrationKind.Delay,
							milliseconds,
							RandomAction(random),
							random);
					}

				case 9:
					{
						if (_handles.Count == 0)
						{
							return Advance(random.Next(1, 4));
						}

						int[] known = [.. _handles.Keys];
						int id = known[random.Next(0, known.Length)];

						return Cancel(id);
					}

				case 10:
					return Advance(1);

				case 11:
					return Advance(random.Next(2, 6));

				case 12:
					return Advance(random.Next(10, 21));

				case 13:
					{
						// Three equal deadlines must run in registration
						// order regardless of heap layout.
						int milliseconds = new[] { 250, 1000, 2000 }[
							random.Next(0, 3)];

						Register(
							RegistrationKind.Interval,
							milliseconds,
							OracleAction.None,
							random);

						Register(
							RegistrationKind.Interval,
							milliseconds,
							OracleAction.None,
							random);

						return Register(
							RegistrationKind.Interval,
							milliseconds,
							OracleAction.None,
							random);
					}

				case 14:
					{
						// Cancelling an already-cancelled handle must be
						// harmless on both sides.
						if (_handles.Count == 0)
						{
							return Advance(1);
						}

						int[] known = [.. _handles.Keys];
						int id = known[random.Next(0, known.Length)];

						_loop.Schedule.Cancel(_handles[id]);
						_model.Cancel(id);

						return $"stale cancel {id}";
					}

				default:
					return Advance(random.Next(1, 3));
			}
		}

		public void Compare(uint seed, int operation, string description)
		{
			long step = _loop.TotalFixedSteps;

			if (!_model.Invocations.SequenceEqual(_actual))
			{
				throw new Xunit.Sdk.XunitException(
					$"Seed {seed} operation {operation} ({description}) " +
					$"at step {step}: invocation log differs. " +
					$"{DescribeDifference()}");
			}

			if (_model.ActiveCount != _loop.Schedule.ActiveCount)
			{
				throw new Xunit.Sdk.XunitException(
					$"Seed {seed} operation {operation} ({description}) " +
					$"at step {step}: active count was " +
					$"{_loop.Schedule.ActiveCount}, expected " +
					$"{_model.ActiveCount}.");
			}

			long expectedDeadline = _model.NextDeadlineStep();

			if (expectedDeadline != _loop.Schedule.NextDeadlineStep)
			{
				throw new Xunit.Sdk.XunitException(
					$"Seed {seed} operation {operation} ({description}) " +
					$"at step {step}: next deadline was " +
					$"{_loop.Schedule.NextDeadlineStep}, expected " +
					$"{expectedDeadline}.");
			}

			if (_model.TotalSkipped != _loop.Schedule.SkippedInvocations)
			{
				throw new Xunit.Sdk.XunitException(
					$"Seed {seed} operation {operation} ({description}) " +
					$"at step {step}: skipped count was " +
					$"{_loop.Schedule.SkippedInvocations}, expected " +
					$"{_model.TotalSkipped}.");
			}
		}

		public void Dispose()
		{
		}

		private string DescribeDifference()
		{
			int count = Math.Min(_model.Invocations.Count, _actual.Count);

			for (int index = 0; index < count; index++)
			{
				if (!_model.Invocations[index].Equals(_actual[index]))
				{
					return $"First difference at record {index}: " +
						$"actual {_actual[index]}, expected " +
						$"{_model.Invocations[index]}. " +
						$"Model entries: " +
						_model.Describe(_actual[index].Id) + " | " +
						_model.Describe(_model.Invocations[index].Id) + ".";
				}
			}

			return $"Record counts: actual {_actual.Count}, expected " +
				$"{_model.Invocations.Count}.";
		}

		private string Advance(int steps)
		{
			_clock.AdvanceNanoseconds(steps * FrameNanoseconds);

			FrameTiming frame = _loop.Advance();

			Assert.Equal(steps, frame.FixedSteps);

			for (long index = frame.TotalFixedSteps - steps + 1;
				index <= frame.TotalFixedSteps;
				index++)
			{
				_model.AdvanceToStep(index);
			}

			_elapsedSteps = frame.TotalFixedSteps;

			return string.Create(
				CultureInfo.InvariantCulture,
				$"advance {steps} step(s) to {_elapsedSteps}");
		}

		private string Register(
			RegistrationKind kind,
			int value,
			OracleAction action,
			Random random)
		{
			int id = _nextId++;
			int target = action == OracleAction.CancelTarget &&
				_activeIds.Count > 0
				? _activeIds[random.Next(0, _activeIds.Count)]
				: -1;
			int immediateId = action == OracleAction.RegisterImmediate
				? _nextId++
				: -1;

			long origin = _loop.TotalFixedSteps;

			long periodNumerator;
			long periodDenominator;
			bool repeating;

			switch (kind)
			{
				case RegistrationKind.Hertz:
					periodNumerator = 1;
					periodDenominator = value;
					repeating = true;
					break;

				case RegistrationKind.Interval:
					periodNumerator = value * TimeSpan.TicksPerMillisecond;
					periodDenominator = TimeSpan.TicksPerSecond;
					repeating = true;
					break;

				default:
					periodNumerator =
						value * TimeSpan.TicksPerMillisecond;
					periodDenominator = TimeSpan.TicksPerSecond;
					repeating = false;
					break;
			}

			// An immediate registration is identified once, so it may
			// only be created by a one-shot parent. A repeating parent
			// would otherwise reuse the same logical id on every
			// occurrence.
			if (repeating && action == OracleAction.RegisterImmediate)
			{
				action = OracleAction.None;
				immediateId = -1;
			}

			_model.Register(
				id,
				origin,
				periodNumerator,
				periodDenominator,
				repeating,
				action,
				target,
				immediateId);

			_activeIds.Add(id);

			ScheduledUpdateCallback callback =
				(in ScheduledUpdateContext context) =>
					OnInvoked(
						id,
						action,
						target,
						immediateId,
						in context);

			ScheduledHandle handle = kind switch
			{
				RegistrationKind.Hertz => _loop.Schedule.EveryHz(value, callback),
				RegistrationKind.Interval => _loop.Schedule.Every(
					TimeSpan.FromMilliseconds(value),
					callback),
				_ => _loop.Schedule.After(
					TimeSpan.FromMilliseconds(value),
					callback)
			};

			_handles[id] = handle;

			return string.Create(
				CultureInfo.InvariantCulture,
				$"register {id} {kind} {value} {action}");
		}

		private void OnInvoked(
			int id,
			OracleAction action,
			int target,
			int immediateId,
			in ScheduledUpdateContext context)
		{
			_actual.Add(new RecordedInvocation(
				id,
				context.StepIndex,
				context.Invocation,
				context.SkippedInvocations));

			switch (action)
			{
				case OracleAction.CancelSelf:
					if (_handles.TryGetValue(id, out ScheduledHandle self))
					{
						_loop.Schedule.Cancel(self);
					}

					break;

				case OracleAction.CancelTarget:
					if (_handles.TryGetValue(
						target,
						out ScheduledHandle other))
					{
						_loop.Schedule.Cancel(other);
					}

					break;

				case OracleAction.RegisterImmediate:
					{
						ScheduledHandle handle = _loop.Schedule.After(
							TimeSpan.Zero,
							(in ScheduledUpdateContext nested) =>
								OnInvoked(
									immediateId,
									OracleAction.None,
									-1,
									-1,
									in nested));

						_handles[immediateId] = handle;
						_activeIds.Add(immediateId);

						break;
					}
			}
		}

		private string Cancel(int id)
		{
			ScheduledHandle handle = _handles[id];

			bool cancelled = _loop.Schedule.Cancel(handle);

			Assert.Equal(_model.Cancel(id), cancelled);

			return $"cancel {id}";
		}

		private OracleAction RandomAction(Random random)
		{
			int value = random.Next(0, 10);

			return value switch
			{
				0 => OracleAction.CancelSelf,
				1 => OracleAction.CancelTarget,
				2 => OracleAction.RegisterImmediate,
				_ => OracleAction.None
			};
		}
	}

	private enum RegistrationKind
	{
		Hertz,
		Interval,
		Delay
	}

	private enum OracleAction
	{
		None,
		CancelSelf,
		CancelTarget,
		RegisterImmediate
	}

	private readonly record struct RecordedInvocation(
		int Id,
		long Step,
		long Occurrence,
		long Skipped);

	private sealed class ReferenceRegistration
	{
		public int Id;
		public long Order;
		public long Origin;
		public long PeriodNumerator;
		public long PeriodDenominator;
		public long NextOccurrence = 1;
		public bool Repeating;
		public bool Active = true;
		public OracleAction Action;
		public int Target;
		public int ImmediateId;
	}

	private sealed class ReferenceScheduler
	{
		private readonly List<ReferenceRegistration> _registrations = [];
		private readonly List<RecordedInvocation> _invocations = [];

		private long _nextOrder;
		private long _totalSkipped;

		public IReadOnlyList<RecordedInvocation> Invocations =>
			_invocations;

		public long TotalSkipped => _totalSkipped;

		public int ActiveCount
		{
			get
			{
				int count = 0;

				foreach (ReferenceRegistration registration in _registrations)
				{
					if (registration.Active)
					{
						count++;
					}
				}

				return count;
			}
		}

		public void Register(
			int id,
			long origin,
			long periodNumerator,
			long periodDenominator,
			bool repeating,
			OracleAction action,
			int target,
			int immediateId)
		{
			_registrations.Add(new ReferenceRegistration
			{
				Id = id,
				Order = _nextOrder++,
				Origin = origin,
				PeriodNumerator = periodNumerator,
				PeriodDenominator = periodDenominator,
				Repeating = repeating,
				Action = action,
				Target = target,
				ImmediateId = immediateId
			});
		}

		public bool Cancel(int id)
		{
			foreach (ReferenceRegistration registration in _registrations)
			{
				if (registration.Id == id && registration.Active)
				{
					registration.Active = false;
					return true;
				}
			}

			return false;
		}

		public long NextDeadlineStep()
		{
			long best = -1;

			foreach (ReferenceRegistration registration in _registrations)
			{
				if (!registration.Active)
				{
					continue;
				}

				long deadline = DeadlineStep(registration);

				if (best < 0 || deadline < best)
				{
					best = deadline;
				}
			}

			return best;
		}

		public string Describe(int id)
		{
			foreach (ReferenceRegistration registration in _registrations)
			{
				if (registration.Id != id)
				{
					continue;
				}

				return $"#{id} order={registration.Order} " +
					$"origin={registration.Origin} " +
					$"period={registration.PeriodNumerator}/" +
					$"{registration.PeriodDenominator} " +
					$"next={registration.NextOccurrence} " +
					$"active={registration.Active} " +
					$"deadline={DeadlineStep(registration)}";
			}

			return $"#{id} unknown";
		}

		public void AdvanceToStep(long step)
		{
			while (true)
			{
				ReferenceRegistration? due = null;
				long dueDeadline = long.MaxValue;

				foreach (ReferenceRegistration registration in _registrations)
				{
					if (!registration.Active)
					{
						continue;
					}

					long deadline = DeadlineStep(registration);

					if (deadline > step)
					{
						continue;
					}

					if (due is null ||
						deadline < dueDeadline ||
						(deadline == dueDeadline &&
							registration.Order < due.Order))
					{
						due = registration;
						dueDeadline = deadline;
					}
				}

				if (due is null)
				{
					return;
				}

				Fire(due, step);
			}
		}

		private void Fire(ReferenceRegistration registration, long step)
		{
			long occurrence = registration.NextOccurrence;
			long nextOccurrence = occurrence + 1;
			long skipped = 0;

			if (registration.Repeating)
			{
				// Occurrences whose deadlines are already in the past
				// are reported, not replayed.
				while (IsDue(registration, nextOccurrence, step))
				{
					nextOccurrence++;
				}

				skipped = nextOccurrence - occurrence - 1;
			}

			_invocations.Add(new RecordedInvocation(
				registration.Id,
				step,
				occurrence,
				skipped));

			_totalSkipped += skipped;

			if (registration.Repeating)
			{
				registration.NextOccurrence = nextOccurrence;
			}
			else
			{
				registration.Active = false;
			}

			switch (registration.Action)
			{
				case OracleAction.CancelSelf:
					registration.Active = false;
					break;

				case OracleAction.CancelTarget:
					Cancel(registration.Target);
					break;

				case OracleAction.RegisterImmediate:
					Register(
						id: registration.ImmediateId,
						origin: step,
						periodNumerator: 0,
						periodDenominator: TimeSpan.TicksPerSecond,
						repeating: false,
						action: OracleAction.None,
						target: -1,
						immediateId: -1);
					break;
			}
		}

		private static bool IsDue(
			ReferenceRegistration registration,
			long occurrence,
			long step)
		{
			long elapsed = step - registration.Origin;

			// Work registered during a step can never run on it.
			if (elapsed <= 0)
			{
				return false;
			}

			Int128 left = (Int128)elapsed *
				registration.PeriodDenominator;
			Int128 right = (Int128)FixedUpdatesPerSecond *
				occurrence * registration.PeriodNumerator;

			return left >= right;
		}

		private static long DeadlineStep(
			ReferenceRegistration registration)
		{
			if (registration.PeriodNumerator == 0)
			{
				return registration.Origin + 1;
			}

			Int128 scaled = (Int128)FixedUpdatesPerSecond *
				registration.NextOccurrence *
				registration.PeriodNumerator;

			Int128 steps =
				(scaled + registration.PeriodDenominator - 1) /
				registration.PeriodDenominator;

			Int128 deadline = (Int128)registration.Origin + steps;

			if (deadline <= registration.Origin)
			{
				deadline = (Int128)registration.Origin + 1;
			}

			return deadline > long.MaxValue
				? long.MaxValue
				: (long)deadline;
		}
	}
}
