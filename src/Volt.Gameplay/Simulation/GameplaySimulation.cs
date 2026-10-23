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
	/// The services belong to different worlds.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The effect or ability system is already driven by another
	/// integration, or the loop is currently advancing.
	/// </exception>
	/// <remarks>
	/// The integration does not own the stores. Dispose it before
	/// disposing the ability system or the effect system.
	/// </remarks>
	public GameplaySimulation(
		GameLoop loop,
		GameplayAbilitySystem abilities,
		GameplayEffectSystem effects)
	{
		ArgumentNullException.ThrowIfNull(loop);
		ArgumentNullException.ThrowIfNull(abilities);
		ArgumentNullException.ThrowIfNull(effects);

		if (!ReferenceEquals(abilities.World, effects.World))
		{
			throw new ArgumentException(
				"The ability and effect systems belong to different " +
				"worlds. Gameplay services for one simulation must share " +
				"a world.",
				nameof(effects));
		}

		_loop = loop;
		_abilities = abilities;
		_effects = effects;
		_callback = Advance;

		_effects.BeginSimulation();

		try
		{
			_abilities.BeginSimulation();
			_loop.RegisterPostFixedUpdate(_callback);
		}
		catch
		{
			_abilities.EndSimulation();
			_effects.EndSimulation();

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
		_abilities.EndSimulation();
		_effects.EndSimulation();
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
