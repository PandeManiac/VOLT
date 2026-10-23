using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// One immutable step of an ability plan.
/// </summary>
/// <remarks>
/// A step is a value in a shared plan, not a delegate. Starting an
/// ability that has a plan stores only a cursor into the shared array,
/// so a thousand simultaneous activations share one plan and allocate
/// nothing for it.
/// </remarks>
public readonly struct GameplayAbilityPlanStep
{
	private GameplayAbilityPlanStep(
		GameplayAbilityPlanStepKind kind,
		float durationSeconds,
		GameplayTag tag,
		GameplayEventMatch match,
		GameplayTagWaitMode waitMode,
		bool exact,
		GameplayEffectDefinition? effect,
		GameplayAbilityEffectTarget effectTarget)
	{
		Kind = kind;
		DurationSeconds = durationSeconds;
		Tag = tag;
		Match = match;
		WaitMode = waitMode;
		Exact = exact;
		Effect = effect;
		EffectTarget = effectTarget;
	}

	/// <summary>
	/// Gets the step kind.
	/// </summary>
	public GameplayAbilityPlanStepKind Kind { get; }

	/// <summary>
	/// Gets the duration of a <see cref="GameplayAbilityPlanStepKind.WaitDuration"/>
	/// step.
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
	/// Creates a step that waits for a simulation duration.
	/// </summary>
	/// <param name="durationSeconds">The non-negative duration.</param>
	public static GameplayAbilityPlanStep WaitDuration(float durationSeconds)
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.WaitDuration,
			durationSeconds,
			GameplayTag.None,
			GameplayEventMatch.Exact,
			GameplayTagWaitMode.Present,
			false,
			null,
			GameplayAbilityEffectTarget.Owner);
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
			0f,
			tag,
			match,
			GameplayTagWaitMode.Present,
			false,
			null,
			GameplayAbilityEffectTarget.Owner);
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
			0f,
			tag,
			GameplayEventMatch.Exact,
			mode,
			exact,
			null,
			GameplayAbilityEffectTarget.Owner);
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
			0f,
			GameplayTag.None,
			GameplayEventMatch.Exact,
			GameplayTagWaitMode.Present,
			false,
			effect,
			target);
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
			0f,
			tag,
			GameplayEventMatch.Exact,
			GameplayTagWaitMode.Present,
			false,
			null,
			GameplayAbilityEffectTarget.Owner);
	}

	/// <summary>
	/// Creates a step that ends the ability normally.
	/// </summary>
	public static GameplayAbilityPlanStep End()
	{
		return new GameplayAbilityPlanStep(
			GameplayAbilityPlanStepKind.End,
			0f,
			GameplayTag.None,
			GameplayEventMatch.Exact,
			GameplayTagWaitMode.Present,
			false,
			null,
			GameplayAbilityEffectTarget.Owner);
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
			_ => "End"
		};
	}
}
