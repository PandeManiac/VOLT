using Volt.Gameplay.Cues;
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
	private readonly List<ConditionalScope> _conditions = [];

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

	/// <summary>Appends a one-shot presentation cue step.</summary>
	public GameplayAbilityPlanBuilder ExecuteCue(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException("A cue step needs a registered tag.", nameof(tag));
		}

		_steps.Add(GameplayAbilityPlanStep.ExecuteCue(tag));
		return this;
	}

	/// <summary>Appends a persistent cue step automatically owned by this activation.</summary>
	public GameplayAbilityPlanBuilder AddCue(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException("A cue step needs a registered tag.", nameof(tag));
		}

		_steps.Add(GameplayAbilityPlanStep.AddCue(tag));
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
	/// Appends a step that waits for the first of several sources.
	/// </summary>
	/// <param name="wait">The composite wait description.</param>
	/// <exception cref="ArgumentNullException">
	/// The wait is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The wait has no source.
	/// </exception>
	/// <remarks>
	/// The first source to fire completes the step: the timeout elapses,
	/// a watched event is published, or the tag condition holds.
	/// </remarks>
	public GameplayAbilityPlanBuilder WaitAny(GameplayWaitAny wait)
	{
		ArgumentNullException.ThrowIfNull(wait);
		wait.Validate();

		_steps.Add(GameplayAbilityPlanStep.WaitAny(wait));

		return this;
	}

	/// <summary>
	/// Appends a step that waits for a registered custom task.
	/// </summary>
	/// <param name="definition">The registered definition.</param>
	/// <exception cref="ArgumentNullException">
	/// The definition is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// The step starts the task and continues when application code
	/// completes its handle. Cancelling or ending the ability releases
	/// the task like any other wait.
	/// </remarks>
	public GameplayAbilityPlanBuilder WaitCustom(
		GameplayAbilityTaskDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);

		_steps.Add(GameplayAbilityPlanStep.WaitCustom(definition));

		return this;
	}

	/// <summary>
	/// Begins a conditional block.
	/// </summary>
	/// <param name="condition">The condition to evaluate.</param>
	/// <exception cref="ArgumentNullException">
	/// The condition is <see langword="null"/>.
	/// </exception>
	/// <remarks>
	/// <para>
	/// The steps added after this call are the true branch. An optional
	/// <see cref="Else"/> starts the false branch, and
	/// <see cref="EndIf"/> closes the block. The builder compiles the
	/// block into forward jumps when the plan is built, so the running
	/// plan stays a flat array with a cursor.
	/// </para>
	/// <para>
	/// Only forward jumps are produced, so a plan cannot loop and always
	/// terminates. Conditions are evaluated when the branch step runs,
	/// against live state.
	/// </para>
	/// </remarks>
	public GameplayAbilityPlanBuilder If(GameplayCondition condition)
	{
		ArgumentNullException.ThrowIfNull(condition);

		_conditions.Add(new ConditionalScope
		{
			BranchIndex = _steps.Count,
			GotoIndex = -1
		});

		_steps.Add(GameplayAbilityPlanStep.Branch(condition, -1));

		return this;
	}

	/// <summary>
	/// Starts the false branch of the innermost conditional block.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// There is no open <see cref="If"/>, or it already has an
	/// <see cref="Else"/>.
	/// </exception>
	public GameplayAbilityPlanBuilder Else()
	{
		if (_conditions.Count == 0)
		{
			throw new InvalidOperationException(
				"Else() needs an open If(). Add If(condition) first.");
		}

		ConditionalScope scope = _conditions[^1];

		if (scope.GotoIndex >= 0)
		{
			throw new InvalidOperationException(
				"An If block has at most one Else().");
		}

		int gotoIndex = _steps.Count;

		_steps.Add(GameplayAbilityPlanStep.Goto(-1));

		// The false branch starts after the jump that ends the true
		// branch.
		_steps[scope.BranchIndex] =
			_steps[scope.BranchIndex].WithTargetStep(gotoIndex + 1);

		scope.GotoIndex = gotoIndex;

		_conditions[^1] = scope;

		return this;
	}

	/// <summary>
	/// Closes the innermost conditional block.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// There is no open <see cref="If"/>.
	/// </exception>
	public GameplayAbilityPlanBuilder EndIf()
	{
		if (_conditions.Count == 0)
		{
			throw new InvalidOperationException(
				"EndIf() needs an open If(). Add If(condition) first.");
		}

		ConditionalScope scope = _conditions[^1];

		int endIndex = _steps.Count;

		if (scope.GotoIndex >= 0)
		{
			_steps[scope.GotoIndex] =
				_steps[scope.GotoIndex].WithTargetStep(endIndex);
		}
		else
		{
			_steps[scope.BranchIndex] =
				_steps[scope.BranchIndex].WithTargetStep(endIndex);
		}

		_conditions.RemoveAt(_conditions.Count - 1);

		return this;
	}

	/// <summary>
	/// Validates and creates the plan.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The plan has more than
	/// <see cref="GameplayAbilityPlan.MaximumStepCount"/> steps, a
	/// conditional block was not closed, or a jump target is invalid.
	/// </exception>
	public GameplayAbilityPlan Build()
	{
		if (_conditions.Count > 0)
		{
			throw new InvalidOperationException(
				"An ability plan has an If without a matching EndIf. " +
				"Every conditional block must be closed before Build.");
		}

		if (_steps.Count > GameplayAbilityPlan.MaximumStepCount)
		{
			throw new InvalidOperationException(
				$"An ability plan has at most " +
				$"{GameplayAbilityPlan.MaximumStepCount} steps. Split the " +
				$"behavior into multiple abilities instead.");
		}

		ValidateJumps();

		return new GameplayAbilityPlan([.. _steps]);
	}

	/// <summary>
	/// Rejects jump targets that are outside the plan or point
	/// backwards.
	/// </summary>
	/// <remarks>
	/// Backward jumps would let an authored plan run forever inside one
	/// activation step, so the builder only produces forward jumps and
	/// verifies that invariant here.
	/// </remarks>
	private void ValidateJumps()
	{
		for (int index = 0; index < _steps.Count; index++)
		{
			GameplayAbilityPlanStep step = _steps[index];

			if (step.Kind is not (
				GameplayAbilityPlanStepKind.Branch
				or GameplayAbilityPlanStepKind.Goto))
			{
				continue;
			}

			if (step.TargetStep <= index
				|| step.TargetStep > _steps.Count)
			{
				throw new InvalidOperationException(
					$"Plan step {index} ({step.Kind}) jumps to {step.TargetStep}, " +
					$"but a jump must move forward within 0..{_steps.Count}. " +
					"Plans cannot loop.");
			}
		}
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

	private struct ConditionalScope
	{
		public int BranchIndex;
		public int GotoIndex;
	}
}
