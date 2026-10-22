using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Describes one ability state change.
/// </summary>
/// <remarks>
/// <para>
/// One event carries every change kind rather than one delegate per
/// kind, so a listener that cares about a single ability can subscribe
/// once and filter. The payload is a value type, so the notification
/// allocates nothing.
/// </para>
/// <para>
/// The payload is delivered after the change is committed. A handler
/// may therefore query the ability system, and may grant, revoke,
/// activate, or cancel abilities; the runtime tolerates that
/// re-entrancy.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityChange
{
	internal GameplayAbilityChange(
		GameplayAbilityChangeKind kind,
		GameplayAbilityHandle handle,
		GameplayAbilityDefinition definition,
		Entity owner,
		Entity target,
		int level)
	{
		Kind = kind;
		Handle = handle;
		Definition = definition;
		Owner = owner;
		Target = target;
		Level = level;
	}

	/// <summary>
	/// Gets what changed.
	/// </summary>
	public GameplayAbilityChangeKind Kind { get; }

	/// <summary>
	/// Gets the affected grant.
	/// </summary>
	/// <remarks>
	/// The handle of a revoked grant is already dead; it identifies
	/// which grant ended rather than referring to live state.
	/// </remarks>
	public GameplayAbilityHandle Handle { get; }

	/// <summary>
	/// Gets the shared definition.
	/// </summary>
	public GameplayAbilityDefinition Definition { get; }

	/// <summary>
	/// Gets the entity that owns the ability.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the activation target, or <c>default(Entity)</c> when the
	/// ability has not activated or was not targeted.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the level of the grant.
	/// </summary>
	public int Level { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Kind} {Definition.Name} on {Owner}, level {Level}";
	}
}
