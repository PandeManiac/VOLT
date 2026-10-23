using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Builds an immutable <see cref="GameplayAbilityPlan"/>.
/// </summary>
/// <remarks>
/// This is authoring code: it may allocate and throw on configuration
/// mistakes, because it runs during initialization. The built plan is
/// validated and shared by every activation of the definition.
/// </remarks>
public sealed class GameplayAbilityPlanBuilder
{
	private readonly List<GameplayAbilityPlanStep> _steps = [];

	internal GameplayAbilityPlanBuilder()
	{
	}

	/// <summary>
	/// Appends a step that waits for a simulation duration.
	/// </summary>
	/// <param name="seconds">The non-negative duration.</param>
	public GameplayAbilityPlanBuilder Wait(float seconds)
	{
		if (!float.IsFinite(seconds) || seconds < 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(seconds),
				seconds,
				"A plan wait must be finite and not negative.");
		}

		_steps.Add(GameplayAbilityPlanStep.WaitDuration(seconds));

		return this;
	}

	/// <summary>
	/// Appends a step that waits for a gameplay event.
	/// </summary>
	/// <param name="tag">The event tag.</param>
	/// <param name="match">The matching mode.</param>
	public GameplayAbilityPlanBuilder WaitEvent(
		GameplayTag tag,
		GameplayEventMatch match = GameplayEventMatch.Exact)
	{
		ValidateEventTag(tag);

		if (match is not (
			GameplayEventMatch.Exact or GameplayEventMatch.Hierarchical))
		{
			throw new ArgumentOutOfRangeException(
				nameof(match),
				match,
				"Unknown gameplay event match mode.");
		}

		_steps.Add(GameplayAbilityPlanStep.WaitEvent(tag, match));

		return this;
	}

	/// <summary>
	/// Appends a step that waits for a tag condition.
	/// </summary>
	/// <param name="tag">The tag to watch.</param>
	/// <param name="mode">Whether to wait for presence or absence.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant.
	/// </param>
	public GameplayAbilityPlanBuilder WaitTag(
		GameplayTag tag,
		GameplayTagWaitMode mode,
		bool exact = false)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A plan tag condition needs a registered tag.",
				nameof(tag));
		}

		if (mode is not (
			GameplayTagWaitMode.Present or GameplayTagWaitMode.Absent))
		{
			throw new ArgumentOutOfRangeException(
				nameof(mode),
				mode,
				"Unknown tag wait mode.");
		}

		_steps.Add(GameplayAbilityPlanStep.WaitTag(tag, mode, exact));

		return this;
	}

	/// <summary>
	/// Appends a step that applies a gameplay effect.
	/// </summary>
	/// <param name="effect">The effect to apply.</param>
	/// <param name="target">The entity to apply it to.</param>
	public GameplayAbilityPlanBuilder ApplyEffect(
		GameplayEffectDefinition effect,
		GameplayAbilityEffectTarget target = GameplayAbilityEffectTarget.Owner)
	{
		ArgumentNullException.ThrowIfNull(effect);

		if (target is not (
			GameplayAbilityEffectTarget.Owner
			or GameplayAbilityEffectTarget.Target))
		{
			throw new ArgumentOutOfRangeException(
				nameof(target),
				target,
				"Unknown ability effect target.");
		}

		_steps.Add(GameplayAbilityPlanStep.ApplyEffect(effect, target));

		return this;
	}

	/// <summary>
	/// Appends a step that publishes a gameplay event.
	/// </summary>
	/// <param name="tag">The event tag.</param>
	public GameplayAbilityPlanBuilder PublishEvent(GameplayTag tag)
	{
		ValidateEventTag(tag);

		_steps.Add(GameplayAbilityPlanStep.PublishEvent(tag));

		return this;
	}

	/// <summary>
	/// Appends a step that ends the ability normally.
	/// </summary>
	public GameplayAbilityPlanBuilder End()
	{
		_steps.Add(GameplayAbilityPlanStep.End());

		return this;
	}

	/// <summary>
	/// Validates and creates the plan.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The plan has more than <see cref="GameplayAbilityPlan.MaximumStepCount"/>
	/// steps.
	/// </exception>
	public GameplayAbilityPlan Build()
	{
		if (_steps.Count > GameplayAbilityPlan.MaximumStepCount)
		{
			throw new InvalidOperationException(
				$"An ability plan has at most " +
				$"{GameplayAbilityPlan.MaximumStepCount} steps. Split the " +
				$"behavior into multiple abilities instead.");
		}

		return new GameplayAbilityPlan([.. _steps]);
	}

	private static void ValidateEventTag(GameplayTag tag)
	{
		if (!tag.IsValid && tag != GameplayTag.None)
		{
			throw new ArgumentException(
				"A plan event step needs a registered tag.",
				nameof(tag));
		}
	}
}
