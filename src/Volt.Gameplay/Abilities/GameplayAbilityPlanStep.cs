using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// One immutable step of an ability plan.
/// </summary>
/// <remarks>
/// <para>
/// A step is a value in a shared plan, not a delegate. Starting an
/// ability that has a plan stores only a cursor into the shared array,
/// so a thousand simultaneous activations share one plan and allocate
/// nothing for it.
/// </para>
/// <para>
/// Branching is expressed with jump targets that the plan builder
/// resolves when it compiles a structured <c>If</c>/<c>Else</c> block.
/// Execution remains a cursor over a flat array with no nested
/// interpreter and no per-activation control-flow object.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityPlanStep
{
	private GameplayAbilityPlanStep(
		GameplayAbilityPlanStepKind kind,
		float durationSeconds = 0f,
		GameplayTag tag = default,
		GameplayEventMatch match = GameplayEventMatch.Exact,
		GameplayTagWaitMode waitMode = GameplayTagWaitMode.Present,
		bool exact = false,
		GameplayEffectDefinition? effect = null,
		GameplayAbilityEffectTarget effectTarget =
			GameplayAbilityEffectTarget.Owner,
		GameplayWaitAny? wait = null,
		GameplayAbilityTaskDefinition? customTask = null,
		GameplayCondition? condition = null,
		int targetStep = -1)
	{
		Kind = kind;
		DurationSeconds = durationSeconds;
		Tag = tag;
		Match = match;
		WaitMode = waitMode;
		Exact = exact;
		Effect = effect;
		EffectTarget = effectTarget;
		Wait = wait;
		CustomTask = customTask;
		Condition = condition;
		TargetStep = targetStep;
	}

	/// <summary>
	/// Gets the step kind.
	/// </summary>
	public GameplayAbilityPlanStepKind Kind { get; }

	/// <summary>
	/// Gets the duration of a
	/// <see cref="GameplayAbilityPlanStepKind.WaitDuration"/> step.
	/// </summary>
	public float DurationSeconds { get; }

	/// <summary>
	/// Gets the tag of a wait or publish step.
	/// </summary>
	public GameplayTag Tag { get; }

	/// <summary>
	/// Gets the event matching mode of a
	/// <see cref="GameplayAbilityPlanStepKind.WaitEvent"/> step.
	/// </summary>
	public GameplayEventMatch Match { get; }

	/// <summary>
	/// Gets the wait mode of a
	/// <see cref="GameplayAbilityPlanStepKind.WaitTag"/> step.
	/// </summary>
	public GameplayTagWaitMode WaitMode { get; }

	/// <summary>
	/// Gets whether a tag condition requires an explicit grant.
	/// </summary>
	public bool Exact { get; }

	/// <summary>
	/// Gets the effect of an
	/// <see cref="GameplayAbilityPlanStepKind.ApplyEffect"/> step.
	/// </summary>
	public GameplayEffectDefinition? Effect { get; }

	/// <summary>
	/// Gets the target of an
	/// <see cref="GameplayAbilityPlanStepKind.ApplyEffect"/> step.
	/// </summary>
	public GameplayAbilityEffectTarget EffectTarget { get; }

	/// <summary>
	/// Gets the description of a
	/// <see cref="GameplayAbilityPlanStepKind.WaitAny"/> step.
	/// </summary>
	public GameplayWaitAny? Wait { get; }

	/// <summary>
	/// Gets the definition of a
	/// <see cref="GameplayAbilityPlanStepKind.WaitCustom"/> step.
	/// </summary>
	public GameplayAbilityTaskDefinition? CustomTask { get; }

	/// <summary>
	/// Gets the condition of a
	/// <see cref="GameplayAbilityPlanStepKind.Branch"/> step.
	/// </summary>
	public GameplayCondition? Condition { get; }

	/// <summary>
	/// Gets the index a branch jumps to when its condition does not
	/// hold, or the index a goto continues at.
	/// </summary>
	public int TargetStep { get; }

	/// <summary>
	/// Creates a step that waits for a simulation duration.
	/// </summary>
	/// <param name="durationSeconds">The non-negative duration.</param>
	public static GameplayAbilityPlanStep WaitDuration(float durationSeconds)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitDuration,
			durationSeconds);
	}

	/// <summary>
	/// Creates a step that waits for a gameplay event.
	/// </summary>
	/// <param name="tag">The event tag.</param>
	/// <param name="match">The matching mode.</param>
	public static GameplayAbilityPlanStep WaitEvent(
		GameplayTag tag,
		GameplayEventMatch match = GameplayEventMatch.Exact)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitEvent,
			tag: tag,
			match: match);
	}

	/// <summary>
	/// Creates a step that waits for a tag condition.
	/// </summary>
	/// <param name="tag">The tag to watch.</param>
	/// <param name="mode">Whether to wait for presence or absence.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant.
	/// </param>
	public static GameplayAbilityPlanStep WaitTag(
		GameplayTag tag,
		GameplayTagWaitMode mode,
		bool exact = false)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitTag,
			tag: tag,
			waitMode: mode,
			exact: exact);
	}

	/// <summary>
	/// Creates a step that applies an effect.
	/// </summary>
	/// <param name="effect">The effect to apply.</param>
	/// <param name="target">The entity to apply it to.</param>
	public static GameplayAbilityPlanStep ApplyEffect(
		GameplayEffectDefinition effect,
		GameplayAbilityEffectTarget target = GameplayAbilityEffectTarget.Owner)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.ApplyEffect,
			effect: effect,
			effectTarget: target);
	}

	/// <summary>
	/// Creates a step that publishes a gameplay event from the owner to
	/// the activation target.
	/// </summary>
	/// <param name="tag">The event tag.</param>
	public static GameplayAbilityPlanStep PublishEvent(GameplayTag tag)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.PublishEvent,
			tag: tag);
	}

	/// <summary>
	/// Creates a step that waits for the first of several sources.
	/// </summary>
	/// <param name="wait">The composite wait description.</param>
	public static GameplayAbilityPlanStep WaitAny(GameplayWaitAny wait)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitAny,
			wait: wait);
	}

	/// <summary>
	/// Creates a step that waits for a registered custom task.
	/// </summary>
	/// <param name="definition">The registered definition.</param>
	public static GameplayAbilityPlanStep WaitCustom(
		GameplayAbilityTaskDefinition definition)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitCustom,
			customTask: definition);
	}

	/// <summary>
	/// Creates a step that ends the ability normally.
	/// </summary>
	public static GameplayAbilityPlanStep End()
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.End);
	}

	/// <summary>
	/// Creates a step that jumps when a condition does not hold.
	/// </summary>
	/// <param name="condition">The condition to evaluate.</param>
	/// <param name="targetStep">The index to continue at otherwise.</param>
	internal static GameplayAbilityPlanStep Branch(
		GameplayCondition condition,
		int targetStep)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.Branch,
			condition: condition,
			targetStep: targetStep);
	}

	/// <summary>
	/// Creates a step that continues at another index.
	/// </summary>
	/// <param name="targetStep">The index to continue at.</param>
	internal static GameplayAbilityPlanStep Goto(int targetStep)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.Goto,
			targetStep: targetStep);
	}

	/// <summary>
	/// Returns the same step with a different jump target.
	/// </summary>
	/// <param name="targetStep">The resolved index.</param>
	internal GameplayAbilityPlanStep WithTargetStep(int targetStep)
	{
		return new GameplayAbilityPlanStep(
			Kind,
			DurationSeconds,
			Tag,
			Match,
			WaitMode,
			Exact,
			Effect,
			EffectTarget,
			Wait,
			CustomTask,
			Condition,
			targetStep);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Kind switch
		{
			GameplayAbilityPlanStepKind.WaitDuration =>
				$"WaitDuration({DurationSeconds})",
			GameplayAbilityPlanStepKind.WaitEvent => $"WaitEvent({Tag})",
			GameplayAbilityPlanStepKind.WaitTag =>
				$"WaitTag({Tag}, {WaitMode})",
			GameplayAbilityPlanStepKind.ApplyEffect =>
				$"ApplyEffect({Effect?.Name}, {EffectTarget})",
			GameplayAbilityPlanStepKind.PublishEvent =>
				$"PublishEvent({Tag})",
			GameplayAbilityPlanStepKind.WaitAny =>
				$"WaitAny({Wait?.ToString() ?? "none"})",
			GameplayAbilityPlanStepKind.WaitCustom =>
				$"WaitCustom({CustomTask?.Name ?? "none"})",
			GameplayAbilityPlanStepKind.Branch =>
				$"Branch({Condition}, -> {TargetStep})",
			GameplayAbilityPlanStepKind.Goto => $"Goto({TargetStep})",
			_ => "End"
		};
	}

}
