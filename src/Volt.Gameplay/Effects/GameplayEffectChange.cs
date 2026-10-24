using Volt.Ecs;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Describes one gameplay effect state change.
/// </summary>
/// <remarks>
/// <para>
/// One event carries every change kind rather than one delegate per
/// kind, so a system that cares about a single effect can subscribe
/// once and filter. The payload is a value type, so the notification
/// allocates nothing.
/// </para>
/// <para>
/// The payload is delivered after the change is committed. A handler
/// may therefore query the effect system, and may apply or remove
/// effects; the runtime tolerates that re-entrancy.
/// </para>
/// </remarks>
public readonly struct GameplayEffectChange
{
	internal GameplayEffectChange(
		GameplayEffectChangeKind kind,
		GameplayEffectHandle handle,
		GameplayEffectDefinition definition,
		Entity target,
		Entity source,
		int stackCount,
		bool isInhibited = false)
	{
		Kind = kind;
		Handle = handle;
		Definition = definition;
		Target = target;
		Source = source;
		StackCount = stackCount;
		IsInhibited = isInhibited;
	}

	/// <summary>
	/// Gets what changed.
	/// </summary>
	public GameplayEffectChangeKind Kind { get; }

	/// <summary>
	/// Gets the affected instance.
	/// </summary>
	/// <remarks>
	/// The handle of a removed or expired instance is already dead;
	/// it identifies which instance ended rather than referring to
	/// live state.
	/// </remarks>
	public GameplayEffectHandle Handle { get; }

	/// <summary>
	/// Gets the shared definition.
	/// </summary>
	public GameplayEffectDefinition Definition { get; }

	/// <summary>
	/// Gets the affected entity.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the applying entity.
	/// </summary>
	public Entity Source { get; }

	/// <summary>
	/// Gets the stack count at the time of the change.
	/// </summary>
	public int StackCount { get; }

	/// <summary>Whether an applied or stack transition remains inhibited.</summary>
	public bool IsInhibited { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Kind} {Definition.Name} on {Target}, " +
			$"{StackCount} stack(s)";
	}
}
