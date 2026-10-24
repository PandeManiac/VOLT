namespace Volt.Gameplay.Abilities;

/// <summary>
/// An immutable sequence of ability steps shared by every activation
/// of a definition.
/// </summary>
/// <remarks>
/// <para>
/// The plan is the ergonomic layer over the task runtime. Authoring it
/// allocates once; running it stores one cursor in the active grant and
/// creates the same compact tasks that the low-level API creates. There
/// is no second execution engine and no delegate per activation.
/// </para>
/// <para>
/// A plan that runs out of steps ends the ability normally. An explicit
/// <see cref="GameplayAbilityPlanStep.End"/> step does the same and
/// makes the intent visible. A cancelled or revoked activation stops
/// its plan immediately because all of its tasks are cancelled.
/// </para>
/// </remarks>
public sealed class GameplayAbilityPlan
{
	private const int MaxSteps = 32;

	private readonly GameplayAbilityPlanStep[] _steps;

	internal GameplayAbilityPlan(GameplayAbilityPlanStep[] steps)
	{
		_steps = steps;
	}

	/// <summary>
	/// Starts building a plan.
	/// </summary>
	public static GameplayAbilityPlanBuilder Create()
	{
		return new GameplayAbilityPlanBuilder();
	}

	/// <summary>
	/// Gets the number of steps.
	/// </summary>
	public int Count => _steps.Length;

	/// <summary>
	/// Gets whether the plan has no steps.
	/// </summary>
	public bool IsEmpty => _steps.Length == 0;

	/// <summary>
	/// Gets the maximum number of steps in one plan.
	/// </summary>
	public static int MaximumStepCount => MaxSteps;

	/// <summary>
	/// Gets the steps in execution order.
	/// </summary>
	public ReadOnlySpan<GameplayAbilityPlanStep> Steps => _steps;

	/// <summary>
	/// Gets one step by index.
	/// </summary>
	/// <param name="index">The step index.</param>
	public GameplayAbilityPlanStep this[int index] => _steps[index];

	/// <inheritdoc />
	public override string ToString()
	{
		return $"GameplayAbilityPlan({_steps.Length} steps)";
	}
}
