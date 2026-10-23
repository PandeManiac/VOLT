using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// An immutable boolean condition over live gameplay state.
/// </summary>
/// <remarks>
/// <para>
/// Conditions are authoring-time values. A plan holds one shared
/// instance, and every activation evaluates that instance against its
/// own owner and target, so a thousand simultaneous activations do not
/// copy a condition tree. The tree is validated when it is built and
/// never captures a mutable authoring object.
/// </para>
/// <para>
/// Evaluation reads live state: current tag grants, cached attribute
/// values, and whether the target still exists. A condition is
/// therefore evaluated at the moment its step runs, not when the plan
/// was written, and a callback that changes state between two steps is
/// observed by the next step.
/// </para>
/// <para>
/// The tree is bounded: at most <see cref="MaximumNodeCount"/> nodes and
/// <see cref="MaximumDepth"/> levels of nesting. Evaluation walks at
/// most that many nodes, allocates nothing, and does not recurse
/// without bound.
/// </para>
/// </remarks>
public sealed class GameplayCondition
{
	/// <summary>
	/// The maximum number of condition nodes in one tree.
	/// </summary>
	public const int MaximumNodeCount = 32;

	/// <summary>
	/// The maximum nesting depth of one condition tree.
	/// </summary>
	public const int MaximumDepth = 8;

	private readonly GameplayCondition[] _children;
	private readonly GameplayTag _tag;
	private readonly GameplayAttribute _attribute;
	private readonly float _value;

	private GameplayCondition(
		GameplayConditionKind kind,
		GameplayTag tag,
		GameplayAttribute attribute,
		GameplayAttributeComparison comparison,
		float value,
		bool exact,
		GameplayCondition[] children,
		int nodeCount,
		int depth)
	{
		Kind = kind;
		_tag = tag;
		_attribute = attribute;
		Comparison = comparison;
		_value = value;
		Exact = exact;
		_children = children;
		NodeCount = nodeCount;
		Depth = depth;
	}

	/// <summary>
	/// Gets the kind of condition.
	/// </summary>
	public GameplayConditionKind Kind { get; }

	/// <summary>
	/// Gets the attribute comparison used by an attribute condition.
	/// </summary>
	public GameplayAttributeComparison Comparison { get; }

	/// <summary>
	/// Gets whether a tag condition requires an explicit grant.
	/// </summary>
	public bool Exact { get; }

	/// <summary>
	/// Gets the number of nodes in this tree.
	/// </summary>
	public int NodeCount { get; }

	/// <summary>
	/// Gets the nesting depth of this tree, where a leaf is one.
	/// </summary>
	public int Depth { get; }

	/// <summary>
	/// Creates a condition that requires the owner to have a tag.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant;
	/// <see langword="false"/> to accept a granted descendant.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public static GameplayCondition OwnerHasTag(
		GameplayTag tag,
		bool exact = false)
	{
		ValidateTag(tag);

		return new GameplayCondition(
			GameplayConditionKind.OwnerHasTag,
			tag,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			exact,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that requires the owner to lack a tag.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant;
	/// <see langword="false"/> to accept a granted descendant.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public static GameplayCondition OwnerMissingTag(
		GameplayTag tag,
		bool exact = false)
	{
		ValidateTag(tag);

		return new GameplayCondition(
			GameplayConditionKind.OwnerMissingTag,
			tag,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			exact,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that requires the activation target to have
	/// a tag. It is false when the target no longer exists.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant;
	/// <see langword="false"/> to accept a granted descendant.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public static GameplayCondition TargetHasTag(
		GameplayTag tag,
		bool exact = false)
	{
		ValidateTag(tag);

		return new GameplayCondition(
			GameplayConditionKind.TargetHasTag,
			tag,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			exact,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that requires the activation target to lack
	/// a tag. It is false when the target no longer exists.
	/// </summary>
	/// <param name="tag">The tag to test for.</param>
	/// <param name="exact">
	/// <see langword="true"/> to require an explicit grant;
	/// <see langword="false"/> to accept a granted descendant.
	/// </param>
	/// <exception cref="ArgumentException">
	/// The tag is not registered.
	/// </exception>
	public static GameplayCondition TargetMissingTag(
		GameplayTag tag,
		bool exact = false)
	{
		ValidateTag(tag);

		return new GameplayCondition(
			GameplayConditionKind.TargetMissingTag,
			tag,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			exact,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that requires the activation target to still
	/// exist in the world.
	/// </summary>
	public static GameplayCondition TargetAlive()
	{
		return new GameplayCondition(
			GameplayConditionKind.TargetAlive,
			GameplayTag.None,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			false,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that compares one of the owner's attributes
	/// with a constant.
	/// </summary>
	/// <param name="attribute">The attribute to read.</param>
	/// <param name="comparison">The comparison to perform.</param>
	/// <param name="value">The constant to compare with.</param>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The comparison is unknown, or the constant is not finite.
	/// </exception>
	public static GameplayCondition OwnerAttribute(
		GameplayAttribute attribute,
		GameplayAttributeComparison comparison,
		float value)
	{
		ValidateAttribute(attribute);
		ValidateComparison(comparison);
		ValidateValue(value);

		return new GameplayCondition(
			GameplayConditionKind.OwnerAttribute,
			GameplayTag.None,
			attribute,
			comparison,
			value,
			false,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that compares one of the activation target's
	/// attributes with a constant. It is false when the target no
	/// longer exists.
	/// </summary>
	/// <param name="attribute">The attribute to read.</param>
	/// <param name="comparison">The comparison to perform.</param>
	/// <param name="value">The constant to compare with.</param>
	/// <exception cref="ArgumentException">
	/// The attribute is not registered.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// The comparison is unknown, or the constant is not finite.
	/// </exception>
	public static GameplayCondition TargetAttribute(
		GameplayAttribute attribute,
		GameplayAttributeComparison comparison,
		float value)
	{
		ValidateAttribute(attribute);
		ValidateComparison(comparison);
		ValidateValue(value);

		return new GameplayCondition(
			GameplayConditionKind.TargetAttribute,
			GameplayTag.None,
			attribute,
			comparison,
			value,
			false,
			[],
			1,
			1);
	}

	/// <summary>
	/// Creates a condition that requires every child condition to hold.
	/// </summary>
	/// <param name="conditions">The child conditions, at least one.</param>
	/// <exception cref="ArgumentNullException">
	/// The array or a child is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The array is empty.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The combined tree exceeds the node or depth limit.
	/// </exception>
	/// <remarks>
	/// Children are evaluated in order and evaluation stops at the
	/// first one that is false.
	/// </remarks>
	public static GameplayCondition All(
		params GameplayCondition[] conditions)
	{
		return Combine(GameplayConditionKind.All, conditions);
	}

	/// <summary>
	/// Creates a condition that requires at least one child condition
	/// to hold.
	/// </summary>
	/// <param name="conditions">The child conditions, at least one.</param>
	/// <exception cref="ArgumentNullException">
	/// The array or a child is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The array is empty.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The combined tree exceeds the node or depth limit.
	/// </exception>
	/// <remarks>
	/// Children are evaluated in order and evaluation stops at the
	/// first one that holds.
	/// </remarks>
	public static GameplayCondition Any(
		params GameplayCondition[] conditions)
	{
		return Combine(GameplayConditionKind.Any, conditions);
	}

	/// <summary>
	/// Creates a condition that inverts another condition.
	/// </summary>
	/// <param name="condition">The condition to invert.</param>
	/// <exception cref="ArgumentNullException">
	/// The condition is <see langword="null"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The combined tree exceeds the node or depth limit.
	/// </exception>
	public static GameplayCondition Not(GameplayCondition condition)
	{
		ArgumentNullException.ThrowIfNull(condition);

		return Combine(GameplayConditionKind.Not, [condition]);
	}

	/// <summary>
	/// Evaluates the condition against live gameplay state.
	/// </summary>
	/// <param name="context">The owner, target, and stores.</param>
	/// <returns>Whether the condition holds.</returns>
	internal bool Evaluate(in GameplayConditionContext context)
	{
		switch (Kind)
		{
			case GameplayConditionKind.OwnerHasTag:
				return HasTag(context.Tags, context.Owner);

			case GameplayConditionKind.OwnerMissingTag:
				return !HasTag(context.Tags, context.Owner);

			case GameplayConditionKind.TargetHasTag:
				return context.TargetAlive
					&& HasTag(context.Tags, context.Target);

			case GameplayConditionKind.TargetMissingTag:
				return context.TargetAlive
					&& !HasTag(context.Tags, context.Target);

			case GameplayConditionKind.TargetAlive:
				return context.TargetAlive;

			case GameplayConditionKind.OwnerAttribute:
				return Compare(
					context.Attributes.GetCurrentValue(
						context.Owner,
						_attribute));

			case GameplayConditionKind.TargetAttribute:
				return context.TargetAlive
					&& Compare(
						context.Attributes.GetCurrentValue(
							context.Target,
							_attribute));

			case GameplayConditionKind.All:
				for (int index = 0; index < _children.Length; index++)
				{
					if (!_children[index].Evaluate(in context))
					{
						return false;
					}
				}

				return true;

			case GameplayConditionKind.Any:
				for (int index = 0; index < _children.Length; index++)
				{
					if (_children[index].Evaluate(in context))
					{
						return true;
					}
				}

				return false;

			case GameplayConditionKind.Not:
				return !_children[0].Evaluate(in context);

			default:
				return false;
		}
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Kind switch
		{
			GameplayConditionKind.OwnerHasTag => $"OwnerHasTag({_tag})",
			GameplayConditionKind.OwnerMissingTag =>
				$"OwnerMissingTag({_tag})",
			GameplayConditionKind.TargetHasTag => $"TargetHasTag({_tag})",
			GameplayConditionKind.TargetMissingTag =>
				$"TargetMissingTag({_tag})",
			GameplayConditionKind.TargetAlive => "TargetAlive()",
			GameplayConditionKind.OwnerAttribute =>
				$"OwnerAttribute({_attribute} {Comparison} {_value})",
			GameplayConditionKind.TargetAttribute =>
				$"TargetAttribute({_attribute} {Comparison} {_value})",
			GameplayConditionKind.All => $"All({_children.Length})",
			GameplayConditionKind.Any => $"Any({_children.Length})",
			_ => $"Not({_children[0]})"
		};
	}

	private static GameplayCondition Combine(
		GameplayConditionKind kind,
		GameplayCondition[] conditions)
	{
		ArgumentNullException.ThrowIfNull(conditions);

		if (conditions.Length == 0)
		{
			throw new ArgumentException(
				"A combined condition needs at least one child. All() " +
				"without children would always hold and Any() without " +
				"children would never hold, which is almost always a " +
				"mistake.",
				nameof(conditions));
		}

		int nodeCount = 1;
		int depth = 1;

		for (int index = 0; index < conditions.Length; index++)
		{
			GameplayCondition child = conditions[index]
				?? throw new ArgumentNullException(
					nameof(conditions),
					"A condition tree cannot contain null children.");

			// A child cannot change after it is constructed; still, copy
			// every node into the new array below rather than retaining the
			// caller's mutable params array.
			nodeCount += child.NodeCount;
			depth = Math.Max(depth, child.Depth + 1);
		}

		GameplayCondition[] children = new GameplayCondition[conditions.Length];
		Array.Copy(conditions, children, conditions.Length);

		if (nodeCount > MaximumNodeCount)
		{
			throw new InvalidOperationException(
				$"A condition tree has at most {MaximumNodeCount} " +
				"nodes. Split the rule into simpler conditions.");
		}

		if (depth > MaximumDepth)
		{
			throw new InvalidOperationException(
				$"A condition tree nests at most {MaximumDepth} levels. " +
				"Flatten the rule or split it into separate steps.");
		}

		return new GameplayCondition(
			kind,
			GameplayTag.None,
			default,
			GameplayAttributeComparison.Equal,
			0f,
			false,
			children,
			nodeCount,
			depth);
	}

	private bool HasTag(GameplayTagStore tags, Entity entity)
	{
		return Exact
			? tags.HasTagExact(entity, _tag)
			: tags.HasTag(entity, _tag);
	}

	private bool Compare(float current)
	{
		return Comparison switch
		{
			GameplayAttributeComparison.LessThan => current < _value,
			GameplayAttributeComparison.LessThanOrEqual =>
				current <= _value,
			GameplayAttributeComparison.GreaterThan => current > _value,
			GameplayAttributeComparison.GreaterThanOrEqual =>
				current >= _value,
			GameplayAttributeComparison.Equal => current == _value,
			_ => current != _value
		};
	}

	private static void ValidateTag(GameplayTag tag)
	{
		if (!tag.IsValid)
		{
			throw new ArgumentException(
				"A tag condition needs a registered tag.",
				nameof(tag));
		}
	}

	private static void ValidateAttribute(GameplayAttribute attribute)
	{
		if (!attribute.IsValid)
		{
			throw new ArgumentException(
				"An attribute condition needs a registered attribute.",
				nameof(attribute));
		}
	}

	private static void ValidateComparison(
		GameplayAttributeComparison comparison)
	{
		if (comparison is < GameplayAttributeComparison.LessThan
			or > GameplayAttributeComparison.NotEqual)
		{
			throw new ArgumentOutOfRangeException(
				nameof(comparison),
				comparison,
				"Unknown attribute comparison.");
		}
	}

	private static void ValidateValue(float value)
	{
		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				value,
				"An attribute condition constant must be finite.");
		}
	}
}
