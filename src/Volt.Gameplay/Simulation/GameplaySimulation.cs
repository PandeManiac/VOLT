using Volt.Core.Timing;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;

namespace Volt.Gameplay.Simulation;

/// <summary>
/// Advances gameplay effects and ability tasks from the engine's
/// fixed-step simulation loop.
/// </summary>
/// <remarks>
/// <para>
/// The integration owns one post-fixed-update registration. It does not
/// create a timer, thread, or second clock: every gameplay clock is
/// advanced by the loop's exact fixed delta, after the step's game
/// fixed-update and scheduled callbacks have run.
/// </para>
/// <para>
/// Within the gameplay phase, effects advance first, then ability
/// tasks. Effect expiry and tag changes therefore run before tag
/// conditions complete, and a plan continuation runs after the effects
/// and tags that made it eligible. Gameplay events are delivered
/// synchronously where they are published, as they are without the
/// integration.
/// </para>
/// <para>
/// Tasks created during the gameplay phase are eligible on the next
/// fixed step, matching the task runtime and
/// <see cref="SimulationScheduler"/>. Effects and tasks that were
/// already due in this step all complete in this step.
/// </para>
/// <para>
/// While attached, the effect and task systems reject manual
/// <c>Advance</c> calls. That prevents a double advance in one step
/// without a per-entity validation on the hot path. Detach the
/// integration to return to manual control.
/// </para>
/// <para>
/// The integration is optional: a project that does not construct one
/// keeps the manual API and pays nothing. It is single-threaded and
/// allocates nothing per fixed step.
/// </para>
/// </remarks>
public sealed class GameplaySimulation : IDisposable
{
	private readonly GameLoop _loop;
	private readonly GameplayEffectSystem _effects;
	private readonly GameplayAbilitySystem _abilities;
	private readonly PostFixedUpdateCallback _callback;
	private readonly SimulationClockClaim _claim = new();

	private long _fixedSteps;
	private bool _disposed;

	/// <summary>
	/// Attaches gameplay advancement to a loop.
	/// </summary>
	/// <param name="loop">The loop that owns simulation time.</param>
	/// <param name="abilities">The ability runtime to advance.</param>
	/// <param name="effects">The effect runtime to advance.</param>
	/// <exception cref="ArgumentNullException">
	/// An argument is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The effect system is not the one the ability system applies its
	/// costs, cooldowns, and plan effects through.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The effect or ability system is already driven by another
	/// integration, or the loop is currently advancing.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The integration does not own the stores. Dispose it before
	/// disposing the ability system or the effect system.
	/// </para>
	/// <para>
	/// A failed construction releases only the claims it acquired. An
	/// integration that already owns a clock keeps it, so a rejected
	/// second attempt cannot leave the first one running without its
	/// double-advance protection.
	/// </para>
	/// </remarks>
	public GameplaySimulation(
		GameLoop loop,
		GameplayAbilitySystem abilities,
		GameplayEffectSystem effects)
	{
		ArgumentNullException.ThrowIfNull(loop);
		ArgumentNullException.ThrowIfNull(abilities);
		ArgumentNullException.ThrowIfNull(effects);

		if (!ReferenceEquals(abilities.EffectRuntime, effects))
		{
			throw new ArgumentException(
				"The supplied effect system is not the one these " +
				"abilities apply costs, cooldowns, and plan effects " +
				"through. Advancing a different instance would leave the " +
				"ability clocks unmoved even though both share a world.",
				nameof(effects));
		}

		_loop = loop;
		_abilities = abilities;
		_effects = effects;
		_callback = Advance;

		try
		{
			_effects.BeginSimulation(_claim);
			_abilities.BeginSimulation(_claim);
			_loop.RegisterPostFixedUpdate(_callback);
		}
		catch
		{
			// Both releases are claim-checked, so a clock this
			// construction never acquired is left with its owner.
			_abilities.EndSimulation(_claim);
			_effects.EndSimulation(_claim);

			throw;
		}
	}

	/// <summary>
	/// Gets the ability runtime advanced by this integration.
	/// </summary>
	public GameplayAbilitySystem Abilities => _abilities;

	/// <summary>
	/// Gets the effect runtime advanced by this integration.
	/// </summary>
	public GameplayEffectSystem Effects => _effects;

	/// <summary>
	/// Gets the event runtime shared by the abilities.
	/// </summary>
	public GameplayEventSystem Events => _abilities.Events;

	/// <summary>
	/// Gets the loop that owns simulation time.
	/// </summary>
	public GameLoop Loop => _loop;

	/// <summary>
	/// Gets the last fixed step index the integration advanced.
	/// </summary>
	public long FixedStepCount => _fixedSteps;

	/// <summary>
	/// Gets whether the integration is still attached.
	/// </summary>
	public bool IsAttached => !_disposed;

	/// <inheritdoc />
	/// <remarks>
	/// Unregisters the fixed-step callback and releases the clock
	/// claims, so the effect and task systems return to manual control.
	/// It does not dispose or clear the gameplay stores. Call it outside
	/// <see cref="GameLoop.Advance"/>, because the loop rejects
	/// registration changes while a frame is executing.
	/// </remarks>
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_loop.UnregisterPostFixedUpdate(_callback);
		_abilities.EndSimulation(_claim);
		_effects.EndSimulation(_claim);
		_disposed = true;

		GC.SuppressFinalize(this);
	}

	private void Advance(in PostFixedUpdateContext context)
	{
		if (_disposed)
		{
			return;
		}

		_fixedSteps = context.StepIndex;

		float deltaSeconds = _loop.FixedDeltaSeconds;

		_effects.AdvanceFromSimulation(deltaSeconds);
		_abilities.AdvanceTasksFromSimulation(deltaSeconds);
	}
}
